using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using KtcApiExplorer.Application.Common;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace KtcApiExplorer.Application.CodeGen;

/// <summary>Generates Newtonsoft-annotated C# classes from a JSON sample, merging all array elements into one shape.</summary>
public static class CSharpDtoGenerator
{
    enum Kind { Unknown, Primitive, Object, Array }
    class Shape
    {
        public Kind Kind; public string Prim = "object"; public bool Nullable;
        public Dictionary<string, Shape> Props = new(); public List<string> Order = new(); public Shape? Element;
    }
    static readonly Regex IsoDate = new(@"^\d{4}-\d{2}-\d{2}([T ]\d{2}:\d{2}(:\d{2}(\.\d+)?)?(Z|[+-]\d{2}:?\d{2})?)?$", RegexOptions.Compiled);

    public static string Generate(string json, string rootName = "Root", string ns = "KtcApi.Models")
    {
        JToken token;
        try { using var jr = new JsonTextReader(new StringReader(json)) { DateParseHandling = DateParseHandling.None }; token = JToken.Load(jr); }
        catch (JsonException ex) { throw new ValidationException("The sample is not valid JSON: " + ex.Message); }

        rootName = Pascal(string.IsNullOrWhiteSpace(rootName) ? "Root" : rootName);
        var shape = Infer(token, rootName);
        var classes = new List<(string Name, Shape Shape)>();
        var used = new HashSet<string>();
        var rootType = TypeOf(shape, rootName, classes, used);

        var sb = new StringBuilder();
        sb.AppendLine("#nullable enable\nusing System;\nusing System.Collections.Generic;\nusing Newtonsoft.Json;\n");
        sb.AppendLine($"namespace {ns};\n");
        if (shape.Kind != Kind.Object)
            sb.AppendLine($"// Root payload is {(shape.Kind == Kind.Array ? "an array" : "a primitive value")}: use {rootType}\n");
        for (int ci = 0; ci < classes.Count; ci++)   // nested classes are appended while rendering
        {
            var (name, s) = classes[ci];
            sb.AppendLine($"public class {name}\n{{");
            var pused = new HashSet<string>();
            foreach (var key in s.Order)
            {
                var p = s.Props[key];
                var propName = Unique(Pascal(key), pused);
                var type = TypeOf(p, propName, classes, used, parentIsClass: true);
                sb.AppendLine($"    [JsonProperty({Quote(key)})]");
                var init = type == "string" ? " = string.Empty;" : type.StartsWith("List<") ? " = new();" : "";
                sb.AppendLine($"    public {type} {propName} {{ get; set; }}{init}\n");
            }
            sb.AppendLine("}\n");
        }
        return sb.ToString().TrimEnd() + "\n";
    }

    // ---- inference ----
    static Shape Infer(JToken t, string hint)
    {
        switch (t.Type)
        {
            case JTokenType.Object:
                var o = new Shape { Kind = Kind.Object };
                foreach (var p in ((JObject)t).Properties()) { o.Props[p.Name] = Infer(p.Value, p.Name); o.Order.Add(p.Name); }
                return o;
            case JTokenType.Array:
                var a = new Shape { Kind = Kind.Array };
                foreach (var i in (JArray)t) a.Element = a.Element == null ? Infer(i, hint) : Merge(a.Element, Infer(i, hint));
                return a;
            case JTokenType.Null: return new Shape { Kind = Kind.Unknown, Nullable = true, Prim = GuessFromName(hint) };
            case JTokenType.Integer: return new Shape { Kind = Kind.Primitive, Prim = (long)((JValue)t).Value! is var l && (l > int.MaxValue || l < int.MinValue) ? "long" : "int" };
            case JTokenType.Float: return new Shape { Kind = Kind.Primitive, Prim = "double" };
            case JTokenType.Boolean: return new Shape { Kind = Kind.Primitive, Prim = "bool" };
            default:
                var s = t.ToString();
                return new Shape { Kind = Kind.Primitive, Prim = Guid.TryParse(s, out _) ? "Guid" : IsoDate.IsMatch(s) ? "DateTime" : "string" };
        }
    }

