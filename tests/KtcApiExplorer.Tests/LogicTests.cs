using KtcApiExplorer.Application.Abstractions;
using KtcApiExplorer.Application.Ai;
using KtcApiExplorer.Application.CodeGen;
using KtcApiExplorer.Application.Common;
using KtcApiExplorer.Application.Dtos;
using KtcApiExplorer.Application.Services;
using KtcApiExplorer.Domain.Entities;
using Newtonsoft.Json.Linq;
using Xunit;

namespace KtcApiExplorer.Tests;

public class MaskerTests
{
    readonly SensitiveDataMasker _m = new();

    [Fact]
    public void Masks_sensitive_json_keys_at_any_depth_and_keeps_the_rest()
    {
        var o = JObject.Parse(_m.MaskJson("{\"user\":\"bob\",\"Password\":\"p\",\"nested\":{\"access_token\":\"abc\",\"list\":[{\"apiKey\":\"k\",\"id\":5}]}}"));
        Assert.Equal("bob", (string?)o["user"]);
        Assert.Equal(SensitiveDataMasker.Mask, (string?)o["Password"]);
        Assert.Equal(SensitiveDataMasker.Mask, (string?)o["nested"]!["access_token"]);
        Assert.Equal(SensitiveDataMasker.Mask, (string?)o["nested"]!["list"]![0]!["apiKey"]);
        Assert.Equal(5, (int?)o["nested"]!["list"]![0]!["id"]);
    }

    [Fact]
    public void Masks_authorization_and_cookie_headers_and_bearer_text()
    {
        var h = _m.MaskHeaders(new[] { new KeyValueDto { Key = "Authorization", Value = "Bearer abc.def" }, new KeyValueDto { Key = "Accept", Value = "json" }, new KeyValueDto { Key = "Cookie", Value = "s=1" } });
        Assert.Equal(SensitiveDataMasker.Mask, h[0].Value); Assert.Equal("json", h[1].Value); Assert.Equal(SensitiveDataMasker.Mask, h[2].Value);
        Assert.DoesNotContain("abc.def", _m.MaskText("failed with Bearer abc.def")!);
    }

    [Fact]
    public void Non_json_text_with_json_like_secrets_is_still_masked() =>
        Assert.DoesNotContain("hunter2", _m.MaskJson("not json {\"password\":\"hunter2\"}"));
}

public class TokenParserTests
{
    static readonly DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Reads_both_tokens_and_expires_in()
    {
        var t = KtcTokenParser.Parse("{\"access_token\":\"A\",\"ktc_access_token\":\"K\",\"expires_in\":600}", Now)!;
        Assert.Equal("A", t.AccessToken); Assert.Equal("K", t.KtcAccessToken); Assert.Equal(Now.AddSeconds(600), t.ExpiresUtc);
    }

    [Fact]
    public void Accepts_nested_pascal_case_and_bare_string_tokens()
    {
        Assert.Equal("X", KtcTokenParser.Parse("{\"Data\":{\"AccessToken\":\"X\"}}", Now)!.AccessToken);
        Assert.Equal("RAW", KtcTokenParser.Parse("\"RAW\"", Now)!.AccessToken);
    }

    [Fact]
    public void Falls_back_to_jwt_exp_claim()
    {
        var payload = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("{\"exp\":1893456000}")).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var t = KtcTokenParser.Parse($"{{\"access_token\":\"h.{payload}.s\"}}", Now)!;
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1893456000).UtcDateTime, t.ExpiresUtc);
    }

    [Theory] [InlineData("")] [InlineData("garbage")] [InlineData("{\"foo\":1}")]
    public void Returns_null_when_no_token_present(string body) => Assert.Null(KtcTokenParser.Parse(body, Now));
}

public class RequestBuilderTests
{
    static ApiEndpoint Ep(string route, string method = "GET") => new() { Id = 1, Method = method, RouteTemplate = route, HeadersJson = "[{\"key\":\"Authorization\",\"value\":\"Bearer {{access_token}}\"},{\"key\":\"Content-Type\",\"value\":\"application/json\"}]" };
    static readonly Dictionary<string, string> Vars = new() { ["access_token"] = "TOK" };
    const string Base = "https://ktc.example.com/KTCRESTAPI";

    [Fact]
    public void Builds_url_escapes_path_and_query_and_injects_the_session_token()
    {
        var r = RequestBuilder.Build(Ep("api/v1/Client/{id}/Actions"), Base, new ExecuteRequestDto { PathParams = { ["id"] = "a b" }, Query = { new KeyValueDto { Key = "x", Value = "1&2" }, new KeyValueDto { Key = "skip", Value = "" } } }, Vars);
        Assert.Equal("https://ktc.example.com/KTCRESTAPI/api/v1/Client/a%20b/Actions?x=1%262", r.Url);
        Assert.Equal("Bearer TOK", r.Headers.Single(h => h.Key == "Authorization").Value);
    }

