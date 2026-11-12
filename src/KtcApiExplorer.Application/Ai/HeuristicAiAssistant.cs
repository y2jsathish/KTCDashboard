using System.Text;
using System.Text.RegularExpressions;
using KtcApiExplorer.Application.Abstractions;
using KtcApiExplorer.Application.Common;
using KtcApiExplorer.Application.Dtos;
using KtcApiExplorer.Application.Services;
using KtcApiExplorer.Domain.Entities;
using KtcApiExplorer.Domain.Interfaces;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace KtcApiExplorer.Application.Ai;

/// <summary>
/// Offline, deterministic assistant: derives answers from the endpoint's route, description, parameters and sample payload.
/// It is the default provider and the fallback when an LLM provider is unavailable. Nothing leaves the server.
/// </summary>
public class HeuristicAiAssistant : IAiAssistant
{
    readonly IEndpointRepository _eps;
    public HeuristicAiAssistant(IEndpointRepository eps) => _eps = eps;
    public string ProviderName => "Heuristic (offline)";

    async Task<ApiEndpoint> LoadAsync(int id, CancellationToken ct) => await _eps.GetWithCollectionAsync(id, ct) ?? throw new NotFoundException("Endpoint not found.");

    public async Task<AiResultDto> ExplainAsync(int endpointId, CancellationToken ct = default) =>
        new() { Kind = "explain", Provider = ProviderName, Content = Explain(await LoadAsync(endpointId, ct)) };

    public async Task<AiResultDto> GenerateRequestBodyAsync(int endpointId, CancellationToken ct = default)
    {
        var ep = await LoadAsync(endpointId, ct);
        var sample = ParseBody(ep);
        var content = sample == null ? "// This endpoint does not take a JSON body." : Fill(sample, 0, "").ToString(Formatting.Indented);
        return new() { Kind = "request-body", Provider = ProviderName, Content = content };
    }

    public async Task<AiResultDto> GenerateTestDataAsync(int endpointId, int count, CancellationToken ct = default)
    {
        var ep = await LoadAsync(endpointId, ct);
        var sample = ParseBody(ep);
        if (sample == null) return new() { Kind = "test-data", Provider = ProviderName, Content = "// This endpoint does not take a JSON body." };
        var arr = new JArray();
        for (int i = 0; i < Math.Clamp(count, 1, 20); i++) arr.Add(Fill(sample, i, ""));
        return new() { Kind = "test-data", Provider = ProviderName, Content = arr.ToString(Formatting.Indented) };
    }

    public async Task<AiResultDto> IdentifyRequiredFieldsAsync(int endpointId, CancellationToken ct = default)
    {
        var ep = await LoadAsync(endpointId, ct);
        var fields = RequiredFields(ep);
        var sb = new StringBuilder();
        foreach (var f in fields) sb.AppendLine($"- {(f.Required ? "REQUIRED" : "optional")}  {f.Location}:{f.Name} ({f.Type}) - {f.Reason}");
        if (fields.Count == 0) sb.AppendLine("No parameters or body fields were found for this endpoint.");
        sb.AppendLine("\nNote: derived from the sample payload and route (non-null sample value => treated as required). The KTC help pages do not publish validation rules.");
        return new() { Kind = "required-fields", Provider = ProviderName, Content = sb.ToString(), Fields = fields };
    }

    public async Task<AiResultDto> GenerateDocumentationAsync(int endpointId, CancellationToken ct = default) =>
        new() { Kind = "documentation", Provider = ProviderName, Content = Document(await LoadAsync(endpointId, ct)) };

    // ======================= building blocks (public for tests) =======================
    public static string Explain(ApiEndpoint ep)
    {
        var (verb, resource) = Describe(ep);
        var sb = new StringBuilder();
        sb.AppendLine($"**{ep.Method} {ep.RouteTemplate}**\n");
        sb.AppendLine($"{verb} {resource}." + (string.IsNullOrWhiteSpace(ep.Description) ? "" : $" The KTC documentation says: \"{ep.Description}\"."));
        sb.AppendLine();
        var path = RequestBuilder.ReadList(ep.PathParamsJson); var query = RequestBuilder.ReadList(ep.QueryParamsJson);
        if (path.Count > 0) sb.AppendLine("- Path parameters: " + string.Join(", ", path.Select(p => $"`{p.Key}`")) + " (identify the target record).");
        if (query.Count > 0) sb.AppendLine("- Query parameters: " + string.Join(", ", query.Select(p => $"`{p.Key}`")) + ".");
        var fields = RequiredFields(ep).Where(f => f.Location == "body").ToList();
        if (fields.Count > 0) sb.AppendLine($"- Body: JSON with {fields.Count} field(s), e.g. " + string.Join(", ", fields.Take(6).Select(f => $"`{f.Name}`")) + (fields.Count > 6 ? ", …" : "") + ".");
        sb.AppendLine("- Authentication: send `Authorization: Bearer <access_token>` from `POST api/v1/Login`.");
        sb.AppendLine("- Side effects: " + (ep.Method switch { "GET" or "HEAD" => "none (read-only, safe to retry).", "DELETE" => "removes data - confirm the target before executing, especially in production.", "PUT" => "replaces/updates existing data (idempotent).", _ => "may create or change data - not necessarily idempotent." }));
        return sb.ToString().TrimEnd();
    }

