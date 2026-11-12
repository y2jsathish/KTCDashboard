using KtcApiExplorer.Domain.Entities;
using KtcApiExplorer.Domain.Interfaces;
using KtcApiExplorer.Domain.Models;
using KtcApiExplorer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KtcApiExplorer.Infrastructure.Repositories;

public class UserRepository : IUserRepository
{
    readonly AppDbContext _db; public UserRepository(AppDbContext db) => _db = db;
    public Task<AppUser?> GetByUserNameAsync(string userName, CancellationToken ct = default) => _db.Users.Include(u => u.Role).FirstOrDefaultAsync(u => u.UserName == userName, ct);
    public Task<AppUser?> GetByIdAsync(int id, CancellationToken ct = default) => _db.Users.Include(u => u.Role).FirstOrDefaultAsync(u => u.Id == id, ct);
    public Task<List<AppUser>> ListAsync(CancellationToken ct = default) => _db.Users.Include(u => u.Role).OrderBy(u => u.UserName).ToListAsync(ct);
    public Task<List<Role>> ListRolesAsync(CancellationToken ct = default) => _db.Roles.OrderBy(r => r.Id).ToListAsync(ct);
    public Task<Role?> GetRoleByNameAsync(string name, CancellationToken ct = default) => _db.Roles.FirstOrDefaultAsync(r => r.Name == name, ct);
    public Task<int> CountAsync(CancellationToken ct = default) => _db.Users.CountAsync(ct);
    public void Add(AppUser user) => _db.Users.Add(user);
    public void AddRole(Role role) => _db.Roles.Add(role);
}

public class CollectionRepository : ICollectionRepository
{
    readonly AppDbContext _db; public CollectionRepository(AppDbContext db) => _db = db;
    public Task<List<ApiCollection>> ListAsync(CancellationToken ct = default) => _db.ApiCollections.Where(c => c.IsActive).OrderBy(c => c.Name).ToListAsync(ct);
    public Task<ApiCollection?> GetAsync(int id, CancellationToken ct = default) => _db.ApiCollections.FirstOrDefaultAsync(c => c.Id == id, ct);
    public Task<ApiCollection?> GetByNameAsync(string name, CancellationToken ct = default) => _db.ApiCollections.FirstOrDefaultAsync(c => c.Name == name, ct);
    public void Add(ApiCollection c) => _db.ApiCollections.Add(c);
    public void Remove(ApiCollection c) => _db.ApiCollections.Remove(c);
    public Task<List<ApiFolder>> GetFoldersAsync(int collectionId, CancellationToken ct = default) => _db.ApiFolders.Where(f => f.CollectionId == collectionId).ToListAsync(ct);
    public Task<List<ApiEndpoint>> GetEndpointsAsync(int collectionId, bool includeInactive = false, CancellationToken ct = default) =>
        _db.ApiEndpoints.Include(e => e.Folder).Where(e => e.CollectionId == collectionId && (includeInactive || e.IsActive)).OrderBy(e => e.SortOrder).ToListAsync(ct);
    public void AddFolder(ApiFolder f) => _db.ApiFolders.Add(f);
    public void AddEndpoint(ApiEndpoint e) => _db.ApiEndpoints.Add(e);
}

public class EndpointRepository : IEndpointRepository
{
    readonly AppDbContext _db; public EndpointRepository(AppDbContext db) => _db = db;
    public Task<ApiEndpoint?> GetAsync(int id, CancellationToken ct = default) => _db.ApiEndpoints.FirstOrDefaultAsync(e => e.Id == id, ct);
    public Task<ApiEndpoint?> GetWithCollectionAsync(int id, CancellationToken ct = default) => _db.ApiEndpoints.Include(e => e.Collection).FirstOrDefaultAsync(e => e.Id == id, ct);
    public Task<List<ApiEndpoint>> GetMonitoredAsync(CancellationToken ct = default) => _db.ApiEndpoints.Where(e => e.IsMonitored && e.IsActive).OrderBy(e => e.RouteTemplate).ToListAsync(ct);
    public Task<List<ApiEndpoint>> GetByIdsAsync(IEnumerable<int> ids, CancellationToken ct = default) { var l = ids.ToList(); return _db.ApiEndpoints.Where(e => l.Contains(e.Id)).ToListAsync(ct); }
    public async Task<List<MethodCount>> CountByMethodAsync(CancellationToken ct = default) =>
        (await _db.ApiEndpoints.Where(e => e.IsActive && e.Collection!.IsActive).GroupBy(e => e.Method).Select(g => new { g.Key, N = g.Count() }).ToListAsync(ct)).Select(x => new MethodCount(x.Key, x.N)).ToList();
    public Task<int> CountAsync(CancellationToken ct = default) => _db.ApiEndpoints.CountAsync(e => e.IsActive, ct);
}

