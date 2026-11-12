using System.Text;
using KtcApiExplorer.Application.Abstractions;
using KtcApiExplorer.Application.Services;
using Newtonsoft.Json;

namespace KtcApiExplorer.Application.CodeGen;

/// <summary>
/// Client code generators. They receive a request built with RequestBuilder.TokenPlaceholder as the bearer token
/// and emit an environment-variable / variable reference instead, so real tokens never reach the clipboard.
/// </summary>
public static class CodeGenerators
{
    static string Q(string s, string quote) => quote + s.Replace("\\", "\\\\").Replace(quote, "\\" + quote).Replace("\r", "\\r").Replace("\n", "\\n") + quote;

    static string TokenAwareJoin(string value, Func<string, string> lit, string tokenExpr, string concat)
    {
        if (!value.Contains(RequestBuilder.TokenPlaceholder)) return lit(value);
        var parts = value.Split(RequestBuilder.TokenPlaceholder);
        return string.Join(concat, parts.SelectMany((p, i) => i == 0 ? new[] { lit(p) } : new[] { tokenExpr, lit(p) }).Where(x => x != "\"\"" && x != "''" && x != "``"));
    }

    // ---------- curl ----------
    public static string Curl(ApiHttpRequest r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# export KTC_ACCESS_TOKEN=<token from KTC login>");
        sb.Append($"curl -X {r.Method} {ShQuote(r.Url)}");
        foreach (var h in r.Headers)
        {
            var line = $"{h.Key}: {h.Value}";
            sb.Append(" \\\n  -H ");
            sb.Append(line.Contains(RequestBuilder.TokenPlaceholder) ? "\"" + line.Replace("\"", "\\\"").Replace(RequestBuilder.TokenPlaceholder, "$KTC_ACCESS_TOKEN") + "\"" : ShQuote(line));
        }
        if (r.Body != null)
        {
            if (r.BodyMode == "urlencoded") foreach (var kv in Pairs(r.Body)) sb.Append(" \\\n  --data-urlencode ").Append(ShQuote(kv.Key + "=" + kv.Value));
            else if (r.BodyMode == "formdata") foreach (var kv in Pairs(r.Body)) sb.Append(" \\\n  -F ").Append(ShQuote(kv.Key + "=" + kv.Value));
            else sb.Append(" \\\n  --data-raw ").Append(ShQuote(Compact(r.Body)));
        }
        return sb.ToString();
    }

