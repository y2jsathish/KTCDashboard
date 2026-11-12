using KtcApiExplorer.Application.Abstractions;
using KtcApiExplorer.Application.CodeGen;
using KtcApiExplorer.Application.Common;
using KtcApiExplorer.Application.Dtos;
using KtcApiExplorer.Domain.Interfaces;

namespace KtcApiExplorer.Application.Services;

public class CodeGenerationService : ICodeGenerationService
{
    readonly IEndpointRepository _eps;
    public CodeGenerationService(IEndpointRepository eps) => _eps = eps;

    public async Task<CodeGenResultDto> GenerateAsync(CodeGenRequestDto r, CancellationToken ct = default)
    {
        var ep = await _eps.GetWithCollectionAsync(r.Request.EndpointId, ct) ?? throw new NotFoundException("Endpoint not found.");
        var lang = (r.Language ?? "curl").ToLowerInvariant();

        if (lang == "csharp-dto")
        {
            var json = !string.IsNullOrWhiteSpace(r.Json) ? r.Json : !string.IsNullOrWhiteSpace(r.Request.Body) ? r.Request.Body : ep.BodyRaw;
            if (string.IsNullOrWhiteSpace(json)) throw new ValidationException("There is no JSON payload to generate models from. Provide a request body or a sample response.");
            var root = r.RootName ?? SuggestName(ep) + "Request";
            return new CodeGenResultDto { Language = lang, Code = CSharpDtoGenerator.Generate(json, root) };
        }

        // token is replaced by a placeholder, never the real value
        var vars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["access_token"] = RequestBuilder.TokenPlaceholder, ["ktc_access_token"] = "<KTC_ACCESS_TOKEN>", ["baseUrl"] = ep.Collection!.BaseUrl };
        var http = RequestBuilder.Build(ep, ep.Collection!.BaseUrl, r.Request, vars);
        var code = lang switch
        {
            "curl" => CodeGenerators.Curl(http),
            "csharp-httpclient" => CodeGenerators.CSharpHttpClient(http),
            "csharp-restsharp" => CodeGenerators.CSharpRestSharp(http),
            "javascript-fetch" => CodeGenerators.JavaScriptFetch(http),
            _ => throw new ValidationException("Unsupported language: " + r.Language)
        };
        return new CodeGenResultDto { Language = lang, Code = code };
    }

    /// <summary>POST api/v1/Client/{id}/ActionsWithSecondLogin -> ClientActionsWithSecondLogin</summary>
    public static string SuggestName(Domain.Entities.ApiEndpoint ep)
    {
        var segs = ep.RouteTemplate.Split('/', StringSplitOptions.RemoveEmptyEntries).Where(s => !s.StartsWith('{') && !s.Equals("api", StringComparison.OrdinalIgnoreCase) && !System.Text.RegularExpressions.Regex.IsMatch(s, @"^v\d+$"));
        var name = string.Concat(segs.Select(CSharpDtoGenerator.Pascal));
        return string.IsNullOrEmpty(name) ? "Api" : name;
    }
}