public class ExecutionRepository : IExecutionRepository
{
    readonly AppDbContext _db; public ExecutionRepository(AppDbContext db) => _db = db;
    public void Add(ApiExecution e) => _db.ApiExecutions.Add(e);
    public Task<ApiExecution?> GetAsync(long id, CancellationToken ct = default) => _db.ApiExecutions.FirstOrDefaultAsync(e => e.Id == id, ct);

    public async Task<(List<ApiExecution> Items, int Total)> SearchAsync(ExecutionFilter f, CancellationToken ct = default)
    {
        var q = _db.ApiExecutions.AsNoTracking().AsQueryable();
        if (f.EndpointId != null) q = q.Where(e => e.EndpointId == f.EndpointId);
        if (f.UserId != null) q = q.Where(e => e.UserId == f.UserId);
        if (f.Success != null) q = q.Where(e => e.IsSuccess == f.Success);
        if (f.Source != null) q = q.Where(e => e.Source == f.Source);
        var total = await q.CountAsync(ct);
        // list views never need the (large) bodies
        var items = await q.OrderByDescending(e => e.ExecutedUtc).ThenByDescending(e => e.Id).Skip(f.Skip).Take(f.Take)
            .Select(e => new ApiExecution { Id = e.Id, EndpointId = e.EndpointId, UserId = e.UserId, Source = e.Source, Method = e.Method, Url = e.Url, StatusCode = e.StatusCode, DurationMs = e.DurationMs, IsSuccess = e.IsSuccess, ExecutedUtc = e.ExecutedUtc })
            .ToListAsync(ct);
        return (items, total);
    }

    public Task<List<ApiExecution>> GetLatestForEndpointAsync(int endpointId, int take, CancellationToken ct = default) =>
        _db.ApiExecutions.AsNoTracking().Where(e => e.EndpointId == endpointId).OrderByDescending(e => e.ExecutedUtc).Take(take).ToListAsync(ct);

    public async Task<List<UsageByEndpoint>> TopUsedAsync(DateTime sinceUtc, int take, CancellationToken ct = default)
    {
        var top = await _db.ApiExecutions.Where(e => e.ExecutedUtc >= sinceUtc && e.EndpointId != null && e.Source == ExecutionSources.Manual)
            .GroupBy(e => e.EndpointId).Select(g => new { Id = g.Key, N = g.Count() }).OrderByDescending(x => x.N).Take(take).ToListAsync(ct);
        var ids = top.Select(t => t.Id!.Value).ToList();
        var names = await _db.ApiEndpoints.Where(e => ids.Contains(e.Id)).Select(e => new { e.Id, e.Method, e.RouteTemplate }).ToDictionaryAsync(e => e.Id, ct);
        return top.Select(t => new UsageByEndpoint(t.Id, names.TryGetValue(t.Id!.Value, out var n) ? n.Method + " " + n.RouteTemplate : "(deleted)", t.N)).ToList();
    }

    public async Task<(int Success, int Failure)> SuccessFailureAsync(DateTime sinceUtc, CancellationToken ct = default)
    {
        var g = await _db.ApiExecutions.Where(e => e.ExecutedUtc >= sinceUtc).GroupBy(e => e.IsSuccess).Select(x => new { x.Key, N = x.Count() }).ToListAsync(ct);
        return (g.FirstOrDefault(x => x.Key)?.N ?? 0, g.FirstOrDefault(x => !x.Key)?.N ?? 0);
    }

    // Aggregated in memory over a 3-column projection: portable across SQL Server/SQLite and cheap for a 14-30 day window.
    public async Task<List<DailyStat>> DailyStatsAsync(DateTime sinceUtc, string? source, CancellationToken ct = default)
    {
        var q = _db.ApiExecutions.AsNoTracking().Where(e => e.ExecutedUtc >= sinceUtc);
        if (source != null) q = q.Where(e => e.Source == source);
        var rows = await q.Select(e => new { e.ExecutedUtc, e.IsSuccess, e.DurationMs }).ToListAsync(ct);
        return rows.GroupBy(r => r.ExecutedUtc.Date).Select(g => new DailyStat(g.Key, g.Count(), g.Count(x => !x.IsSuccess), g.Average(x => (double)x.DurationMs))).OrderBy(d => d.Day).ToList();
    }