    // ---------- C# HttpClient ----------
    public static string CSharpHttpClient(ApiHttpRequest r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("using System.Net.Http;\nusing System.Net.Http.Headers;\nusing System.Text;\n");
        sb.AppendLine("var token = Environment.GetEnvironmentVariable(\"KTC_ACCESS_TOKEN\"); // obtained from POST api/v1/Login\n");
        sb.AppendLine("using var client = new HttpClient();   // in production inject IHttpClientFactory");
        sb.AppendLine($"using var request = new HttpRequestMessage(new HttpMethod({Q(r.Method, "\"")}), {Q(r.Url, "\"")});");
        foreach (var h in r.Headers.Where(h => !h.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)))
        {
            if (h.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase) && h.Value.StartsWith("Bearer "))
                sb.AppendLine("request.Headers.Authorization = new AuthenticationHeaderValue(\"Bearer\", token);");
            else sb.AppendLine($"request.Headers.TryAddWithoutValidation({Q(h.Key, "\"")}, {Q(h.Value, "\"")});");
        }
        if (r.Body != null)
        {
            var ct = r.Headers.FirstOrDefault(h => h.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))?.Value ?? "application/json";
            if (r.BodyMode is "urlencoded")
                sb.AppendLine("request.Content = new FormUrlEncodedContent(new Dictionary<string, string>\n{\n" + string.Join("\n", Pairs(r.Body).Select(p => $"    [{Q(p.Key, "\"")}] = {Q(p.Value, "\"")},")) + "\n});");
            else
            {
                sb.AppendLine("var json = \"\"\"\n" + Pretty(r.Body) + "\n\"\"\";");
                sb.AppendLine($"request.Content = new StringContent(json, Encoding.UTF8, {Q(ct.Split(';')[0], "\"")});");
            }
        }
        sb.AppendLine("\nusing var response = await client.SendAsync(request);");
        sb.AppendLine("var body = await response.Content.ReadAsStringAsync();");
        sb.AppendLine("Console.WriteLine($\"{(int)response.StatusCode} {response.ReasonPhrase}\\n{body}\");");
        return sb.ToString();
    }

    // ---------- C# RestSharp ----------
    public static string CSharpRestSharp(ApiHttpRequest r)
    {
        var uri = new Uri(r.Url);
        var sb = new StringBuilder();
        sb.AppendLine("using RestSharp;\n");
        sb.AppendLine("var token = Environment.GetEnvironmentVariable(\"KTC_ACCESS_TOKEN\"); // obtained from POST api/v1/Login\n");
        sb.AppendLine($"var client = new RestClient({Q(uri.GetLeftPart(UriPartial.Path), "\"")});");
        sb.AppendLine($"var request = new RestRequest(string.Empty, Method.{char.ToUpper(r.Method[0]) + r.Method[1..].ToLower()});");
        foreach (var h in r.Headers.Where(h => !h.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)))
            sb.AppendLine($"request.AddHeader({Q(h.Key, "\"")}, {TokenAwareJoin(h.Value, v => Q(v, "\""), "token", " + ")});");
        foreach (var kv in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var i = kv.IndexOf('='); var k = Uri.UnescapeDataString(i < 0 ? kv : kv[..i]); var v = i < 0 ? "" : Uri.UnescapeDataString(kv[(i + 1)..]);
            sb.AppendLine($"request.AddQueryParameter({Q(k, "\"")}, {Q(v, "\"")});");
        }
        if (r.Body != null)
        {
            if (r.BodyMode is "urlencoded" or "formdata") foreach (var p in Pairs(r.Body)) sb.AppendLine($"request.AddParameter({Q(p.Key, "\"")}, {Q(p.Value, "\"")});");
            else sb.AppendLine("request.AddStringBody(\"\"\"\n" + Pretty(r.Body) + "\n\"\"\", DataFormat.Json);");
        }
        sb.AppendLine("\nRestResponse response = await client.ExecuteAsync(request);");
        sb.AppendLine("Console.WriteLine($\"{(int)response.StatusCode} {response.StatusDescription}\\n{response.Content}\");");
        return sb.ToString();
    }

    // ---------- JavaScript fetch ----------
    public static string JavaScriptFetch(ApiHttpRequest r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("const token = process.env.KTC_ACCESS_TOKEN; // browser: keep it in memory, never in localStorage\n");
        sb.AppendLine("const headers = {");
        foreach (var h in r.Headers) sb.AppendLine($"  {Q(h.Key, "\"")}: {TokenAwareJoin(h.Value, v => Q(v, "\""), "token", " + ")},");
        sb.AppendLine("};\n");
        sb.AppendLine($"const response = await fetch({Q(r.Url, "\"")}, {{");
        sb.AppendLine($"  method: {Q(r.Method, "\"")},");
        sb.AppendLine("  headers,");
        if (r.Body != null)
        {
            if (r.BodyMode == "urlencoded") sb.AppendLine("  body: new URLSearchParams(" + JsonConvert.SerializeObject(Pairs(r.Body).ToDictionary(p => p.Key, p => p.Value)) + "),");
            else sb.AppendLine("  body: JSON.stringify(" + Pretty(r.Body).Replace("\n", "\n  ") + "),");
        }
        sb.AppendLine("});\n");
        sb.AppendLine("console.log(response.status, await response.text());");
        return sb.ToString();
    }

    // ---------- helpers ----------
    static string ShQuote(string s) => "'" + s.Replace("'", "'\\''") + "'";
    static string Compact(string s) { try { return Newtonsoft.Json.Linq.JToken.Parse(s).ToString(Formatting.None); } catch { return s; } }
    static string Pretty(string s) { try { return Newtonsoft.Json.Linq.JToken.Parse(s).ToString(Formatting.Indented); } catch { return s; } }
    static IEnumerable<KeyValuePair<string, string>> Pairs(string body)
    {
        try { return (JsonConvert.DeserializeObject<List<Dtos.KeyValueDto>>(body) ?? new()).Where(p => p.Enabled).Select(p => new KeyValuePair<string, string>(p.Key, p.Value)).ToList(); }
        catch { return Array.Empty<KeyValuePair<string, string>>(); }
    }
}
