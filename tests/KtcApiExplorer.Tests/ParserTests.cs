using KtcApiExplorer.Application.Parsing;
using Xunit;

namespace KtcApiExplorer.Tests;

public class ParserTests
{
    static string Sample(string f) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "samples", f));

    [Fact]
    public void Parses_all_805_endpoints_of_the_real_KTC_collection()
    {
        var c = PostmanCollectionParser.Parse(Sample("KTC_REST_API.postman_collection.json"));
        Assert.Equal("KTC REST API", c.Name);
        Assert.Equal(805, c.Endpoints.Count);
        Assert.Equal(413, c.Endpoints.Count(e => e.Method == "GET"));
        Assert.Equal(201, c.Endpoints.Count(e => e.Method == "POST"));
        Assert.Equal(106, c.Endpoints.Count(e => e.Method == "PUT"));
        Assert.Equal(84, c.Endpoints.Count(e => e.Method == "DELETE"));
        // the only empty routes are the API root GET / HEAD
        Assert.Equal(new[] { "GET", "HEAD" }, c.Endpoints.Where(e => e.Route == "").Select(e => e.Method).OrderBy(m => m));
    }

    [Fact]
    public void Derives_route_from_item_name_when_url_is_empty()
    {
        var c = PostmanCollectionParser.Parse(Sample("KTC_REST_API.postman_collection.json"));
        var ep = c.Endpoints.First(e => e.Name == "GET api/v1/Action/{id}");
        Assert.Equal("api/v1/Action/{id}", ep.Route);
        Assert.Equal("id", Assert.Single(ep.PathParams).Key);
        var login = c.Endpoints.Single(e => e.Name == "POST api/v1/Login");
        Assert.Contains("\"AuthMode\"", login.BodyRaw);
        Assert.Equal("raw", login.BodyMode);
    }

    [Fact]
    public void Extracts_templated_query_parameters_as_required()
    {
        var c = PostmanCollectionParser.Parse(Sample("KTC_REST_API.postman_collection.json"));
        var ep = c.Endpoints.First(e => e.Route == "api/v1/Action/ActionsOfTypeWithStatus");
        Assert.Equal(new[] { "commandtypeID", "actionstatus" }, ep.Query.Select(q => q.Key));
        Assert.All(ep.Query, q => Assert.True(q.Required));
    }

    [Fact]
    public void Handles_nested_folders_absolute_urls_and_postman_path_variables()
    {
        const string json = """
        {"info":{"name":"X","schema":"https://schema.getpostman.com/json/collection/v2.1.0/collection.json"},
         "item":[{"name":"Users","item":[{"name":"Sub","item":[
           {"name":"get","request":{"method":"get","url":{"raw":"https://h.example.com/svc/users/:id?x=1","query":[{"key":"x","value":"1"}]},
            "header":[{"key":"A","value":"b"}]},"response":[{"name":"ok","code":200,"body":"{}"}]}]}]}]}
        """;
        var c = PostmanCollectionParser.Parse(json);
        var e = Assert.Single(c.Endpoints);
        Assert.Equal(new[] { "Users", "Sub" }, e.FolderPath);
        Assert.Equal("GET", e.Method);
        Assert.Equal("svc/users/{id}", e.Route);
        Assert.Equal("https://h.example.com", c.DetectedBaseUrl);
        Assert.Single(e.Examples);
        Assert.Equal("1", e.Query.Single().Value);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"foo\":1}")]
    public void Rejects_invalid_files(string json) =>
        Assert.Throws<KtcApiExplorer.Application.Common.ValidationException>(() => PostmanCollectionParser.Parse(json));

    [Fact]
    public void Help_doc_maps_every_postman_endpoint_to_a_group_and_description()
    {
        var entries = KtcHelpDocParser.Parse(Sample("KTC_REST_API_Documentation.html"));
        Assert.Equal(805, entries.Count);
        Assert.Equal(110, entries.Select(e => e.Group).Distinct().Count());
        var map = entries.ToDictionary(e => { var (m, r) = KtcHelpDocParser.SplitApi(e.Api); return KtcHelpDocParser.Key(m, r); }, e => e, StringComparer.Ordinal);
        Assert.Equal(805, map.Count);   // method + path + query signature is unique for every route
        var c = PostmanCollectionParser.Parse(Sample("KTC_REST_API.postman_collection.json"));
        var unmatched = c.Endpoints.Where(e => !map.ContainsKey(KtcHelpDocParser.Key(e.Method, e.Route, e.Query.Select(q => q.Key)))).Select(e => e.Name).ToList();
        Assert.Empty(unmatched);
        // identity incl. query signature is unique across the collection (no overload is lost on import)
        Assert.Equal(805, c.Endpoints.Select(e => e.Method + e.Route.ToLower() + "?" + e.QuerySignature).Distinct().Count());
        Assert.Equal("Get action details", map[KtcHelpDocParser.Key("GET", "api/v1/Action/{id}")].Description);
        Assert.Equal("Action", map[KtcHelpDocParser.Key("GET", "api/v1/Action/{id}")].Group);
    }
}
