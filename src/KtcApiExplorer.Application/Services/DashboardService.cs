using KtcApiExplorer.Application.Abstractions;
using KtcApiExplorer.Application.Common;
using KtcApiExplorer.Application.Dtos;
using KtcApiExplorer.Domain.Entities;
using KtcApiExplorer.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace KtcApiExplorer.Application.Services;

public class DashboardService : IDashboardService
{
    readonly IEndpointRepository _eps; readonly ICollectionRepository _cols; readonly IExecutionRepository _execs; readonly IApiExecutionService _exec;
    readonly IUnitOfWork _uow; readonly IAuditService _audit; readonly ILogger<DashboardService> _log;

    public DashboardService(IEndpointRepository eps, ICollectionRepository cols, IExecutionRepository execs, IApiExecutionService exec, IUnitOfWork uow, IAuditService audit, ILogger<DashboardService> log)
    { _eps = eps; _cols = cols; _execs = execs; _exec = exec; _uow = uow; _audit = audit; _log = log; }

    public async Task<DashboardDto> GetDashboardAsync(CancellationToken ct = default)
    {
        var since30 = DateTime.UtcNow.Date.AddDays(-29); var since14 = DateTime.UtcNow.Date.AddDays(-13);
        var counts = (await _eps.CountByMethodAsync(ct)).ToDictionary(c => c.Method.ToUpperInvariant(), c => c.Count);
        int C(string m) => counts.TryGetValue(m, out var n) ? n : 0;
        var (ok, fail) = await _execs.SuccessFailureAsync(since30, ct);
        return new DashboardDto
        {
            TotalApis = counts.Values.Sum(), GetApis = C("GET"), PostApis = C("POST"), PutApis = C("PUT"), DeleteApis = C("DELETE"),
            OtherApis = counts.Where(k => k.Key is not ("GET" or "POST" or "PUT" or "DELETE")).Sum(k => k.Value),
            Collections = (await _cols.ListAsync(ct)).Count,
            UsageByApi = (await _execs.TopUsedAsync(since30, 10, ct)).Select(u => new NameCount { Name = u.Name, Count = u.Calls }).ToList(),
            Success = ok, Failure = fail, Trend = Fill(await _execs.DailyStatsAsync(since14, null, ct), 14)
        };
    }

    public async Task<MonitoringDto> GetMonitoringAsync(CancellationToken ct = default)
    {
        var since = DateTime.UtcNow.AddHours(-24);
        var health = await _execs.HealthAsync(since, ct);
        return new MonitoringDto
        {
            Rows = health.Select(h => new HealthRowDto { EndpointId = h.EndpointId, Name = h.Name, Method = h.Method, Route = h.Route, LastStatus = h.LastStatus, LastSuccess = h.LastSuccess, LastDurationMs = h.LastDurationMs, LastCheckedUtc = h.LastCheckedUtc, AvgMs = Math.Round(h.AvgMs), Samples = h.Samples, Failures = h.Failures }).ToList(),
            ErrorTrend = Fill(await _execs.DailyStatsAsync(DateTime.UtcNow.Date.AddDays(-13), null, ct), 14)
        };
    }

    public async Task<List<HealthRowDto>> RunHealthChecksAsync(CancellationToken ct = default)
    {
        var monitored = await _eps.GetMonitoredAsync(ct);
        if (monitored.Count == 0) throw new ValidationException("No endpoints are flagged for monitoring. Open an endpoint in the Explorer and press 'Monitor'.");
        int ran = 0, skipped = 0;
        // Sequential on purpose: the scoped DbContext is not thread-safe and health checks should not hammer the KTC server.
        foreach (var ep in monitored.Take(100))
        {
            if (!RequestBuilder.IsSafeMethod(ep.Method) || (ep.RouteTemplate.Contains('{'))) { skipped++; continue; }
            var req = new ExecuteRequestDto { EndpointId = ep.Id, Query = RequestBuilder.ReadList(ep.QueryParamsJson) };
            try { await _exec.ExecuteAsync(req, ExecutionSources.HealthCheck, ct); ran++; }
            catch (KtcLoginRequiredException) { throw; }
            catch (AppException ex) { _log.LogWarning("Health check skipped for {Route}: {Msg}", ep.RouteTemplate, ex.Message); skipped++; }
        }
        await _audit.LogAsync("Monitoring.Run", null, null, $"{ran} executed, {skipped} skipped (non-GET or needs path params)", ct);
        return (await GetMonitoringAsync(ct)).Rows;
    }

    public async Task<bool> ToggleMonitoredAsync(int endpointId, CancellationToken ct = default)
    {
        var e = await _eps.GetAsync(endpointId, ct) ?? throw new NotFoundException("Endpoint not found.");
        e.IsMonitored = !e.IsMonitored;
        await _uow.SaveChangesAsync(ct);
        await _audit.LogAsync(e.IsMonitored ? "Monitoring.Enable" : "Monitoring.Disable", "ApiEndpoint", e.Id.ToString(), $"{e.Method} {e.RouteTemplate}", ct);
        return e.IsMonitored;
    }

    static List<DailyPointDto> Fill(List<Domain.Models.DailyStat> stats, int days)
    {
        var map = stats.ToDictionary(s => s.Day.Date);
        return Enumerable.Range(0, days).Select(i => DateTime.UtcNow.Date.AddDays(-(days - 1 - i))).Select(d =>
            map.TryGetValue(d, out var s) ? new DailyPointDto { Day = d.ToString("MM-dd"), Total = s.Total, Failures = s.Failures, AvgMs = Math.Round(s.AvgMs) }
                                          : new DailyPointDto { Day = d.ToString("MM-dd") }).ToList();
    }
}
