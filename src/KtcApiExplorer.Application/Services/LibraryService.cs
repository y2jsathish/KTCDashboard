using KtcApiExplorer.Application.Abstractions;
using KtcApiExplorer.Application.Common;
using KtcApiExplorer.Application.Dtos;
using KtcApiExplorer.Domain.Entities;
using KtcApiExplorer.Domain.Interfaces;
using Newtonsoft.Json;

namespace KtcApiExplorer.Application.Services;

public class LibraryService : ILibraryService
{
    readonly IFavoriteRepository _favs; readonly ILibraryRepository _lib; readonly IExecutionRepository _execs; readonly IEndpointRepository _eps;
    readonly IApiExecutionService _exec; readonly IUnitOfWork _uow; readonly ICurrentUser _user; readonly IAuditService _audit;

    public LibraryService(IFavoriteRepository favs, ILibraryRepository lib, IExecutionRepository execs, IEndpointRepository eps, IApiExecutionService exec, IUnitOfWork uow, ICurrentUser user, IAuditService audit)
    { _favs = favs; _lib = lib; _execs = execs; _eps = eps; _exec = exec; _uow = uow; _user = user; _audit = audit; }

    int Uid => _user.UserId ?? throw new ForbiddenException("Sign in required.");
    bool IsAdmin => _user.Role == RoleNames.Admin;

    public async Task<bool> ToggleFavoriteAsync(int endpointId, CancellationToken ct = default)
    {
        if (await _eps.GetAsync(endpointId, ct) == null) throw new NotFoundException("Endpoint not found.");
        return await _favs.ToggleAsync(Uid, endpointId, ct);
    }

    public async Task<List<TreeEndpointDto>> GetFavoritesAsync(CancellationToken ct = default) =>
        await ToTreeAsync(await _favs.GetEndpointIdsAsync(Uid, ct), ct, favorite: true);

    public async Task<List<TreeEndpointDto>> GetRecentAsync(int take, CancellationToken ct = default)
    {
        var ids = await _execs.RecentEndpointIdsAsync(Uid, Math.Clamp(take, 1, 50), ct);
        var favs = (await _favs.GetEndpointIdsAsync(Uid, ct)).ToHashSet();
        var list = await ToTreeAsync(ids, ct);
        foreach (var l in list) l.IsFavorite = favs.Contains(l.Id);
        return list;
    }

    async Task<List<TreeEndpointDto>> ToTreeAsync(IEnumerable<int> ids, CancellationToken ct, bool favorite = false)
    {
        var order = ids.ToList();
        var eps = (await _eps.GetByIdsAsync(order, ct)).Where(e => e.IsActive).ToDictionary(e => e.Id);
        return order.Where(eps.ContainsKey).Select(i => new TreeEndpointDto { Id = i, Method = eps[i].Method, Route = eps[i].RouteTemplate, Summary = eps[i].Description, IsFavorite = favorite }).ToList();
    }

    // ---------- templates ----------
    public async Task<List<TemplateDto>> GetTemplatesAsync(int? endpointId, CancellationToken ct = default) =>
        (await _lib.ListTemplatesAsync(Uid, endpointId, ct)).Select(t => MapT(t, true)).ToList();