    public static string Document(ApiEndpoint ep)
    {
        var (verb, resource) = Describe(ep);
        var baseUrl = ep.Collection?.BaseUrl ?? "{baseUrl}";
        var sb = new StringBuilder();
        sb.AppendLine($"# {ep.Method} {ep.RouteTemplate}\n");
        sb.AppendLine(string.IsNullOrWhiteSpace(ep.Description) ? $"{verb} {resource}." : ep.Description + "\n");
        sb.AppendLine("## Request\n");
        sb.AppendLine($"`{ep.Method} {baseUrl.TrimEnd('/')}/{ep.RouteTemplate}`\n");
        sb.AppendLine("**Headers**\n\n| Name | Value |\n|---|---|\n| Authorization | `Bearer {access_token}` |\n| Content-Type | `application/json` |\n");
        var fields = RequiredFields(ep);
        if (fields.Count > 0)
        {
            sb.AppendLine("## Parameters\n\n| Name | In | Type | Required | Notes |\n|---|---|---|---|---|");
            foreach (var f in fields) sb.AppendLine($"| `{f.Name}` | {f.Location} | {f.Type} | {(f.Required ? "yes" : "no")} | {f.Reason} |");
            sb.AppendLine();
        }
        var body = ParseBody(ep);
        if (body != null) sb.AppendLine("## Example body\n\n```json\n" + Fill(body, 0, "").ToString(Formatting.Indented) + "\n```\n");
        sb.AppendLine("## Responses\n\n| Status | Meaning |\n|---|---|\n| 200 | Success |\n| 400 | Validation error in the request |\n| 401 | Missing/expired bearer token - log in again |\n| 403 | The KTC user lacks permission |\n| 404 | Target record not found |\n");
        if (ep.Method == "DELETE") sb.AppendLine("> **Warning:** destructive operation.\n");
        return sb.ToString().TrimEnd();
    }

    public static List<RequiredFieldDto> RequiredFields(ApiEndpoint ep)
    {
        var list = new List<RequiredFieldDto>();
        foreach (var p in RequestBuilder.ReadList(ep.PathParamsJson)) list.Add(new() { Name = p.Key, Location = "path", Type = GuessType(p.Key, null), Required = true, Reason = "part of the URL path" });
        foreach (var q in RequestBuilder.ReadList(ep.QueryParamsJson)) list.Add(new() { Name = q.Key, Location = "query", Type = GuessType(q.Key, null), Required = q.Required, Reason = q.Required ? "templated in the documented route" : "optional filter" });
        var body = ParseBody(ep);
        if (body != null) Flatten(body, "", list);
        return list;
    }

    static void Flatten(JToken t, string path, List<RequiredFieldDto> list)
    {
        if (t is JObject o)
            foreach (var p in o.Properties())
            {
                var name = path == "" ? p.Name : path + "." + p.Name;
                if (p.Value is JObject or JArray) { Flatten(p.Value, name, list); continue; }
                bool hasValue = p.Value.Type != JTokenType.Null;
                list.Add(new() { Name = name, Location = "body", Type = GuessType(p.Name, p.Value), Required = hasValue, Reason = hasValue ? "sample contains a value" : "sample is null => nullable/optional" });
            }
        else if (t is JArray a && a.Count > 0) Flatten(a[0], path + "[]", list);
    }

    // ---------- realistic data ----------
    static readonly string[] Names = { "Jane Doe", "John Smith", "Alice Johnson", "Bob Williams", "Carol Davis", "Dan Brown" };
    static readonly string[] Cities = { "London", "Berlin", "Madrid", "Paris", "Rome", "Oslo" };

    public static JToken Fill(JToken t, int seed, string name)
    {
        switch (t)
        {
            case JObject o:
                var r = new JObject();
                foreach (var p in o.Properties()) r[p.Name] = Fill(p.Value, seed, p.Name);
                return r;
            case JArray a: return new JArray(a.Take(1).Select(x => Fill(x, seed, name)));
            case JValue v when v.Type == JTokenType.Null: return Value(name, null, seed);
            case JValue v: return Value(name, v, seed);
        }
        return t;
    }

