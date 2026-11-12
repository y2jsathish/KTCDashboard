using System.Text.RegularExpressions;
using KtcApiExplorer.Application.Common;
using KtcApiExplorer.Application.Dtos;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace KtcApiExplorer.Application.Parsing;

public class ParsedEndpoint
{
    public string Name { get; set; } = "";
    public string Method { get; set; } = "GET";
    public string Route { get; set; } = "";
    public List<string> FolderPath { get; set; } = new();
    public string? Description { get; set; }
    public List<KeyValueDto> Headers { get; set; } = new();
    public List<KeyValueDto> Query { get; set; } = new();
    public List<KeyValueDto> PathParams { get; set; } = new();
    public string? BodyMode { get; set; }
    public string? BodyRaw { get; set; }
    public string? BodyLanguage { get; set; }
    public List<ExampleDto> Examples { get; set; } = new();
    public string QuerySignature => KtcHelpDocParser.Signature(Query.Select(q => q.Key));
}

public class ParsedCollection
{
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string? SchemaVersion { get; set; }
    public Dictionary<string, string> Variables { get; set; } = new();
    /// <summary>Origin shared by all absolute URLs in the file, if any.</summary>
    public string? DetectedBaseUrl { get; set; }
    public List<ParsedEndpoint> Endpoints { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

/// <summary>
/// Parses Postman Collection v2.1 (and tolerant of v2.0). Handles nested folders, string/object URLs,
/// and exports whose url is empty and whose "METHOD route" lives in the item name (as in the KTC collection).
/// </summary>
public static class PostmanCollectionParser
{
    static readonly Regex NameRoute = new(@"^\s*(GET|POST|PUT|DELETE|PATCH|HEAD|OPTIONS)\b(?:\s+(\S+))?", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex LeadingVar = new(@"^(\{\{[^}]+\}\})+/?", RegexOptions.Compiled);
    static readonly Regex Origin = new(@"^(https?://[^/?#]+)(/[^?#]*)?(\?.*)?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static ParsedCollection Parse(string json)
    {
        JObject root;
        try
        {
            using var sr = new StringReader(json);
            using var jr = new JsonTextReader(sr) { DateParseHandling = DateParseHandling.None };
            root = JObject.Load(jr);
        }
        catch (Exception ex) when (ex is JsonException)
        {
            throw new ValidationException("The file is not valid JSON: " + ex.Message);
        }

        var info = root["info"] as JObject;
        var schema = info?["schema"]?.ToString();
        if (info == null || root["item"] is not JArray items)
            throw new ValidationException("This does not look like a Postman collection (missing 'info' or 'item').");

        var result = new ParsedCollection
        {
            Name = info["name"]?.ToString() ?? "Imported collection",
            Description = ReadDescription(info["description"]),
            SchemaVersion = schema
        };
        if (schema != null && !schema.Contains("collection/v2", StringComparison.OrdinalIgnoreCase))
            result.Warnings.Add($"Unexpected collection schema '{schema}'. Only Postman Collection v2.x is supported; parsing was attempted anyway.");

        if (root["variable"] is JArray vars)
            foreach (var v in vars.OfType<JObject>())
                if (v["key"]?.ToString() is { Length: > 0 } k) result.Variables[k] = v["value"]?.ToString() ?? "";

        var origins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Walk(items, new List<string>(), result, origins);

        if (origins.Count == 1) result.DetectedBaseUrl = origins.First();
        else if (origins.Count > 1) result.Warnings.Add("Requests target several hosts; set the base URL manually: " + string.Join(", ", origins));
        else if (result.Variables.TryGetValue("baseUrl", out var bu) && Uri.IsWellFormedUriString(bu, UriKind.Absolute)) result.DetectedBaseUrl = bu;
        return result;
    }

    static void Walk(JArray items, List<string> path, ParsedCollection result, HashSet<string> origins)
    {
        foreach (var item in items.OfType<JObject>())
        {
            var name = item["name"]?.ToString() ?? "";
            if (item["item"] is JArray children)
            {
                Walk(children, new List<string>(path) { name }, result, origins);
                continue;
            }
            var ep = ParseRequest(item, name, path, origins);
            if (ep == null) { result.Warnings.Add($"Skipped item '{name}': no usable request."); continue; }
            result.Endpoints.Add(ep);
        }
    }

    static ParsedEndpoint? ParseRequest(JObject item, string name, List<string> path, HashSet<string> origins)
    {
        var req = item["request"];
        if (req == null) return null;

        string method = "GET";
        JToken? urlToken = null;
        JObject? reqObj = req as JObject;
        if (req.Type == JTokenType.String) urlToken = req;            // shorthand: "request": "https://..."
        else if (reqObj != null) { method = (reqObj["method"]?.ToString() ?? "GET").ToUpperInvariant(); urlToken = reqObj["url"]; }

        var ep = new ParsedEndpoint { Name = name, Method = method, FolderPath = new List<string>(path) };

        // ---- URL -> route + query ----
        string raw = "";
        var query = new List<KeyValueDto>();
        if (urlToken is JValue uv) raw = uv.ToString();
        else if (urlToken is JObject uo)
        {
            raw = uo["raw"]?.ToString() ?? "";
            if (string.IsNullOrWhiteSpace(raw) && uo["path"] is JArray pa)
            {
                var host = uo["host"] is JArray ha ? string.Join(".", ha.Select(x => x.ToString())) : uo["host"]?.ToString();
                var proto = uo["protocol"]?.ToString();
                var p = string.Join("/", pa.Select(x => x.Type == JTokenType.Object ? x["value"]?.ToString() : x.ToString()));
                raw = (host != null ? (proto != null ? proto + "://" : "") + host + "/" : "") + p;
            }
            if (uo["query"] is JArray qa)
                foreach (var q in qa.OfType<JObject>())
                    query.Add(new KeyValueDto { Key = q["key"]?.ToString() ?? "", Value = q["value"]?.ToString() ?? "", Description = ReadDescription(q["description"]), Enabled = q["disabled"]?.Value<bool>() != true });
        }

        // KTC exports: empty url, route in the item name (names are HTML-escaped: "&amp;"). A bare "GET"/"HEAD" is the API root.
        var rootRoute = false;
        if (string.IsNullOrWhiteSpace(raw))
        {
            var m = NameRoute.Match(name);
            if (m.Success)
            {
                if (reqObj?["method"] == null) ep.Method = m.Groups[1].Value.ToUpperInvariant();
                raw = System.Net.WebUtility.HtmlDecode(m.Groups[2].Value);
                rootRoute = raw.Length == 0;
            }
        }
        if (string.IsNullOrWhiteSpace(raw) && !rootRoute) return null;

        raw = raw.Trim();
        var route = raw;
        string? queryString = null;
        var qi = route.IndexOf('?');
        if (qi >= 0) { queryString = route[(qi + 1)..]; route = route[..qi]; }

        var om = Origin.Match(route);
        if (om.Success) { origins.Add(om.Groups[1].Value); route = om.Groups[2].Value; }
        route = LeadingVar.Replace(route, "");
        route = Regex.Replace(route, @"/:(\w+)", "/{$1}");           // Postman :id -> {id}
        route = route.Trim('/');
        ep.Route = route;

        if (queryString != null && query.Count == 0)
            foreach (var part in queryString.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var eq = part.IndexOf('=');
                var key = Uri.UnescapeDataString(eq < 0 ? part : part[..eq]);
                var val = eq < 0 ? "" : part[(eq + 1)..];
                // "{name}" is a placeholder in help-page style routes -> required parameter, empty default
                var isPlaceholder = Regex.IsMatch(val, @"^\{[^}]+\}$");
                query.Add(new KeyValueDto { Key = key, Value = isPlaceholder ? "" : val, Required = isPlaceholder });
            }
        ep.Query = query;

        foreach (Match pm in Regex.Matches(route, @"\{([^}/]+)\}"))
            if (ep.PathParams.All(p => p.Key != pm.Groups[1].Value))
                ep.PathParams.Add(new KeyValueDto { Key = pm.Groups[1].Value, Required = true });

        // ---- headers / description / body ----
        if (reqObj?["header"] is JArray ha2)
            foreach (var h in ha2.OfType<JObject>())
                ep.Headers.Add(new KeyValueDto { Key = h["key"]?.ToString() ?? "", Value = h["value"]?.ToString() ?? "", Description = ReadDescription(h["description"]), Enabled = h["disabled"]?.Value<bool>() != true });

        ep.Description = ReadDescription(reqObj?["description"]) ?? ReadDescription(item["description"]);

        if (reqObj?["body"] is JObject body)
        {
            var mode = body["mode"]?.ToString();
            ep.BodyMode = mode;
            switch (mode)
            {
                case "raw":
                    ep.BodyRaw = body["raw"]?.ToString();
                    ep.BodyLanguage = body["options"]?["raw"]?["language"]?.ToString();
                    break;
                case "urlencoded":
                case "formdata":
                    var pairs = (body[mode] as JArray ?? new JArray()).OfType<JObject>()
                        .Where(x => x["type"]?.ToString() != "file")
                        .Select(x => new KeyValueDto { Key = x["key"]?.ToString() ?? "", Value = x["value"]?.ToString() ?? "", Enabled = x["disabled"]?.Value<bool>() != true }).ToList();
                    ep.BodyRaw = JsonConvert.SerializeObject(pairs);
                    break;
                case "graphql":
                    ep.BodyRaw = body["graphql"]?.ToString(Formatting.None);
                    break;
            }
        }

        // ---- examples (saved responses) ----
        if (item["response"] is JArray resp)
            foreach (var r in resp.OfType<JObject>())
                ep.Examples.Add(new ExampleDto
                {
                    Name = r["name"]?.ToString() ?? "Example",
                    Status = r["code"]?.Value<int?>(),
                    Body = r["body"]?.ToString(),
                    RequestBody = r["originalRequest"]?["body"]?["raw"]?.ToString()
                });
        return ep;
    }

    static string? ReadDescription(JToken? t)
    {
        if (t == null || t.Type == JTokenType.Null) return null;
        var s = t.Type == JTokenType.Object ? t["content"]?.ToString() : t.ToString();
        return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }
}
