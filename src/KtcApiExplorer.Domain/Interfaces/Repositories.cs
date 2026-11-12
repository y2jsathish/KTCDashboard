using KtcApiExplorer.Domain.Entities;
using KtcApiExplorer.Domain.Models;

namespace KtcApiExplorer.Domain.Interfaces;

public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

public interface IUserRepository
{
    Task<AppUser?> GetByUserNameAsync(string userName, CancellationToken ct = default);
    Task<AppUser?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<List<AppUser>> ListAsync(CancellationToken ct = default);
    Task<List<Role>> ListRolesAsync(CancellationToken ct = default);
    Task<Role?> GetRoleByNameAsync(string name, CancellationToken ct = default);
    Task<int> CountAsync(CancellationToken ct = default);
    void Add(AppUser user);
    void AddRole(Role role);
}

public interface ICollectionRepository
{
    Task<List<ApiCollection>> ListAsync(CancellationToken ct = default);
    Task<ApiCollection?> GetAsync(int id, CancellationToken ct = default);
    Task<ApiCollection?> GetByNameAsync(string name, CancellationToken ct = default);
    void Add(ApiCollection collection);
    void Remove(ApiCollection collection);

    Task<List<ApiFolder>> GetFoldersAsync(int collectionId, CancellationToken ct = default);
    Task<List<ApiEndpoint>> GetEndpointsAsync(int collectionId, bool includeInactive = false, CancellationToken ct = default);
    void AddFolder(ApiFolder folder);
    void AddEndpoint(ApiEndpoint endpoint);
}

public interface IEndpointRepository
{
    Task<ApiEndpoint?> GetAsync(int id, CancellationToken ct = default);
    Task<ApiEndpoint?> GetWithCollectionAsync(int id, CancellationToken ct = default);
    Task<List<ApiEndpoint>> GetMonitoredAsync(CancellationToken ct = default);
    Task<List<ApiEndpoint>> GetByIdsAsync(IEnumerable<int> ids, CancellationToken ct = default);
    Task<List<MethodCount>> CountByMethodAsync(CancellationToken ct = default);
    Task<int> CountAsync(CancellationToken ct = default);
}

public interface IExecutionRepository
{
    void Add(ApiExecution execution);
    Task<ApiExecution?> GetAsync(long id, CancellationToken ct = default);
    Task<(List<ApiExecution> Items, int Total)> SearchAsync(ExecutionFilter filter, CancellationToken ct = default);
    Task<List<ApiExecution>> GetLatestForEndpointAsync(int endpointId, int take, CancellationToken ct = default);
    Task<List<UsageByEndpoint>> TopUsedAsync(DateTime sinceUtc, int take, CancellationToken ct = default);
    Task<(int Success, int Failure)> SuccessFailureAsync(DateTime sinceUtc, CancellationToken ct = default);
    Task<List<DailyStat>> DailyStatsAsync(DateTime sinceUtc, string? source, CancellationToken ct = default);
    Task<List<EndpointHealth>> HealthAsync(DateTime sinceUtc, CancellationToken ct = default);
    Task<List<int>> RecentEndpointIdsAsync(int userId, int take, CancellationToken ct = default);
}

public interface IFavoriteRepository
{
    Task<List<int>> GetEndpointIdsAsync(int userId, CancellationToken ct = default);
    Task<bool> ToggleAsync(int userId, int endpointId, CancellationToken ct = default); // returns new state
}

public interface ILibraryRepository
{
    Task<List<RequestTemplate>> ListTemplatesAsync(int userId, int? endpointId, CancellationToken ct = default);
    Task<RequestTemplate?> GetTemplateAsync(int id, CancellationToken ct = default);
    void Add(RequestTemplate t);
    void Remove(RequestTemplate t);
    Task<List<TestCase>> ListTestCasesAsync(int userId, int? endpointId, CancellationToken ct = default);
    Task<TestCase?> GetTestCaseAsync(int id, CancellationToken ct = default);
    void Add(TestCase t);
    void Remove(TestCase t);
}

public interface IAuditRepository
{
    void Add(AuditLog log);
    Task<(List<AuditLog> Items, int Total)> SearchAsync(string? action, string? user, int skip, int take, CancellationToken ct = default);
}
