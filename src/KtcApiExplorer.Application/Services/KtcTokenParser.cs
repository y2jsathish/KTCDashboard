using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace KtcApiExplorer.Application.Services;

public record KtcTokens(string AccessToken, string? KtcAccessToken, DateTime? ExpiresUtc);

/// <summary>
/// Extracts access_token / ktc_access_token from the Login response. The KTC doc does not publish the response schema,
/// so this accepts several spellings, a bare string token, nested objects, and derives expiry from expires_in / expires / JWT exp.
/// </summary>
public static class KtcTokenParser
{
    static readonly string[] AccessNames = { "access_token", "accesstoken", "token", "bearer", "authtoken" };
    static readonly string[] KtcNames = { "ktc_access_token", "ktcaccesstoken", "ktctoken" };
    static readonly string[] ExpiresIn = { "expires_in", "expiresin", "expires_in_seconds", "lifetime" };
    static readonly string[] ExpiresAt = { "expires", ".expires", "expires_at", "expiresat", "expiry", "expirationdate", "expiration" };

    public static KtcTokens? Parse(string? body, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        JToken root;
        try
        {
            using var jr = new JsonTextReader(new StringReader(body)) { DateParseHandling = DateParseHandling.None };
            root = JToken.Load(jr);
        }
        catch (JsonException) { return null; }

        if (root.Type == JTokenType.String) return Build(root.ToString(), null, null, nowUtc);

        string? access = Find(root, AccessNames), ktc = Find(root, KtcNames);
        if (string.IsNullOrEmpty(access)) return null;

        DateTime? exp = null;
        var inS = Find(root, ExpiresIn);
        if (inS != null && double.TryParse(inS, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var secs) && secs > 0)
            exp = nowUtc.AddSeconds(secs);
        if (exp == null)
        {
            var at = Find(root, ExpiresAt);
            if (at != null && DateTime.TryParse(at, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var dt)) exp = dt;
        }
        return Build(access, ktc, exp, nowUtc);
    }

    static KtcTokens Build(string access, string? ktc, DateTime? exp, DateTime now) =>
        new(access, ktc, exp ?? JwtExpiry(access) ?? JwtExpiry(ktc));

    static string? Find(JToken t, string[] names)
    {
        if (t is JObject o)
        {
            foreach (var p in o.Properties())
                if (names.Contains(p.Name.ToLowerInvariant()) && p.Value.Type is JTokenType.String or JTokenType.Integer or JTokenType.Float or JTokenType.Date)
                    return p.Value.ToString();
            foreach (var p in o.Properties())
                if (p.Value is JObject or JArray && Find(p.Value, names) is { } r) return r;
        }
        else if (t is JArray a) foreach (var i in a) if (Find(i, names) is { } r) return r;
        return null;
    }

    public static DateTime? JwtExpiry(string? jwt)
    {
        if (string.IsNullOrEmpty(jwt)) return null;
        var parts = jwt.Split('.');
        if (parts.Length != 3) return null;
        try
        {
            var s = parts[1].Replace('-', '+').Replace('_', '/');
            s = s.PadRight(s.Length + (4 - s.Length % 4) % 4, '=');
            var payload = JObject.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(s)));
            var exp = payload["exp"]?.Value<long?>();
            return exp.HasValue ? DateTimeOffset.FromUnixTimeSeconds(exp.Value).UtcDateTime : null;
        }
        catch { return null; }
    }
}