    [Theory]
    [InlineData("..%2f..%2fadmin")]            // encoded traversal stays a single (escaped) segment
    [InlineData("../../other")]
    public void Path_parameter_cannot_escape_the_base_url(string value)
    {
        var r = RequestBuilder.Build(Ep("api/v1/Thing/{id}"), Base, new ExecuteRequestDto { PathParams = { ["id"] = value } }, Vars);
        Assert.StartsWith(Base + "/api/v1/Thing/", r.Url);
        Assert.DoesNotContain("/../", r.Url);
    }

    [Fact]
    public void Absolute_url_style_route_is_blocked()
    {
        var ep = Ep("{id}");
        Assert.Throws<ValidationException>(() => RequestBuilder.Build(ep, Base, new ExecuteRequestDto { PathParams = { ["id"] = "" } }, Vars));
    }

    [Fact]
    public void Client_cannot_override_authorization_or_host_headers()
    {
        var r = RequestBuilder.Build(Ep("api/v1/X"), Base, new ExecuteRequestDto { Headers = { new KeyValueDto { Key = "Authorization", Value = "Bearer EVIL" }, new KeyValueDto { Key = "Host", Value = "evil.com" }, new KeyValueDto { Key = "X-Trace", Value = "1" } } }, Vars);
        Assert.Equal("Bearer TOK", r.Headers.Single(h => h.Key == "Authorization").Value);
        Assert.DoesNotContain(r.Headers, h => h.Key == "Host");
        Assert.Contains(r.Headers, h => h.Key == "X-Trace");
    }

    [Fact]
    public void Missing_required_query_parameter_is_reported() =>
        Assert.Throws<ValidationException>(() => RequestBuilder.Build(Ep("api/v1/X"), Base, new ExecuteRequestDto { Query = { new KeyValueDto { Key = "must", Value = "", Required = true } } }, Vars));

    [Fact]
    public void Substitutes_postman_variables_in_body()
    {
        var r = RequestBuilder.Build(Ep("api/v1/X", "POST"), Base, new ExecuteRequestDto { Body = "{\"t\":\"{{access_token}}\",\"u\":\"{{unknown}}\"}" }, Vars);
        Assert.Contains("\"TOK\"", r.Body); Assert.Contains("{{unknown}}", r.Body);
    }

    [Theory] [InlineData("GET", true)] [InlineData("HEAD", true)] [InlineData("POST", false)] [InlineData("DELETE", false)]
    public void Safe_method_rule_used_for_viewers(string m, bool safe) => Assert.Equal(safe, RequestBuilder.IsSafeMethod(m));

    [Fact]
    public void Display_route_shows_templated_query_parameters() =>
        Assert.Equal("/api/v1/Campaign?name={name}", RequestBuilder.DisplayRoute(new ApiEndpoint { RouteTemplate = "api/v1/Campaign", QueryParamsJson = "[{\"key\":\"name\",\"value\":\"\",\"required\":true}]" }));
}

public class CodeGenTests
{
    static readonly ApiHttpRequest Req = new()
    {
        Method = "POST", Url = "https://h/api/v1/Login?x=1", Body = "{\"a\":1,\"it's\":\"q\"}", BodyMode = "raw",
        Headers = { new KeyValueDto { Key = "Authorization", Value = "Bearer " + RequestBuilder.TokenPlaceholder }, new KeyValueDto { Key = "Content-Type", Value = "application/json" } }
    };

    [Fact]
    public void Curl_uses_env_var_for_token_and_escapes_single_quotes()
    {
        var c = CodeGenerators.Curl(Req);
        Assert.Contains("Authorization: Bearer $KTC_ACCESS_TOKEN", c); Assert.DoesNotContain(RequestBuilder.TokenPlaceholder, c);
        Assert.Contains("it'\\''s", c);
    }

    [Fact]
    public void All_generators_reference_the_token_variable_not_a_literal()
    {
        foreach (var code in new[] { CodeGenerators.CSharpHttpClient(Req), CodeGenerators.CSharpRestSharp(Req), CodeGenerators.JavaScriptFetch(Req) })
        { Assert.Contains("KTC_ACCESS_TOKEN", code); Assert.DoesNotContain(RequestBuilder.TokenPlaceholder, code); }
        Assert.Contains("RestSharp", CodeGenerators.CSharpRestSharp(Req));
        Assert.Contains("AddQueryParameter(\"x\", \"1\")", CodeGenerators.CSharpRestSharp(Req));
    }
}

