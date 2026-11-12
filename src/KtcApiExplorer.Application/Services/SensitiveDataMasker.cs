using System.Text.RegularExpressions;
using KtcApiExplorer.Application.Abstractions;
using KtcApiExplorer.Application.Dtos;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace KtcApiExplorer.Application.Services;

/// <summary>Masks credentials/tokens/PII-like keys before anything is persisted or logged.</summary>
public class SensitiveDataMasker : ISensitiveDataMasker
{
    public const string Mask = "***MASKED***";
    static readonly Regex SensitiveKey = new(
        @"(pass(word|wd)?|pwd|secret|token|api[-_]?key|authorization|auth|credential|private[-_]?key|pin(code)?|card(number|num)?|cvv|ssn|session[-_]?id|cookie)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex BearerInText = new(@"(Bearer\s+)[A-Za-z0-9\-._~+/]+=*", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex JsonPair = new(@"(""(?:[^""\\]|\\.)*(?:pass(?:word|wd)?|pwd|secret|token|api[-_]?key|authorization)(?:[^""\\]|\\.)*""\s*:\s*)""(?:[^""\\]|\\.)*""", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool IsSensitiveKey(string key) => SensitiveKey.IsMatch(key);

    public string MaskJson(string? json)
    {
        if (string.IsNullOrEmpty(json)) return json ?? "";
        try
        {
            using var sr = new StringReader(json);
            using var jr = new JsonTextReader(sr) { DateParseHandling = DateParseHandling.None, FloatParseHandling = FloatParseHandling.Decimal };
            var token = JToken.Load(jr);
            MaskToken(token);
            return token.ToString(Formatting.Indented);
        }
        catch (JsonException)
        {
            return MaskText(json) ?? "";   // not JSON: fall back to pattern based masking
        }
    }

    public string? MaskText(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        text = BearerInText.Replace(text, "$1" + Mask);
        return JsonPair.Replace(text, "$1\"" + Mask + "\"");
    }

    public List<KeyValueDto> MaskHeaders(IEnumerable<KeyValueDto> headers) =>
        headers.Select(h => new KeyValueDto
        {
            Key = h.Key,
            Value = IsSensitiveKey(h.Key) ? Mask : (MaskText(h.Value) ?? ""),
            Description = h.Description, Enabled = h.Enabled, Required = h.Required
        }).ToList();

    static void MaskToken(JToken t)
    {
        switch (t)
        {
            case JObject o:
                foreach (var p in o.Properties().ToList())
                {
                    if (SensitiveKey.IsMatch(p.Name) && p.Value.Type is not (JTokenType.Object or JTokenType.Array or JTokenType.Null))
                        p.Value = new JValue(Mask);
                    else MaskToken(p.Value);
                }
                break;
            case JArray a:
                foreach (var i in a) MaskToken(i);
                break;
        }
    }
}