    static JToken Value(string name, JValue? sample, int seed)
    {
        var n = name.ToLowerInvariant(); var rnd = new Random(seed * 7919 + name.Length);
        var type = sample?.Type ?? JTokenType.Null;
        if (type == JTokenType.Boolean) return seed % 2 == 0;
        if (type == JTokenType.Integer || (type == JTokenType.Null && Regex.IsMatch(n, @"(id|count|number|num|qty|quantity|retry|port)$")))
            return n.Contains("retry") ? seed : n.EndsWith("id") ? 1000 + seed * 17 + rnd.Next(10) : rnd.Next(1, 100);
        if (type == JTokenType.Float) return Math.Round(rnd.NextDouble() * 100, 2);
        if (n.Contains("email")) return $"user{seed + 1}@example.com";
        if (n.Contains("phone") || n.Contains("mobile")) return $"+44 7700 9{rnd.Next(10000, 99999)}";
        if (n.Contains("password")) return "Examp1e-Passw0rd!";
        if (n.Contains("user") && n.Contains("name") || n == "username") return $"test.user{seed + 1}";
        if (n.Contains("date") || n.EndsWith("time") || n.EndsWith("utc") || n.EndsWith("at")) return DateTime.UtcNow.Date.AddDays(-seed).ToString("yyyy-MM-ddTHH:mm:ssZ");
        if (n.Contains("guid") || n.EndsWith("uuid")) return Guid.NewGuid().ToString();
        if (n.Contains("url") || n.Contains("uri")) return $"https://example.com/resource/{seed + 1}";
        if (n.Contains("city")) return Cities[seed % Cities.Length];
        if (n.Contains("name")) return Names[seed % Names.Length];
        if (n.Contains("status")) return seed % 2 == 0 ? "Active" : "Pending";
        if (n.Contains("description") || n.Contains("comment") || n.Contains("note") || n.Contains("message")) return $"Sample {n} {seed + 1}";
        if (type == JTokenType.Null) return JValue.CreateNull();
        var s = sample!.ToString();
        return Regex.IsMatch(s, @"^sample string \d+$") ? $"{(n.Length > 0 ? name : "value")}-{seed + 1}" : s;
    }

    static string GuessType(string name, JToken? v)
    {
        if (v != null) switch (v.Type) { case JTokenType.Integer: return "integer"; case JTokenType.Float: return "number"; case JTokenType.Boolean: return "boolean"; case JTokenType.String: return Regex.IsMatch(v.ToString(), @"^\d{4}-\d{2}-\d{2}") ? "datetime" : "string"; }
        var n = name.ToLowerInvariant();
        if (n.EndsWith("id") || n.EndsWith("count")) return "integer";
        if (n.StartsWith("is") || n.StartsWith("has")) return "boolean";
        if (n.Contains("date") || n.EndsWith("time")) return "datetime";
        return "string";
    }

    static JToken? ParseBody(ApiEndpoint ep)
    {
        if (ep.BodyMode != "raw" || string.IsNullOrWhiteSpace(ep.BodyRaw)) return null;
        try { using var jr = new JsonTextReader(new StringReader(ep.BodyRaw)) { DateParseHandling = DateParseHandling.None }; var t = JToken.Load(jr); return t is JObject { Count: 0 } ? null : t; } catch { return null; }
    }

    static (string Verb, string Resource) Describe(ApiEndpoint ep)
    {
        var segs = ep.RouteTemplate.Split('/', StringSplitOptions.RemoveEmptyEntries).Where(s => !s.Equals("api", StringComparison.OrdinalIgnoreCase) && !Regex.IsMatch(s, @"^v\d+$")).ToList();
        var named = segs.Where(s => !s.StartsWith('{')).Select(s => Regex.Replace(s, "([a-z])([A-Z])", "$1 $2")).ToList();
        var resource = named.Count == 0 ? "the resource" : string.Join(" › ", named);
        var byId = segs.Count > 0 && segs.Any(s => s.StartsWith('{')) ? $" (identified by {string.Join(", ", segs.Where(s => s.StartsWith('{')).Select(s => s.Trim('{', '}')))})" : "";
        var verb = ep.Method switch { "GET" => segs.Any(s => s.StartsWith('{')) ? "Retrieves" : "Lists/retrieves", "POST" => "Creates or submits", "PUT" => "Updates", "DELETE" => "Deletes", "HEAD" => "Checks the existence of", _ => ep.Method };
        return (verb, resource + byId);
    }
}
