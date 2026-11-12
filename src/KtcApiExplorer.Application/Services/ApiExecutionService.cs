using KtcApiExplorer.Application.Abstractions;
using KtcApiExplorer.Application.Common;
using KtcApiExplorer.Application.Dtos;
using KtcApiExplorer.Application.Options;
using KtcApiExplorer.Domain.Entities;
using KtcApiExplorer.Domain.Interfaces;
using KtcApiExplorer.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace KtcApiExplorer.Application.Services;

public class ApiExecutionService : IApiExecutionService
{
    readonly IEndpointRepository _eps; readonly IExecutionRepository _execs; readonly IKtcAuthService _auth; readonly IApiHttpClient _http;
    readonly ISensitiveDataMasker _mask; readonly IUnitOfWork _uow; readonly ICurrentUser _user; readonly IAuditService _audit;
    readonly ExecutionOptions _opt; readonly ILogger<ApiExecutionService> _log;

    public ApiExecutionService(IEndpointRepository eps, IExecutionRepository execs, IKtcAuthService auth, IApiHttpClient http, ISensitiveDataMasker mask,
        IUnitOfWork uow, ICurrentUser user, IAuditService audit, IOptions<ExecutionOptions> opt, ILogger<ApiExecutionService> log)
    { _eps = eps; _execs = execs; _auth = auth; _http = http; _mask = mask; _uow = uow; _user = user; _audit = audit; _opt = opt.Value; _log = log; }

    public async Task<ExecuteResultDto> ExecuteAsync(ExecuteRequestDto req, string source = ExecutionSources.Manual, CancellationToken ct = default)
    {
        if (!_user.IsAuthenticated) throw new ForbiddenException("Sign in required.");
        var ep = await _eps.GetWithCollectionAsync(req.EndpointId, ct);
        if (ep == null || !ep.IsActive || ep.Collection is not { IsActive: true }) throw new NotFoundException("Endpoint not found.");

        // Authorization is enforced here (not only in the UI): Viewers may only call read-only methods.
        if (_user.Role == RoleNames.Viewer && !RequestBuilder.IsSafeMethod(ep.Method))
            throw new ForbiddenException($"Your role (Viewer) may only execute GET/HEAD/OPTIONS requests. {ep.Method} is not permitted.");

        var session = await _auth.GetValidSessionAsync(ct);
        var anonymousOk = ep.RouteTemplate.StartsWith("api/v1/Login", StringComparison.OrdinalIgnoreCase);
        if (session == null && !anonymousOk) throw new KtcLoginRequiredException();
        if (session != null && session.CollectionId != ep.CollectionId && !anonymousOk)
            throw new KtcLoginRequiredException("You are signed in to a different KTC environment. Sign in to this collection first.");

        ApiHttpRequest outbound = Build(ep, req, session);
        var resp = await _http.SendAsync(outbound, ct);

        // token rejected although we thought it valid -> renew once and retry
        if (resp.StatusCode == 401 && session != null)
        {
            try { await _auth.RefreshAsync(ct); session = await _auth.GetValidSessionAsync(ct); } catch (AppException) { session = null; }
            if (session != null) { outbound = Build(ep, req, session); resp = await _http.SendAsync(outbound, ct); }
        }

        var result = ToResult(outbound, resp);
        result.ExecutionId = await PersistAsync(ep, outbound, resp, result, source);
        await _audit.LogAsync("Api.Execute", "ApiEndpoint", ep.Id.ToString(), $"{ep.Method} {ep.RouteTemplate} -> {(resp.Error != null ? "ERR" : resp.StatusCode.ToString())} in {resp.DurationMs}ms [{source}]", ct);
        return result;
    }

