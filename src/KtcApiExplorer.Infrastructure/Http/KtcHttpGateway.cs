using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using KtcApiExplorer.Application.Abstractions;
using KtcApiExplorer.Application.Dtos;
using KtcApiExplorer.Application.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace KtcApiExplorer.Infrastructure.Http;

/// <summary>
/// Outbound calls to the KTC REST API via IHttpClientFactory. Redirects are NOT followed (a redirect could carry the bearer token
/// to another host), responses are size-capped, and network failures are returned as data rather than thrown.
/// </summary>
public class KtcHttpGateway : IApiHttpClient
{
    public const string ClientName = "ktc";
    readonly IHttpClientFactory _factory; readonly ExecutionOptions _opt; readonly KtcOptions _ktc; readonly ILogger<KtcHttpGateway> _log;

    public KtcHttpGateway(IHttpClientFactory factory, IOptions<ExecutionOptions> opt, IOptions<KtcOptions> ktc, ILogger<KtcHttpGateway> log)
    { _factory = factory; _opt = opt.Value; _ktc = ktc.Value; _log = log; }

    public async Task<ApiHttpResponse> SendAsync(ApiHttpRequest req, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var result = new ApiHttpResponse();
        try
        {
            var client = _factory.CreateClient(ClientName);
            using var msg = new HttpRequestMessage(new HttpMethod(req.Method), req.Url);

            string? contentType = req.Headers.FirstOrDefault(h => h.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))?.Value;
            if (req.Body != null) msg.Content = BuildContent(req, contentType);
            foreach (var h in req.Headers)
            {
                if (h.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)) continue;
                if (!msg.Headers.TryAddWithoutValidation(h.Key, h.Value)) msg.Content?.Headers.TryAddWithoutValidation(h.Key, h.Value);
            }

            using var resp = await client.SendAsync(msg, HttpCompletionOption.ResponseHeadersRead, ct);
            result.StatusCode = (int)resp.StatusCode;
            result.ReasonPhrase = resp.ReasonPhrase;
            foreach (var h in resp.Headers) result.Headers.Add(new KeyValueDto { Key = h.Key, Value = string.Join(", ", h.Value) });
            foreach (var h in resp.Content.Headers) result.Headers.Add(new KeyValueDto { Key = h.Key, Value = string.Join(", ", h.Value) });
            result.ContentType = resp.Content.Headers.ContentType?.ToString();

            // stream with a hard cap
            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var ms = new MemoryStream();
            var buf = new byte[81920]; int n; long total = 0;
            while ((n = await stream.ReadAsync(buf, ct)) > 0)
            {
                total += n;
                if (ms.Length + n > _opt.MaxResponseBytes) { ms.Write(buf, 0, (int)(_opt.MaxResponseBytes - ms.Length)); result.Truncated = true; break; }
                ms.Write(buf, 0, n);
            }
            result.SizeBytes = result.Truncated ? total : ms.Length;
            var bytes = ms.ToArray();
            if (IsText(result.ContentType, bytes)) result.Body = Encoding.UTF8.GetString(bytes);
            else if (bytes.Length > 0) { result.IsBinary = true; result.BodyBase64 = Convert.ToBase64String(bytes); }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            result.Error = $"The request timed out after {_ktc.RequestTimeoutSeconds}s.";
        }
        catch (HttpRequestException ex)
        {
            result.Error = Describe(ex);
            _log.LogWarning("KTC request failed: {Method} {Host} - {Error}", req.Method, SafeHost(req.Url), result.Error);
        }
        catch (InvalidOperationException ex) { result.Error = "Invalid request: " + ex.Message; }
        finally { sw.Stop(); result.DurationMs = sw.ElapsedMilliseconds; }
        return result;
    }

    static HttpContent BuildContent(ApiHttpRequest req, string? contentType)
    {
        switch (req.BodyMode)
        {
            case "urlencoded":
                return new FormUrlEncodedContent(Pairs(req.Body!).Select(p => new KeyValuePair<string, string>(p.Key, p.Value)));
            case "formdata":
                var mp = new MultipartFormDataContent();
                foreach (var p in Pairs(req.Body!)) mp.Add(new StringContent(p.Value), p.Key);
                return mp;
            default:
                var sc = new StringContent(req.Body!, Encoding.UTF8);
                sc.Headers.ContentType = MediaTypeHeaderValue.TryParse(contentType, out var mt) ? mt : new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
                return sc;
        }
    }

    static List<KeyValueDto> Pairs(string body)
    {
        try { return (JsonConvert.DeserializeObject<List<KeyValueDto>>(body) ?? new()).Where(p => p.Enabled && !string.IsNullOrEmpty(p.Key)).ToList(); }
        catch (JsonException) { return new(); }
    }

    static bool IsText(string? ct, byte[] bytes)
    {
        if (ct != null && (ct.Contains("json", StringComparison.OrdinalIgnoreCase) || ct.Contains("xml", StringComparison.OrdinalIgnoreCase) || ct.Contains("text", StringComparison.OrdinalIgnoreCase) ||
                           ct.Contains("javascript", StringComparison.OrdinalIgnoreCase) || ct.Contains("x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase))) return true;
        if (ct != null && (ct.Contains("octet-stream") || ct.Contains("image/") || ct.Contains("pdf") || ct.Contains("zip"))) return false;
        return !bytes.Take(2048).Contains((byte)0);   // unknown type: heuristic
    }

    static string Describe(HttpRequestException ex)
    {
        var inner = ex.InnerException;
        if (inner is System.Security.Authentication.AuthenticationException) return "TLS/certificate error talking to the KTC server (" + inner.Message + "). For a self-signed test certificate set Ktc:AllowInvalidCertificates=true (development only).";
        if (inner is System.Net.Sockets.SocketException se) return $"Cannot connect to the KTC server ({se.SocketErrorCode}).";
        return ex.Message;
    }
    static string SafeHost(string url) => Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Authority : "?";
}
