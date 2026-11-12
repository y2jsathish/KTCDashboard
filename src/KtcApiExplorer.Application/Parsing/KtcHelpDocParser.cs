using System.Net;
using System.Text.RegularExpressions;

namespace KtcApiExplorer.Application.Parsing;

public record HelpDocEntry(string Group, string Api, string Description);

/// <summary>
/// Reads the ASP.NET Web API "Help Page" HTML that ships with KTC REST API (the KAL API doc): an h2 per controller
/// followed by a table of "METHOD route" + description. Used to enrich a Postman import with real folder names and descriptions.
/// </summary>
public static class KtcHelpDocParser
{
    static readonly Regex Token = new(
        @"<h2[^>]*>(?<h>.*?)</h2>|<td class=""api-name""><a[^>]*>(?<api>[^<]+)</a></td>\s*<td class=""api-documentation"">\s*(?:<p>(?<desc>.*?)</p>)?",
        RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static List<HelpDocEntry> Parse(string html)
    {
        var list = new List<HelpDocEntry>();
        string? group = null;
        foreach (Match m in Token.Matches(html))
        {
            if (m.Groups["h"].Success) { group = Clean(m.Groups["h"].Value); continue; }
            if (!m.Groups["api"].Success || group == null || group == "Introduction") continue;
            var desc = Clean(m.Groups["desc"].Value);
            if (desc.Equals("No documentation available.", StringComparison.OrdinalIgnoreCase)) desc = "";
            list.Add(new HelpDocEntry(group, Clean(m.Groups["api"].Value), desc));
        }
        return list;
    }

    static string Clean(string s) => Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(s, "<[^>]+>", "")), @"\s+", " ").Trim();

    /// <summary>Lookup key: METHOD + path + sorted query-parameter names (parameter placeholders ignored, case-insensitive).</summary>
    public static string Key(string method, string route)
    {
        var q = route.IndexOf('?');
        var keys = q < 0 ? Array.Empty<string>() : route[(q + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Split('=')[0]);
        return Key(method, q < 0 ? route : route[..q], keys);
    }

    public static string Key(string method, string path, IEnumerable<string> queryKeys)
    {
        path = Regex.Replace(path.Trim().Trim('/'), @"\{[^}]*\}", "{}");
        var sig = Signature(queryKeys);
        return (method.ToUpperInvariant() + " " + path + (sig.Length > 0 ? "?" + sig : "")).ToLowerInvariant();
    }

    public static string Signature(IEnumerable<string> queryKeys) =>
        string.Join("&", queryKeys.Where(k => !string.IsNullOrWhiteSpace(k)).Select(k => k.Trim().ToLowerInvariant()).Distinct().OrderBy(k => k, StringComparer.Ordinal));

    /// <summary>"GET api/v1/X?a={a}" -> ("GET", "api/v1/X?a={a}")</summary>
    public static (string Method, string Route) SplitApi(string api)
    {
        var i = api.IndexOf(' ');
        return i < 0 ? (api, "") : (api[..i], api[(i + 1)..]);
    }
}
