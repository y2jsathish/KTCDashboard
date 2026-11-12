using System.Text.RegularExpressions;
using KtcApiExplorer.Application.Abstractions;
using KtcApiExplorer.Application.Common;
using KtcApiExplorer.Application.Dtos;
using KtcApiExplorer.Domain.Entities;
using Newtonsoft.Json;

namespace KtcApiExplorer.Application.Services;

/// <summary>Builds the outbound request from an endpoint definition + user input. Pure, so it is shared by execution and code generation.</summary>
public static class RequestBuilder
{
    public const string TokenPlaceholder = "<ACCESS_TOKEN>";
    static readonly HashSet<string> Forbidden = new(StringComparer.OrdinalIgnoreCase) { "host", "content-length", "transfer-encoding", "connection", "upgrade", "te", "expect" };
    static readonly Regex Var = new(@"\{\{\s*([\w.\-]+)\s*\}\}", RegexOptions.Compiled);

    public static List<KeyValueDto> ReadList(string? json) =>
        string.IsNullOrWhiteSpace(json) ? new() : (JsonConvert.DeserializeObject<List<KeyValueDto>>(json) ?? new());

    public static ApiHttpRequest Build(ApiEndpoint ep, string baseUrl, ExecuteRequestDto input, IReadOnlyDictionary<string, string> vars)
    {
        if (string.IsNullOrWhiteSpace(baseUrl)) throw new ValidationException("The collection has no base URL configured.");
        var baseUri = new Uri(baseUrl.TrimEnd('/') + "/", UriKind.Absolute);

        // path params
        var route = Regex.Replace(ep.RouteTemplate, @"\{([^}/]+)\}", m =>
        {
            input.PathParams.TryGetValue(m.Groups[1].Value, out var v);
            if (string.IsNullOrWhiteSpace(v)) throw new ValidationException($"Path parameter '{m.Groups[1].Value}' is required.");
            return Uri.EscapeDataString(v.Trim());
        });

        // query
        var qs = string.Join("&", input.Query
            .Where(q => q.Enabled && !string.IsNullOrWhiteSpace(q.Key) && !string.IsNullOrEmpty(q.Value))
            .Select(q => Uri.EscapeDataString(q.Key) + "=" + Uri.EscapeDataString(q.Value)));
        var missing = input.Query.Where(q => q.Required && q.Enabled && string.IsNullOrEmpty(q.Value)).Select(q => q.Key).ToList();
        if (missing.Count > 0) throw new ValidationException("Required query parameter(s) missing: " + string.Join(", ", missing));

        var url = new Uri(baseUri, route + (qs.Length > 0 ? "?" + qs : ""));
        // SSRF / token-leak guard: the final URL must stay on the configured KTC host and below its base path
        if (!string.Equals(url.Scheme, baseUri.Scheme, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(url.Authority, baseUri.Authority, StringComparison.OrdinalIgnoreCase) ||
            !url.AbsolutePath.StartsWith(baseUri.AbsolutePath, StringComparison.Ordinal))
            throw new ValidationException("The resulting URL leaves the configured KTC base URL and was blocked.");

        // headers: endpoint defaults, then user overrides; auth is always driven by the session
        var headers = new List<KeyValueDto>();
        foreach (var h in ReadList(ep.HeadersJson).Concat(input.Headers))
        {
            if (!h.Enabled || string.IsNullOrWhiteSpace(h.Key) || Forbidden.Contains(h.Key) ||
                h.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase)) continue;
            headers.RemoveAll(x => x.Key.Equals(h.Key, StringComparison.OrdinalIgnoreCase));
            headers.Add(new KeyValueDto { Key = h.Key.Trim(), Value = Substitute(h.Value, vars) });
        }
        if (vars.TryGetValue("access_token", out var tok) && !string.IsNullOrEmpty(tok))
            headers.Insert(0, new KeyValueDto { Key = "Authorization", Value = "Bearer " + tok });

        var body = input.Body;
        var mode = ep.BodyMode;
        if (string.IsNullOrWhiteSpace(body) && ep.Method is "GET" or "HEAD") body = null;
        if (body != null) body = Substitute(body, vars);
        if (!string.IsNullOrWhiteSpace(body) && mode is null or "") mode = "raw";
        if (!string.IsNullOrWhiteSpace(body) && mode == "raw" &&
            !headers.Any(h => h.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)))
            headers.Add(new KeyValueDto { Key = "Content-Type", Value = "application/json" });

        return new ApiHttpRequest { Method = ep.Method, Url = url.AbsoluteUri, Headers = headers, Body = string.IsNullOrWhiteSpace(body) ? null : body, BodyMode = string.IsNullOrWhiteSpace(body) ? null : mode };
    }

    public static string Substitute(string input, IReadOnlyDictionary<string, string> vars) =>
        string.IsNullOrEmpty(input) ? input : Var.Replace(input, m => vars.TryGetValue(m.Groups[1].Value, out var v) ? v : m.Value);

    /// <summary>route plus documented query parameters, e.g. api/v1/Campaign?name={name}&status={status}</summary>
    public static string DisplayRoute(ApiEndpoint e)
    {
        var q = ReadList(e.QueryParamsJson).Where(x => !string.IsNullOrWhiteSpace(x.Key)).Select(x => x.Required ? $"{x.Key}={{{x.Key}}}" : x.Key + "=" + x.Value).ToList();
        return "/" + e.RouteTemplate + (q.Count > 0 ? "?" + string.Join("&", q) : "");
    }

    public static bool IsSafeMethod(string method) => method is "GET" or "HEAD" or "OPTIONS";
}