public class DtoGeneratorTests
{
    [Fact]
    public void Generates_types_nullability_nesting_and_list_item_classes()
    {
        var code = CSharpDtoGenerator.Generate("{\"Id\":1,\"Big\":9999999999,\"Price\":1.5,\"Ok\":true,\"When\":\"2026-01-01T10:00:00Z\",\"Guid\":\"3f2504e0-4f89-11d3-9a0c-0305e82c3301\",\"Owner\":{\"Name\":\"x\"},\"Tags\":[\"a\"],\"Rows\":[{\"A\":1},{\"A\":2,\"B\":\"s\"}],\"Missing\":null,\"userId\":null}", "Order");
        Assert.Contains("public class Order", code);
        Assert.Contains("public int Id", code); Assert.Contains("public long Big", code); Assert.Contains("public double Price", code); Assert.Contains("public bool Ok", code);
        Assert.Contains("public DateTime When", code); Assert.Contains("public Guid Guid", code);
        Assert.Contains("public Owner Owner", code); Assert.Contains("public class Owner", code);
        Assert.Contains("public List<string> Tags", code);
        Assert.Contains("public List<Row> Rows", code); Assert.Contains("public string? B", code);   // key missing in one element => nullable
        Assert.Contains("[JsonProperty(\"userId\")]", code); Assert.Contains("public int? UserId", code);   // name hint for null
    }

    [Fact]
    public void Generated_code_compiles()
    {
        var code = CSharpDtoGenerator.Generate("{\"Item1\":64,\"Item2\":\"s\",\"Item3\":null,\"class\":1,\"1st\":2,\"Nested\":{\"Nested\":{\"x\":1}}}", "Root");
        var tree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(code);
        Assert.Empty(tree.GetDiagnostics().Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
    }

    [Fact] public void Rejects_invalid_json() => Assert.Throws<ValidationException>(() => CSharpDtoGenerator.Generate("{nope"));
}

public class ComparerTests
{
    [Fact]
    public void Reports_added_removed_changed_with_paths()
    {
        var d = ResponseComparer.Compare("{\"a\":1,\"b\":[1,2],\"c\":{\"x\":1}}", "{\"a\":2,\"b\":[1],\"c\":{\"x\":1,\"y\":2}}");
        Assert.False(d.AreEqual);
        Assert.Contains(d.Differences, x => x.Path == "$.a" && x.Type == "Changed");
        Assert.Contains(d.Differences, x => x.Path == "$.b[1]" && x.Type == "Removed");
        Assert.Contains(d.Differences, x => x.Path == "$.c.y" && x.Type == "Added");
    }
    [Fact] public void Equal_documents_are_equal_regardless_of_formatting() => Assert.True(ResponseComparer.Compare("{\"a\":1}", "{ \"a\" : 1 }").AreEqual);
    [Fact] public void Non_json_falls_back_to_text_comparison() { Assert.True(ResponseComparer.Compare("x", "x").AreEqual); Assert.False(ResponseComparer.Compare("x", "y").AreEqual); }
}

public class AiHeuristicTests
{
    static ApiEndpoint Ep() => new() { Method = "POST", RouteTemplate = "api/v1/Client/{id}/X", PathParamsJson = "[{\"key\":\"id\",\"required\":true}]", BodyMode = "raw",
        BodyRaw = "{\"Email\":\"sample string 1\",\"Count\":3,\"Note\":null,\"Active\":true}" };

    [Fact]
    public void Required_fields_follow_path_and_non_null_samples()
    {
        var f = HeuristicAiAssistant.RequiredFields(Ep());
        Assert.True(f.Single(x => x.Name == "id").Required); Assert.True(f.Single(x => x.Name == "Count").Required); Assert.False(f.Single(x => x.Name == "Note").Required);
    }

    [Fact]
    public void Generated_data_is_name_aware_and_deterministic_per_seed()
    {
        var a = HeuristicAiAssistant.Fill(JObject.Parse(Ep().BodyRaw!), 1, "");
        Assert.Contains("@example.com", (string)a["Email"]!); Assert.Equal(a.ToString(), HeuristicAiAssistant.Fill(JObject.Parse(Ep().BodyRaw!), 1, "").ToString());
        Assert.NotEqual((string)a["Email"]!, (string)HeuristicAiAssistant.Fill(JObject.Parse(Ep().BodyRaw!), 2, "")["Email"]!);
    }

    [Fact] public void Documentation_and_explanation_mention_route_and_auth() { var e = Ep(); Assert.Contains("api/v1/Client/{id}/X", HeuristicAiAssistant.Explain(e)); Assert.Contains("Bearer", HeuristicAiAssistant.Document(e)); }
}

public class PasswordPolicyTests
{
    [Theory] [InlineData("short1A")] [InlineData("alllowercase123")] [InlineData("ALLUPPERCASE123")] [InlineData("NoDigitsHereAtAll")]
    public void Weak_passwords_are_rejected(string p) => Assert.Throws<ValidationException>(() => AccountService.CheckPassword(p));
    [Fact] public void Strong_password_is_accepted() => AccountService.CheckPassword("Str0ng-Passw0rd");
}
