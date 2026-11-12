namespace KtcApiExplorer.Domain.Entities;

/// <summary>Application roles. Names are used by the authorization policies.</summary>
public static class RoleNames
{
    public const string Admin = "Admin";         // import, user management, audit, everything below
    public const string Developer = "Developer"; // execute any method, templates, test cases
    public const string Viewer = "Viewer";       // browse + execute safe methods (GET/HEAD) only
}

public class Role
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public ICollection<AppUser> Users { get; set; } = new List<AppUser>();
}

/// <summary>Application user (NOT a KTC user - KTC credentials are never persisted).</summary>
public class AppUser
{
    public int Id { get; set; }
    public string UserName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? Email { get; set; }
    public string PasswordHash { get; set; } = "";
    public int RoleId { get; set; }
    public Role? Role { get; set; }
    public bool IsActive { get; set; } = true;
    public int FailedLoginCount { get; set; }
    public DateTime? LockoutEndUtc { get; set; }
    public DateTime? LastLoginUtc { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

public class ApiCollection
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    /// <summary>Root URL of the live KTC REST API, e.g. https://server/KTCRESTAPI. Postman exports may have none.</summary>
    public string BaseUrl { get; set; } = "";
    public string? SchemaVersion { get; set; }
    public string? SourceFileName { get; set; }
    public string? VariablesJson { get; set; }
    public bool IsActive { get; set; } = true;
    public int? ImportedByUserId { get; set; }
    public DateTime ImportedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    public ICollection<ApiFolder> Folders { get; set; } = new List<ApiFolder>();
    public ICollection<ApiEndpoint> Endpoints { get; set; } = new List<ApiEndpoint>();
}

public class ApiFolder
{
    public int Id { get; set; }
    public int CollectionId { get; set; }
    public ApiCollection? Collection { get; set; }
    public int? ParentFolderId { get; set; }
    public ApiFolder? ParentFolder { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public int SortOrder { get; set; }
}

public class ApiEndpoint
{
    public int Id { get; set; }
    public int CollectionId { get; set; }
    public ApiCollection? Collection { get; set; }
    public int? FolderId { get; set; }
    public ApiFolder? Folder { get; set; }
    public string Name { get; set; } = "";
    public string Method { get; set; } = "GET";
    /// <summary>Route without query string, e.g. api/v1/Action/{id}</summary>
    public string RouteTemplate { get; set; } = "";
    /// <summary>
    /// Sorted, lower-cased query parameter names joined by '&amp;'. KTC exposes overloads that differ only by query string
    /// (e.g. GET api/v1/Campaign?name={name} vs ?externalID={externalID}), so (Method, RouteTemplate, QuerySignature) is the identity.
    /// </summary>
    public string QuerySignature { get; set; } = "";
    public string? Description { get; set; }
    /// <summary>JSON array of {key,value,description,disabled}</summary>
    public string? HeadersJson { get; set; }
    public string? QueryParamsJson { get; set; }
    public string? PathParamsJson { get; set; }
    public string? BodyMode { get; set; }
    public string? BodyRaw { get; set; }
    public string? BodyLanguage { get; set; }
    /// <summary>Postman saved responses (examples) as JSON array.</summary>
    public string? ExamplesJson { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsMonitored { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}

public static class ExecutionSources
{
    public const string Manual = "Manual";
    public const string HealthCheck = "HealthCheck";
    public const string TestCase = "TestCase";
}

public class ApiExecution
{
    public long Id { get; set; }
    public int? EndpointId { get; set; }
    public ApiEndpoint? Endpoint { get; set; }
    public int? UserId { get; set; }
    public string Source { get; set; } = ExecutionSources.Manual;
    public string Method { get; set; } = "";
    public string Url { get; set; } = "";
    /// <summary>Always stored masked.</summary>
    public string? RequestHeadersJson { get; set; }
    public string? RequestBody { get; set; }
    public int? StatusCode { get; set; }
    public long DurationMs { get; set; }
    public long ResponseSizeBytes { get; set; }
    public string? ResponseHeadersJson { get; set; }
    /// <summary>Masked + truncated copy of the response (configurable).</summary>
    public string? ResponseBody { get; set; }
    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime ExecutedUtc { get; set; } = DateTime.UtcNow;
}

public class ApiFavorite
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int EndpointId { get; set; }
    public ApiEndpoint? Endpoint { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

public class RequestTemplate
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int EndpointId { get; set; }
    public ApiEndpoint? Endpoint { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    /// <summary>Serialized ExecuteRequest (path/query/headers/body). Auth headers are never saved.</summary>
    public string RequestJson { get; set; } = "{}";
    public bool IsShared { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}

public class TestCase
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int EndpointId { get; set; }
    public ApiEndpoint? Endpoint { get; set; }
    public string Name { get; set; } = "";
    public string RequestJson { get; set; } = "{}";
    public int? ExpectedStatus { get; set; }
    public string? ExpectedBodyContains { get; set; }
    /// <summary>Masked response captured as baseline for "compare responses".</summary>
    public string? BaselineResponse { get; set; }
    public string? LastResult { get; set; } // Passed | Failed
    public string? LastMessage { get; set; }
    public DateTime? LastRunUtc { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

public class AuditLog
{
    public long Id { get; set; }
    public int? UserId { get; set; }
    public string UserName { get; set; } = "";
    public string Action { get; set; } = "";
    public string? EntityType { get; set; }
    public string? EntityId { get; set; }
    public string? Details { get; set; }
    public string? IpAddress { get; set; }
    public string? CorrelationId { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}
