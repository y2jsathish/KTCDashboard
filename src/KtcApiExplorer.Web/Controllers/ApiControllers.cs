using KtcApiExplorer.Application.Abstractions;
using KtcApiExplorer.Application.Common;
using KtcApiExplorer.Application.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace KtcApiExplorer.Web.Controllers;

[ApiController, Route("api/explorer")]
public class ExplorerApiController : ControllerBase
{
    readonly IExplorerService _explorer; readonly IApiExecutionService _exec; readonly ILibraryService _lib;
    public ExplorerApiController(IExplorerService explorer, IApiExecutionService exec, ILibraryService lib) { _explorer = explorer; _exec = exec; _lib = lib; }

    [HttpGet("collections")] public async Task<IActionResult> Collections(CancellationToken ct) => Ok(await _explorer.GetCollectionsAsync(ct));
    [HttpGet("collections/{id:int}/tree")] public async Task<IActionResult> Tree(int id, CancellationToken ct) => Ok(await _explorer.GetTreeAsync(id, ct));
    [HttpGet("endpoints/{id:int}")] public async Task<IActionResult> Endpoint(int id, CancellationToken ct) => Ok(await _explorer.GetEndpointAsync(id, ct));
    [HttpPost("execute")] public async Task<IActionResult> Execute([FromBody] ExecuteRequestDto req, CancellationToken ct) => Ok(await _exec.ExecuteAsync(req, ct: ct));
    [HttpPost("endpoints/{id:int}/favorite")] public async Task<IActionResult> Favorite(int id, CancellationToken ct) => Ok(new { isFavorite = await _lib.ToggleFavoriteAsync(id, ct) });
}

[ApiController, Route("api/ktc")]
public class KtcApiController : ControllerBase
{
    readonly IKtcAuthService _auth; public KtcApiController(IKtcAuthService auth) => _auth = auth;
    [HttpPost("login"), EnableRateLimiting("login")] public async Task<IActionResult> Login([FromBody] KtcLoginRequestDto r, CancellationToken ct) => Ok(await _auth.LoginAsync(r, ct));
    [HttpGet("status")] public IActionResult Status() => Ok(_auth.GetStatus());
    [HttpPost("refresh")] public async Task<IActionResult> Refresh(CancellationToken ct) => Ok(await _auth.RefreshAsync(ct));
    [HttpPost("logout")] public async Task<IActionResult> Logout(CancellationToken ct) { await _auth.LogoutAsync(ct); return Ok(_auth.GetStatus()); }
}

[ApiController, Route("api/tools")]
public class ToolsApiController : ControllerBase
{
    readonly ICodeGenerationService _codegen; readonly IAiAssistant _ai;
    public ToolsApiController(ICodeGenerationService codegen, IAiAssistant ai) { _codegen = codegen; _ai = ai; }

    [HttpPost("codegen")] public async Task<IActionResult> CodeGen([FromBody] CodeGenRequestDto r, CancellationToken ct) => Ok(await _codegen.GenerateAsync(r, ct));

    [HttpPost("ai/{kind}")]
    public async Task<IActionResult> Ai(string kind, [FromBody] AiRequestDto r, CancellationToken ct) => Ok(kind switch
    {
        "explain" => await _ai.ExplainAsync(r.EndpointId, ct),
        "request-body" => await _ai.GenerateRequestBodyAsync(r.EndpointId, ct),
        "test-data" => await _ai.GenerateTestDataAsync(r.EndpointId, r.Count, ct),
        "required-fields" => await _ai.IdentifyRequiredFieldsAsync(r.EndpointId, ct),
        "documentation" => await _ai.GenerateDocumentationAsync(r.EndpointId, ct),
        _ => throw new NotFoundException("Unknown AI action.")
    });
}

[ApiController, Route("api/library")]
public class LibraryApiController : ControllerBase
{
    readonly ILibraryService _lib; readonly IApiExecutionService _exec;
    public LibraryApiController(ILibraryService lib, IApiExecutionService exec) { _lib = lib; _exec = exec; }

    [HttpGet("favorites")] public async Task<IActionResult> Favorites(CancellationToken ct) => Ok(await _lib.GetFavoritesAsync(ct));
    [HttpGet("recent")] public async Task<IActionResult> Recent(int take = 20, CancellationToken ct = default) => Ok(await _lib.GetRecentAsync(take, ct));

    [HttpGet("templates")] public async Task<IActionResult> Templates(int? endpointId, CancellationToken ct) => Ok(await _lib.GetTemplatesAsync(endpointId, ct));
    [HttpPost("templates")] public async Task<IActionResult> SaveTemplate([FromBody] SaveTemplateDto dto, CancellationToken ct) => Ok(await _lib.SaveTemplateAsync(dto, ct));
    [HttpDelete("templates/{id:int}")] public async Task<IActionResult> DeleteTemplate(int id, CancellationToken ct) { await _lib.DeleteTemplateAsync(id, ct); return NoContent(); }

    [HttpGet("testcases")] public async Task<IActionResult> TestCases(int? endpointId, CancellationToken ct) => Ok(await _lib.GetTestCasesAsync(endpointId, ct));
    [HttpPost("testcases")] public async Task<IActionResult> SaveTestCase([FromBody] SaveTestCaseDto dto, CancellationToken ct) => Ok(await _lib.SaveTestCaseAsync(dto, ct));
    [HttpDelete("testcases/{id:int}")] public async Task<IActionResult> DeleteTestCase(int id, CancellationToken ct) { await _lib.DeleteTestCaseAsync(id, ct); return NoContent(); }
    [HttpPost("testcases/{id:int}/run")] public async Task<IActionResult> RunTestCase(int id, bool baseline = false, CancellationToken ct = default) => Ok(await _lib.RunTestCaseAsync(id, baseline, ct));

    [HttpGet("history")] public async Task<IActionResult> History(int? endpointId, bool? success, int skip = 0, int take = 50, CancellationToken ct = default) => Ok(await _exec.GetHistoryAsync(endpointId, success, skip, take, ct));
    [HttpGet("history/{id:long}")] public async Task<IActionResult> HistoryItem(long id, CancellationToken ct) => Ok(await _exec.GetExecutionAsync(id, ct));
    [HttpGet("history/compare")] public async Task<IActionResult> Compare(long left, long right, CancellationToken ct) => Ok(await _exec.CompareExecutionsAsync(left, right, ct));
}

[ApiController, Route("api/monitoring")]
public class MonitoringApiController : ControllerBase
{
    readonly IDashboardService _dash; public MonitoringApiController(IDashboardService dash) => _dash = dash;
    [HttpGet("dashboard")] public async Task<IActionResult> Dashboard(CancellationToken ct) => Ok(await _dash.GetDashboardAsync(ct));
    [HttpGet("health")] public async Task<IActionResult> Health(CancellationToken ct) => Ok(await _dash.GetMonitoringAsync(ct));
    [HttpPost("run"), Authorize(Policy = "Developer")] public async Task<IActionResult> Run(CancellationToken ct) => Ok(await _dash.RunHealthChecksAsync(ct));
    [HttpPost("toggle/{endpointId:int}"), Authorize(Policy = "Admin")] public async Task<IActionResult> Toggle(int endpointId, CancellationToken ct) => Ok(new { isMonitored = await _dash.ToggleMonitoredAsync(endpointId, ct) });
}