    ApiHttpRequest Build(ApiEndpoint ep, ExecuteRequestDto req, KtcSession? s)
    {
        var vars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["baseUrl"] = ep.Collection!.BaseUrl };
        if (s != null) { vars["access_token"] = s.AccessToken; if (s.KtcAccessToken != null) vars["ktc_access_token"] = s.KtcAccessToken; }
        return RequestBuilder.Build(ep, ep.Collection!.BaseUrl, req, vars);
    }

    ExecuteResultDto ToResult(ApiHttpRequest o, ApiHttpResponse r) => new()
    {
        Method = o.Method, Url = o.Url, StatusCode = r.StatusCode, ReasonPhrase = r.ReasonPhrase, IsSuccess = r.IsSuccess, DurationMs = r.DurationMs,
        SizeBytes = r.SizeBytes, Headers = r.Headers, ContentType = r.ContentType, Body = r.Body, IsBinary = r.IsBinary, BodyBase64 = r.BodyBase64,
        Truncated = r.Truncated, Error = r.Error, ErrorCode = r.Error != null ? "network_error" : null,
        MaskedBody = r.Body == null ? null : MaskBody(r.Body, r.ContentType)
    };

    string MaskBody(string body, string? contentType)
    {
        var t = body.TrimStart();
        return (contentType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true || t.StartsWith('{') || t.StartsWith('[')) ? _mask.MaskJson(body) : _mask.MaskText(body) ?? "";
    }

    async Task<long?> PersistAsync(ApiEndpoint ep, ApiHttpRequest o, ApiHttpResponse r, ExecuteResultDto result, string source)
    {
        try
        {
            string? stored = null;
            if (_opt.StoreResponseBody && result.MaskedBody != null)
                stored = result.MaskedBody.Length > _opt.MaxStoredBodyChars ? result.MaskedBody[.._opt.MaxStoredBodyChars] + "\n…[truncated]" : result.MaskedBody;
            var exec = new ApiExecution
            {
                EndpointId = ep.Id, UserId = _user.UserId, Source = source, Method = o.Method, Url = o.Url,
                RequestHeadersJson = JsonConvert.SerializeObject(_mask.MaskHeaders(o.Headers)),
                RequestBody = o.Body == null ? null : (o.Body.TrimStart().StartsWith('{') || o.Body.TrimStart().StartsWith('[') ? _mask.MaskJson(o.Body) : _mask.MaskText(o.Body)),
                StatusCode = r.StatusCode == 0 ? null : r.StatusCode, DurationMs = r.DurationMs, ResponseSizeBytes = r.SizeBytes,
                ResponseHeadersJson = JsonConvert.SerializeObject(_mask.MaskHeaders(r.Headers)), ResponseBody = stored,
                IsSuccess = r.IsSuccess, ErrorMessage = r.Error
            };
            _execs.Add(exec);
            await _uow.SaveChangesAsync();
            return exec.Id;
        }
        catch (Exception ex) { _log.LogError(ex, "Could not persist execution history"); return null; }
    }

    public async Task<PagedResult<ExecutionListItemDto>> GetHistoryAsync(int? endpointId, bool? success, int skip, int take, CancellationToken ct = default)
    {
        var filter = new ExecutionFilter(endpointId, OwnerFilter(), success, null, Math.Max(0, skip), Math.Clamp(take, 1, 200));
        var (items, total) = await _execs.SearchAsync(filter, ct);
        return new PagedResult<ExecutionListItemDto> { Total = total, Items = items.Select(MapItem).ToList() };
    }

    public async Task<ExecutionDetailDto> GetExecutionAsync(long id, CancellationToken ct = default)
    {
        var e = await GetOwnedAsync(id, ct);
        return new ExecutionDetailDto
        {
            Id = e.Id, EndpointId = e.EndpointId, Method = e.Method, Url = e.Url, StatusCode = e.StatusCode, IsSuccess = e.IsSuccess, DurationMs = e.DurationMs, Source = e.Source, ExecutedUtc = e.ExecutedUtc,
            RequestHeadersJson = e.RequestHeadersJson, RequestBody = e.RequestBody, ResponseHeadersJson = e.ResponseHeadersJson, ResponseBody = e.ResponseBody, ErrorMessage = e.ErrorMessage
        };
    }

    public async Task<DiffResultDto> CompareExecutionsAsync(long leftId, long rightId, CancellationToken ct = default)
    {
        var l = await GetOwnedAsync(leftId, ct); var r = await GetOwnedAsync(rightId, ct);
        return ResponseComparer.Compare(l.ResponseBody, r.ResponseBody);
    }

    int? OwnerFilter() => _user.Role == RoleNames.Admin ? null : _user.UserId ?? -1;
    async Task<ApiExecution> GetOwnedAsync(long id, CancellationToken ct)
    {
        var e = await _execs.GetAsync(id, ct) ?? throw new NotFoundException("Execution not found.");
        if (_user.Role != RoleNames.Admin && e.UserId != _user.UserId) throw new NotFoundException("Execution not found.");
        return e;
    }
    static ExecutionListItemDto MapItem(ApiExecution e) => new() { Id = e.Id, EndpointId = e.EndpointId, Method = e.Method, Url = e.Url, StatusCode = e.StatusCode, IsSuccess = e.IsSuccess, DurationMs = e.DurationMs, Source = e.Source, ExecutedUtc = e.ExecutedUtc };
}