    public async Task<List<EndpointHealth>> HealthAsync(DateTime sinceUtc, CancellationToken ct = default)
    {
        var monitored = await _db.ApiEndpoints.AsNoTracking().Where(e => e.IsMonitored && e.IsActive).ToListAsync(ct);
        var ids = monitored.Select(m => m.Id).ToList();
        var rows = await _db.ApiExecutions.AsNoTracking().Where(e => e.EndpointId != null && ids.Contains(e.EndpointId.Value) && e.ExecutedUtc >= sinceUtc)
            .Select(e => new { EndpointId = e.EndpointId!.Value, e.StatusCode, e.IsSuccess, e.DurationMs, e.ExecutedUtc }).ToListAsync(ct);
        var by = rows.GroupBy(r => r.EndpointId).ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.ExecutedUtc).ToList());
        return monitored.Select(m =>
        {
            if (!by.TryGetValue(m.Id, out var l)) return new EndpointHealth(m.Id, m.Name, m.Method, m.RouteTemplate, null, null, null, null, 0, 0, 0);
            var last = l[0];
            return new EndpointHealth(m.Id, m.Name, m.Method, m.RouteTemplate, last.StatusCode, last.IsSuccess, last.DurationMs, last.ExecutedUtc, l.Average(x => (double)x.DurationMs), l.Count, l.Count(x => !x.IsSuccess));
        }).ToList();
    }

    public async Task<List<int>> RecentEndpointIdsAsync(int userId, int take, CancellationToken ct = default) =>
        (await _db.ApiExecutions.Where(e => e.UserId == userId && e.EndpointId != null && e.Source == ExecutionSources.Manual)
            .GroupBy(e => e.EndpointId).Select(g => new { Id = g.Key, Last = g.Max(x => x.ExecutedUtc) }).OrderByDescending(x => x.Last).Take(take).ToListAsync(ct))
        .Select(x => x.Id!.Value).ToList();
}

public class FavoriteRepository : IFavoriteRepository
{
    readonly AppDbContext _db; public FavoriteRepository(AppDbContext db) => _db = db;
    public Task<List<int>> GetEndpointIdsAsync(int userId, CancellationToken ct = default) => _db.ApiFavorites.Where(f => f.UserId == userId).OrderByDescending(f => f.CreatedUtc).Select(f => f.EndpointId).ToListAsync(ct);
    public async Task<bool> ToggleAsync(int userId, int endpointId, CancellationToken ct = default)
    {
        var existing = await _db.ApiFavorites.FirstOrDefaultAsync(f => f.UserId == userId && f.EndpointId == endpointId, ct);
        if (existing != null) { _db.ApiFavorites.Remove(existing); await _db.SaveChangesAsync(ct); return false; }
        _db.ApiFavorites.Add(new ApiFavorite { UserId = userId, EndpointId = endpointId });
        await _db.SaveChangesAsync(ct);
        return true;
    }
}

public class LibraryRepository : ILibraryRepository
{
    readonly AppDbContext _db; public LibraryRepository(AppDbContext db) => _db = db;
    public Task<List<RequestTemplate>> ListTemplatesAsync(int userId, int? endpointId, CancellationToken ct = default) =>
        _db.RequestTemplates.Include(t => t.Endpoint).Where(t => (t.UserId == userId || t.IsShared) && (endpointId == null || t.EndpointId == endpointId)).OrderByDescending(t => t.UpdatedUtc).ToListAsync(ct);
    public Task<RequestTemplate?> GetTemplateAsync(int id, CancellationToken ct = default) => _db.RequestTemplates.FirstOrDefaultAsync(t => t.Id == id, ct);
    public void Add(RequestTemplate t) => _db.RequestTemplates.Add(t);
    public void Remove(RequestTemplate t) => _db.RequestTemplates.Remove(t);
    public Task<List<TestCase>> ListTestCasesAsync(int userId, int? endpointId, CancellationToken ct = default) =>
        _db.TestCases.Include(t => t.Endpoint).Where(t => t.UserId == userId && (endpointId == null || t.EndpointId == endpointId)).OrderByDescending(t => t.CreatedUtc).ToListAsync(ct);
    public Task<TestCase?> GetTestCaseAsync(int id, CancellationToken ct = default) => _db.TestCases.FirstOrDefaultAsync(t => t.Id == id, ct);
    public void Add(TestCase t) => _db.TestCases.Add(t);
    public void Remove(TestCase t) => _db.TestCases.Remove(t);
}

public class AuditRepository : IAuditRepository
{
    readonly AppDbContext _db; public AuditRepository(AppDbContext db) => _db = db;
    public void Add(AuditLog log) => _db.AuditLogs.Add(log);
    public async Task<(List<AuditLog> Items, int Total)> SearchAsync(string? action, string? user, int skip, int take, CancellationToken ct = default)
    {
        var q = _db.AuditLogs.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(action)) q = q.Where(a => a.Action.Contains(action));
        if (!string.IsNullOrWhiteSpace(user)) q = q.Where(a => a.UserName.Contains(user));
        var total = await q.CountAsync(ct);
        return (await q.OrderByDescending(a => a.CreatedUtc).ThenByDescending(a => a.Id).Skip(skip).Take(take).ToListAsync(ct), total);
    }
}
