namespace KtcApiExplorer.Application.Dtos;

public class KeyValueDto
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
    public string? Description { get; set; }
    public bool Enabled { get; set; } = true;
    public bool Required { get; set; }
}

// ---------- import ----------
public class ImportRequest
{
    public string CollectionJson { get; set; } = "";
    public string? FileName { get; set; }
    public string? HelpHtml { get; set; }
    public string? BaseUrl { get; set; }
    public string? CollectionName { get; set; }
}
public class ImportResultDto
{
    public int CollectionId { get; set; }
    public string CollectionName { get; set; } = "";
    public bool Updated { get; set; }
    public int Folders { get; set; }
    public int Endpoints { get; set; }
    public int Added { get; set; }
    public int Changed { get; set; }
    public int Deactivated { get; set; }
    public int DescriptionsFromHelpDoc { get; set; }
    public List<string> Warnings { get; set; } = new();
}
public class CollectionSummaryDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string BaseUrl { get; set; } = "";
    public int EndpointCount { get; set; }
    public int FolderCount { get; set; }
    public DateTime UpdatedUtc { get; set; }
    public string? SourceFileName { get; set; }
}

// ---------- explorer ----------
public class TreeEndpointDto
{
    public int Id { get; set; }
    public string Method { get; set; } = "";
    public string Route { get; set; } = "";
    public string? Summary { get; set; }
    public bool IsFavorite { get; set; }
}
public class TreeFolderDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public List<TreeEndpointDto> Endpoints { get; set; } = new();
}
public class ExampleDto
{
    public string Name { get; set; } = "";
    public int? Status { get; set; }
    public string? Body { get; set; }
    public string? RequestBody { get; set; }
}
public class EndpointDetailDto
{
    public int Id { get; set; }
    public int CollectionId { get; set; }
    public string Folder { get; set; } = "";
    public string Name { get; set; } = "";
    public string Method { get; set; } = "";
    public string Route { get; set; } = "";
    public string DisplayRoute { get; set; } = "";
    public string BaseUrl { get; set; } = "";
    public string Description { get; set; } = "";
    public List<KeyValueDto> PathParams { get; set; } = new();
    public List<KeyValueDto> Query { get; set; } = new();
    public List<KeyValueDto> Headers { get; set; } = new();
    public string? BodyMode { get; set; }
    public string? Body { get; set; }
    public List<ExampleDto> Examples { get; set; } = new();
    public bool IsFavorite { get; set; }
    public bool IsMonitored { get; set; }
    public bool RequiresBody { get; set; }
}

// ---------- execution ----------
public class ExecuteRequestDto
{
    public int EndpointId { get; set; }
    public Dictionary<string, string> PathParams { get; set; } = new();
    public List<KeyValueDto> Query { get; set; } = new();
    public List<KeyValueDto> Headers { get; set; } = new();
    public string? Body { get; set; }
}
public class ExecuteResultDto
{
    public long? ExecutionId { get; set; }
    public string Method { get; set; } = "";
    public string Url { get; set; } = "";
    public int StatusCode { get; set; }
    public string? ReasonPhrase { get; set; }
    public bool IsSuccess { get; set; }
    public long DurationMs { get; set; }
    public long SizeBytes { get; set; }
    public List<KeyValueDto> Headers { get; set; } = new();
    public string? ContentType { get; set; }
    public string? Body { get; set; }
    public string? MaskedBody { get; set; }
    public bool IsBinary { get; set; }
    public string? BodyBase64 { get; set; }
    public bool Truncated { get; set; }
    public string? Error { get; set; }
    public string? ErrorCode { get; set; }
}

// ---------- KTC auth ----------
public class KtcLoginRequestDto
{
    public int CollectionId { get; set; }
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string AppName { get; set; } = "KTC_UI";
    public string AuthMode { get; set; } = "DOMAIN";
    public string? PluginName { get; set; }
}
public class KtcStatusDto
{
    public bool IsLoggedIn { get; set; }
    public string? UserName { get; set; }
    public string? AppName { get; set; }
    public string? AuthMode { get; set; }
    public int? CollectionId { get; set; }
    public string? BaseUrl { get; set; }
    public DateTime? ExpiresUtc { get; set; }
    public int SecondsRemaining { get; set; }
    /// <summary>ok | warning | expired | none</summary>
    public string State { get; set; } = "none";
    public bool CanAutoRefresh { get; set; }
}

// ---------- history / compare ----------
public class ExecutionListItemDto
{
    public long Id { get; set; }
    public int? EndpointId { get; set; }
    public string Method { get; set; } = "";
    public string Url { get; set; } = "";
    public int? StatusCode { get; set; }
    public bool IsSuccess { get; set; }
    public long DurationMs { get; set; }
    public string Source { get; set; } = "";
    public DateTime ExecutedUtc { get; set; }
}
public class ExecutionDetailDto : ExecutionListItemDto
{
    public string? RequestHeadersJson { get; set; }
    public string? RequestBody { get; set; }
    public string? ResponseHeadersJson { get; set; }
    public string? ResponseBody { get; set; }
    public string? ErrorMessage { get; set; }
}
public class PagedResult<T>
{
    public List<T> Items { get; set; } = new();
    public int Total { get; set; }
}
public class DiffItemDto
{
    public string Path { get; set; } = "";
    public string Type { get; set; } = ""; // Added | Removed | Changed
    public string? Left { get; set; }
    public string? Right { get; set; }
}
public class DiffResultDto
{
    public bool AreEqual { get; set; }
    public bool Truncated { get; set; }
    public List<DiffItemDto> Differences { get; set; } = new();
}

