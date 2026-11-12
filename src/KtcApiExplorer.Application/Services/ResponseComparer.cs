using KtcApiExplorer.Application.Dtos;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace KtcApiExplorer.Application.Services;

/// <summary>Structural JSON diff (paths like $.items[2].name). Non-JSON falls back to whole-text comparison.</summary>
public static class ResponseComparer
{
    const int Max = 500;

    public static DiffResultDto Compare(string? left, string? right)
    {
        var res = new DiffResultDto();
        var l = TryParse(left); var r = TryParse(right);
        if (l != null && r != null) Walk("$", l, r, res);
        else if ((left ?? "") != (right ?? ""))
            res.Differences.Add(new DiffItemDto { Path = "$", Type = "Changed", Left = Short(left), Right = Short(right) });
        res.AreEqual = res.Differences.Count == 0;
        return res;
    }

    static void Walk(string path, JToken l, JToken r, DiffResultDto res)
    {
        if (res.Differences.Count >= Max) { res.Truncated = true; return; }
        if (l.Type == JTokenType.Object && r.Type == JTokenType.Object)
        {
            var lo = (JObject)l; var ro = (JObject)r;
            foreach (var p in lo.Properties())
            {
                var cp = path + "." + p.Name;
                if (!ro.TryGetValue(p.Name, out var rv)) Add(res, cp, "Removed", p.Value, null);
                else Walk(cp, p.Value, rv, res);
            }
            foreach (var p in ro.Properties()) if (!lo.ContainsKey(p.Name)) Add(res, path + "." + p.Name, "Added", null, p.Value);
        }
        else if (l.Type == JTokenType.Array && r.Type == JTokenType.Array)
        {
            var la = (JArray)l; var ra = (JArray)r;
            for (int i = 0; i < Math.Max(la.Count, ra.Count); i++)
            {
                var cp = $"{path}[{i}]";
                if (i >= ra.Count) Add(res, cp, "Removed", la[i], null);
                else if (i >= la.Count) Add(res, cp, "Added", null, ra[i]);
                else Walk(cp, la[i], ra[i], res);
            }
        }
        else if (!JToken.DeepEquals(l, r)) Add(res, path, "Changed", l, r);
    }

    static void Add(DiffResultDto res, string path, string type, JToken? l, JToken? r)
    {
        if (res.Differences.Count >= Max) { res.Truncated = true; return; }
        res.Differences.Add(new DiffItemDto { Path = path, Type = type, Left = l == null ? null : Short(l.ToString(Formatting.None)), Right = r == null ? null : Short(r.ToString(Formatting.None)) });
    }
    static string? Short(string? s) => s == null ? null : s.Length > 300 ? s[..300] + "…" : s;
    static JToken? TryParse(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        try { using var jr = new JsonTextReader(new StringReader(s)) { DateParseHandling = DateParseHandling.None }; return JToken.Load(jr); } catch { return null; }
    }
}