    static Shape Merge(Shape a, Shape b)
    {
        if (a.Kind == Kind.Unknown) { b.Nullable |= a.Nullable; return b.Kind == Kind.Unknown ? a : b; }
        if (b.Kind == Kind.Unknown) { a.Nullable |= b.Nullable; return a; }
        if (a.Kind != b.Kind) return new Shape { Kind = Kind.Primitive, Prim = "object", Nullable = true };
        switch (a.Kind)
        {
            case Kind.Object:
                foreach (var k in b.Order)
                {
                    if (a.Props.TryGetValue(k, out var ex)) a.Props[k] = Merge(ex, b.Props[k]);
                    else { a.Props[k] = b.Props[k]; a.Props[k].Nullable = true; a.Order.Add(k); }
                }
                foreach (var k in a.Order.Where(k => !b.Props.ContainsKey(k))) a.Props[k].Nullable = true; // missing in some elements => optional
                break;
            case Kind.Array:
                a.Element = a.Element == null ? b.Element : b.Element == null ? a.Element : Merge(a.Element, b.Element);
                break;
            default:
                if (a.Prim != b.Prim) a.Prim = (a.Prim, b.Prim) is ("int", "long") or ("long", "int") ? "long" : (a.Prim, b.Prim) is ("int", "double") or ("double", "int") or ("long", "double") or ("double", "long") ? "double" : "object";
                a.Nullable |= b.Nullable;
                break;
        }
        return a;
    }

    static string TypeOf(Shape s, string nameHint, List<(string, Shape)> classes, HashSet<string> used, bool parentIsClass = false)
    {
        string t;
        switch (s.Kind)
        {
            case Kind.Object:
                var cname = Unique(Pascal(nameHint), used);
                classes.Add((cname, s));
                t = cname; break;
            case Kind.Array:
                t = $"List<{(s.Element == null ? "object" : TypeOf(s.Element, Singular(nameHint), classes, used, parentIsClass))}>"; break;
            case Kind.Unknown:
                t = s.Prim; break;
            default: t = s.Prim; break;
        }
        var nullable = s.Nullable && s.Kind != Kind.Array;
        return nullable && !t.EndsWith('?') ? t + "?" : t;
    }

    static string GuessFromName(string n)
    {
        var l = n.ToLowerInvariant();
        if (Regex.IsMatch(l, @"(^|[a-z])id$") || l.EndsWith("count") || l.StartsWith("num")) return "int";
        if (l.StartsWith("is") || l.StartsWith("has") || l.StartsWith("enable") || l.EndsWith("flag")) return "bool";
        if (l.Contains("date") || l.EndsWith("time") || l.EndsWith("utc")) return "DateTime";
        if (Regex.IsMatch(l, @"^item\d+$")) return "object";
        return "string";
    }

    // ---- naming ----
    public static string Pascal(string s)
    {
        var parts = Regex.Split(s, @"[^A-Za-z0-9]+").Where(p => p.Length > 0).Select(p => char.ToUpper(p[0], CultureInfo.InvariantCulture) + p[1..]);
        var r = string.Concat(parts);
        if (r.Length == 0) r = "Field";
        if (char.IsDigit(r[0])) r = "_" + r;
        return Keywords.Contains(r.ToLowerInvariant()) ? "@" + r : r;
    }
    static string Singular(string n) => n.EndsWith("ies", StringComparison.OrdinalIgnoreCase) ? n[..^3] + "y" : n.EndsWith("s", StringComparison.OrdinalIgnoreCase) && !n.EndsWith("ss", StringComparison.OrdinalIgnoreCase) ? n[..^1] : n + "Item";
    static string Unique(string name, HashSet<string> used)
    {
        var n = name; int i = 2;
        while (!used.Add(n)) n = name + i++;
        return n;
    }
    static string Quote(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    static readonly HashSet<string> Keywords = new() { "class", "object", "string", "int", "long", "bool", "namespace", "event", "params", "base", "default", "lock", "operator", "ref", "out", "in", "is", "as", "new", "null", "this", "true", "false", "checked", "fixed", "void" };
}