// ---------- library ----------
public class TemplateDto
{
    public int Id { get; set; }
    public int EndpointId { get; set; }
    public string EndpointName { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public bool IsShared { get; set; }
    public bool IsMine { get; set; }
    public ExecuteRequestDto? Request { get; set; }
    public DateTime UpdatedUtc { get; set; }
}
public class SaveTemplateDto
{
    public int EndpointId { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public bool IsShared { get; set; }
    public ExecuteRequestDto Request { get; set; } = new();
}
public class TestCaseDto
{
    public int Id { get; set; }
    public int EndpointId { get; set; }
    public string EndpointName { get; set; } = "";
    public string Name { get; set; } = "";
    public int? ExpectedStatus { get; set; }
    public string? ExpectedBodyContains { get; set; }
    public bool HasBaseline { get; set; }
    public string? LastResult { get; set; }
    public string? LastMessage { get; set; }
    public DateTime? LastRunUtc { get; set; }
    public ExecuteRequestDto? Request { get; set; }
}
public class SaveTestCaseDto
{
    public int EndpointId { get; set; }
    public string Name { get; set; } = "";
    public int? ExpectedStatus { get; set; }
    public string? ExpectedBodyContains { get; set; }
    public ExecuteRequestDto Request { get; set; } = new();
}
public class TestRunResultDto
{
    public int TestCaseId { get; set; }
    public bool Passed { get; set; }
    public string Message { get; set; } = "";
    public ExecuteResultDto? Result { get; set; }
    public DiffResultDto? BaselineDiff { get; set; }
}

// ---------- dashboard / monitoring ----------
public class NameCount { public string Name { get; set; } = ""; public int Count { get; set; } }
public class DashboardDto
{
    public int TotalApis { get; set; }
    public int GetApis { get; set; }
    public int PostApis { get; set; }
    public int PutApis { get; set; }
    public int DeleteApis { get; set; }
    public int OtherApis { get; set; }
    public int Collections { get; set; }
    public List<NameCount> UsageByApi { get; set; } = new();
    public int Success { get; set; }
    public int Failure { get; set; }
    public List<DailyPointDto> Trend { get; set; } = new();
}
public class DailyPointDto { public string Day { get; set; } = ""; public int Total { get; set; } public int Failures { get; set; } public double AvgMs { get; set; } }
public class HealthRowDto
{
    public int EndpointId { get; set; }
    public string Name { get; set; } = "";
    public string Method { get; set; } = "";
    public string Route { get; set; } = "";
    public int? LastStatus { get; set; }
    public bool? LastSuccess { get; set; }
    public long? LastDurationMs { get; set; }
    public DateTime? LastCheckedUtc { get; set; }
    public double AvgMs { get; set; }
    public int Samples { get; set; }
    public int Failures { get; set; }
}
public class MonitoringDto
{
    public List<HealthRowDto> Rows { get; set; } = new();
    public List<DailyPointDto> ErrorTrend { get; set; } = new();
}

// ---------- developer tools / AI ----------
public class CodeGenRequestDto
{
    public ExecuteRequestDto Request { get; set; } = new();
    public string Language { get; set; } = "curl"; // curl | csharp-httpclient | csharp-restsharp | javascript-fetch | csharp-dto
    public string? Json { get; set; }
    public string? RootName { get; set; }
}
public class CodeGenResultDto { public string Language { get; set; } = ""; public string Code { get; set; } = ""; }
public class RequiredFieldDto { public string Name { get; set; } = ""; public string Location { get; set; } = ""; public string Type { get; set; } = ""; public bool Required { get; set; } public string Reason { get; set; } = ""; }
public class AiRequestDto { public int EndpointId { get; set; } public int Count { get; set; } = 3; }
public class AiResultDto { public string Kind { get; set; } = ""; public string Content { get; set; } = ""; public string Provider { get; set; } = ""; public List<RequiredFieldDto>? Fields { get; set; } }

// ---------- accounts ----------
public class UserDto { public int Id { get; set; } public string UserName { get; set; } = ""; public string DisplayName { get; set; } = ""; public string? Email { get; set; } public string Role { get; set; } = ""; public bool IsActive { get; set; } public DateTime? LastLoginUtc { get; set; } }
public class SaveUserDto { public int? Id { get; set; } public string UserName { get; set; } = ""; public string DisplayName { get; set; } = ""; public string? Email { get; set; } public string Role { get; set; } = "Viewer"; public bool IsActive { get; set; } = true; public string? Password { get; set; } }
public class AuditLogDto { public long Id { get; set; } public string UserName { get; set; } = ""; public string Action { get; set; } = ""; public string? EntityType { get; set; } public string? EntityId { get; set; } public string? Details { get; set; } public string? IpAddress { get; set; } public DateTime CreatedUtc { get; set; } }