    public async Task<TemplateDto> SaveTemplateAsync(SaveTemplateDto dto, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(dto.Name)) throw new ValidationException("Template name is required.");
        if (await _eps.GetAsync(dto.EndpointId, ct) == null) throw new NotFoundException("Endpoint not found.");
        var t = new RequestTemplate { UserId = Uid, EndpointId = dto.EndpointId, Name = dto.Name.Trim(), Description = dto.Description, IsShared = dto.IsShared, RequestJson = Serialize(dto.Request) };
        _lib.Add(t); await _uow.SaveChangesAsync(ct);
        await _audit.LogAsync("Template.Save", "RequestTemplate", t.Id.ToString(), t.Name, ct);
        return MapT(t, true);
    }

    public async Task DeleteTemplateAsync(int id, CancellationToken ct = default)
    {
        var t = await _lib.GetTemplateAsync(id, ct) ?? throw new NotFoundException("Template not found.");
        if (t.UserId != Uid && !IsAdmin) throw new ForbiddenException("You can only delete your own templates.");
        _lib.Remove(t); await _uow.SaveChangesAsync(ct);
        await _audit.LogAsync("Template.Delete", "RequestTemplate", id.ToString(), t.Name, ct);
    }

    // ---------- test cases ----------
    public async Task<List<TestCaseDto>> GetTestCasesAsync(int? endpointId, CancellationToken ct = default) =>
        (await _lib.ListTestCasesAsync(Uid, endpointId, ct)).Select(MapTc).ToList();

    public async Task<TestCaseDto> SaveTestCaseAsync(SaveTestCaseDto dto, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(dto.Name)) throw new ValidationException("Test case name is required.");
        if (await _eps.GetAsync(dto.EndpointId, ct) == null) throw new NotFoundException("Endpoint not found.");
        var t = new TestCase { UserId = Uid, EndpointId = dto.EndpointId, Name = dto.Name.Trim(), ExpectedStatus = dto.ExpectedStatus, ExpectedBodyContains = dto.ExpectedBodyContains, RequestJson = Serialize(dto.Request) };
        _lib.Add(t); await _uow.SaveChangesAsync(ct);
        await _audit.LogAsync("TestCase.Save", "TestCase", t.Id.ToString(), t.Name, ct);
        return MapTc(t);
    }

    public async Task DeleteTestCaseAsync(int id, CancellationToken ct = default)
    {
        var t = await _lib.GetTestCaseAsync(id, ct) ?? throw new NotFoundException("Test case not found.");
        if (t.UserId != Uid && !IsAdmin) throw new ForbiddenException("You can only delete your own test cases.");
        _lib.Remove(t); await _uow.SaveChangesAsync(ct);
        await _audit.LogAsync("TestCase.Delete", "TestCase", id.ToString(), t.Name, ct);
    }

    public async Task<TestRunResultDto> RunTestCaseAsync(int id, bool saveAsBaseline, CancellationToken ct = default)
    {
        var tc = await _lib.GetTestCaseAsync(id, ct) ?? throw new NotFoundException("Test case not found.");
        if (tc.UserId != Uid && !IsAdmin) throw new ForbiddenException("You can only run your own test cases.");
        var req = JsonConvert.DeserializeObject<ExecuteRequestDto>(tc.RequestJson) ?? new ExecuteRequestDto();
        req.EndpointId = tc.EndpointId;

        var result = await _exec.ExecuteAsync(req, ExecutionSources.TestCase, ct);
        var problems = new List<string>();
        if (tc.ExpectedStatus is int es && result.StatusCode != es) problems.Add($"expected status {es} but got {result.StatusCode}");
        if (!string.IsNullOrEmpty(tc.ExpectedBodyContains) && (result.Body ?? "").IndexOf(tc.ExpectedBodyContains, StringComparison.OrdinalIgnoreCase) < 0) problems.Add($"body does not contain '{tc.ExpectedBodyContains}'");
        if (tc.ExpectedStatus == null && string.IsNullOrEmpty(tc.ExpectedBodyContains) && !result.IsSuccess) problems.Add($"request failed ({(result.Error ?? result.StatusCode.ToString())})");

        var run = new TestRunResultDto { TestCaseId = id, Result = result, Passed = problems.Count == 0 };
        run.Message = run.Passed ? "Passed" : "Failed: " + string.Join("; ", problems);
        if (tc.BaselineResponse != null && result.MaskedBody != null) run.BaselineDiff = ResponseComparer.Compare(tc.BaselineResponse, result.MaskedBody);
        if (saveAsBaseline && result.MaskedBody != null) tc.BaselineResponse = result.MaskedBody.Length > 60_000 ? result.MaskedBody[..60_000] : result.MaskedBody;
        tc.LastResult = run.Passed ? "Passed" : "Failed"; tc.LastMessage = run.Message; tc.LastRunUtc = DateTime.UtcNow;
        await _uow.SaveChangesAsync(ct);
        return run;
    }

    // ---------- helpers ----------
    /// <summary>Auth headers are never persisted in templates / test cases.</summary>
    static string Serialize(ExecuteRequestDto r)
    {
        var copy = new ExecuteRequestDto { EndpointId = r.EndpointId, PathParams = r.PathParams, Query = r.Query, Body = r.Body, Headers = r.Headers.Where(h => !SensitiveDataMasker.IsSensitiveKey(h.Key)).ToList() };
        return JsonConvert.SerializeObject(copy);
    }
    TemplateDto MapT(RequestTemplate t, bool withRequest) => new()
    {
        Id = t.Id, EndpointId = t.EndpointId, EndpointName = t.Endpoint == null ? "" : $"{t.Endpoint.Method} {t.Endpoint.RouteTemplate}", Name = t.Name, Description = t.Description,
        IsShared = t.IsShared, IsMine = t.UserId == _user.UserId, UpdatedUtc = t.UpdatedUtc,
        Request = withRequest ? JsonConvert.DeserializeObject<ExecuteRequestDto>(t.RequestJson) : null
    };
    static TestCaseDto MapTc(TestCase t) => new()
    {
        Id = t.Id, EndpointId = t.EndpointId, EndpointName = t.Endpoint == null ? "" : $"{t.Endpoint.Method} {t.Endpoint.RouteTemplate}", Name = t.Name, ExpectedStatus = t.ExpectedStatus,
        ExpectedBodyContains = t.ExpectedBodyContains, HasBaseline = t.BaselineResponse != null, LastResult = t.LastResult, LastMessage = t.LastMessage, LastRunUtc = t.LastRunUtc,
        Request = JsonConvert.DeserializeObject<ExecuteRequestDto>(t.RequestJson)
    };
}
