using KtcApiExplorer.Application.Dtos;

namespace KtcApiExplorer.Application.Abstractions;

public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    int? UserId { get; }
    string UserName { get; }
    string? Role { get; }
    string? IpAddress { get; }
    string? CorrelationId { get; }
}

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string hash);
}

public interface IAuditService
{
    Task LogAsync(string action, string? entityType = null, string? entityId = null, string? details = null, CancellationToken ct = default);
    /// <summary>For events where there is no authenticated principal yet (failed logins).</summary>
    Task LogAnonymousAsync(string userName, string action, string? details, CancellationToken ct = default);
}

/// <summary>Server-side holder of the KTC bearer tokens for the current browser session. Implementations must encrypt at rest.</summary>
public interface IKtcSessionStore
{
    KtcSession? Get();
    void Save(KtcSession session);
    void Clear();
}

public class KtcSession
{
    public int CollectionId { get; set; }
    public string BaseUrl { get; set; } = "";
    public string UserName { get; set; } = "";
    public string AppName { get; set; } = "";
    public string AuthMode { get; set; } = "";
    public string? PluginName { get; set; }
    public string AccessToken { get; set; } = "";
    public string? KtcAccessToken { get; set; }
    public DateTime IssuedUtc { get; set; }
    public DateTime ExpiresUtc { get; set; }
    /// <summary>Only populated when Ktc:StoreCredentialsForRefresh is true.</summary>
    public string? Password { get; set; }
}

public class ApiHttpRequest
{
    public string Method { get; set; } = "GET";
    public string Url { get; set; } = "";
    public List<KeyValueDto> Headers { get; set; } = new();
    public string? Body { get; set; }
    /// <summary>raw | urlencoded | formdata | null</summary>
    public string? BodyMode { get; set; }
}
public class ApiHttpResponse
{
    public int StatusCode { get; set; }
    public string? ReasonPhrase { get; set; }
    public List<KeyValueDto> Headers { get; set; } = new();
    public string? ContentType { get; set; }
    public string? Body { get; set; }
    public bool IsBinary { get; set; }
    public string? BodyBase64 { get; set; }
    public long SizeBytes { get; set; }
    public bool Truncated { get; set; }
    public long DurationMs { get; set; }
    public string? Error { get; set; }
    public bool IsSuccess => Error == null && StatusCode is >= 200 and <= 299;
}
/// <summary>Sends the outbound call to the KTC server. Never throws for network/HTTP failures.</summary>
public interface IApiHttpClient
{
    Task<ApiHttpResponse> SendAsync(ApiHttpRequest request, CancellationToken ct = default);
}

public interface ISensitiveDataMasker
{
    string MaskJson(string? json);
    string? MaskText(string? text);
    List<KeyValueDto> MaskHeaders(IEnumerable<KeyValueDto> headers);
}

public interface IPostmanImportService { Task<ImportResultDto> ImportAsync(ImportRequest request, CancellationToken ct = default); }
public interface IExplorerService
{
    Task<List<CollectionSummaryDto>> GetCollectionsAsync(CancellationToken ct = default);
    Task<List<TreeFolderDto>> GetTreeAsync(int collectionId, CancellationToken ct = default);
    Task<EndpointDetailDto> GetEndpointAsync(int endpointId, CancellationToken ct = default);
    Task DeleteCollectionAsync(int collectionId, CancellationToken ct = default);
    Task UpdateBaseUrlAsync(int collectionId, string baseUrl, CancellationToken ct = default);
}
public interface IKtcAuthService
{
    Task<KtcStatusDto> LoginAsync(KtcLoginRequestDto request, CancellationToken ct = default);
    KtcStatusDto GetStatus();
    Task<KtcStatusDto> RefreshAsync(CancellationToken ct = default);
    Task LogoutAsync(CancellationToken ct = default);
    /// <summary>Returns the live session, silently refreshing it when close to expiry. Null when not logged in / expired.</summary>
    Task<KtcSession?> GetValidSessionAsync(CancellationToken ct = default);
}
public interface IApiExecutionService
{
    Task<ExecuteResultDto> ExecuteAsync(ExecuteRequestDto request, string source = "Manual", CancellationToken ct = default);
    Task<PagedResult<ExecutionListItemDto>> GetHistoryAsync(int? endpointId, bool? success, int skip, int take, CancellationToken ct = default);
    Task<ExecutionDetailDto> GetExecutionAsync(long id, CancellationToken ct = default);
    Task<DiffResultDto> CompareExecutionsAsync(long leftId, long rightId, CancellationToken ct = default);
}
public interface ICodeGenerationService { Task<CodeGenResultDto> GenerateAsync(CodeGenRequestDto request, CancellationToken ct = default); }
public interface IDashboardService
{
    Task<DashboardDto> GetDashboardAsync(CancellationToken ct = default);
    Task<MonitoringDto> GetMonitoringAsync(CancellationToken ct = default);
    Task<List<HealthRowDto>> RunHealthChecksAsync(CancellationToken ct = default);
    Task<bool> ToggleMonitoredAsync(int endpointId, CancellationToken ct = default);
}
public interface ILibraryService
{
    Task<bool> ToggleFavoriteAsync(int endpointId, CancellationToken ct = default);
    Task<List<TreeEndpointDto>> GetFavoritesAsync(CancellationToken ct = default);
    Task<List<TreeEndpointDto>> GetRecentAsync(int take, CancellationToken ct = default);
    Task<List<TemplateDto>> GetTemplatesAsync(int? endpointId, CancellationToken ct = default);
    Task<TemplateDto> SaveTemplateAsync(SaveTemplateDto dto, CancellationToken ct = default);
    Task DeleteTemplateAsync(int id, CancellationToken ct = default);
    Task<List<TestCaseDto>> GetTestCasesAsync(int? endpointId, CancellationToken ct = default);
    Task<TestCaseDto> SaveTestCaseAsync(SaveTestCaseDto dto, CancellationToken ct = default);
    Task DeleteTestCaseAsync(int id, CancellationToken ct = default);
    Task<TestRunResultDto> RunTestCaseAsync(int id, bool saveAsBaseline, CancellationToken ct = default);
}
public interface IAccountService
{
    Task<(UserDto? User, string? Error)> ValidateAsync(string userName, string password, CancellationToken ct = default);
    Task<List<UserDto>> GetUsersAsync(CancellationToken ct = default);
    Task<List<string>> GetRolesAsync(CancellationToken ct = default);
    Task<UserDto> SaveUserAsync(SaveUserDto dto, CancellationToken ct = default);
    Task<PagedResult<AuditLogDto>> GetAuditAsync(string? action, string? user, int skip, int take, CancellationToken ct = default);
}
public interface IAiAssistant
{
    string ProviderName { get; }
    Task<AiResultDto> ExplainAsync(int endpointId, CancellationToken ct = default);
    Task<AiResultDto> GenerateRequestBodyAsync(int endpointId, CancellationToken ct = default);
    Task<AiResultDto> GenerateTestDataAsync(int endpointId, int count, CancellationToken ct = default);
    Task<AiResultDto> IdentifyRequiredFieldsAsync(int endpointId, CancellationToken ct = default);
    Task<AiResultDto> GenerateDocumentationAsync(int endpointId, CancellationToken ct = default);
}
