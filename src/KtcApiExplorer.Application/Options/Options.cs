namespace KtcApiExplorer.Application.Options;

public class KtcOptions
{
    public const string Section = "Ktc";
    /// <summary>Pre-filled base URL used when a collection has none (e.g. https://server/KTCRESTAPI).</summary>
    public string DefaultBaseUrl { get; set; } = "https://localhost/KTCRESTAPI";
    public int RequestTimeoutSeconds { get; set; } = 60;
    /// <summary>Refresh the token when fewer than this many seconds remain.</summary>
    public int TokenRefreshSkewSeconds { get; set; } = 90;
    /// <summary>Used when the login response carries no expiry information.</summary>
    public int DefaultTokenLifetimeMinutes { get; set; } = 20;
    /// <summary>Keep the (encrypted, server-side) KTC password in the session so the token can be renewed silently.</summary>
    public bool StoreCredentialsForRefresh { get; set; } = true;
    /// <summary>DEV ONLY: accept self-signed TLS certificates from the KTC server.</summary>
    public bool AllowInvalidCertificates { get; set; } = false;
    public int SessionIdleMinutes { get; set; } = 60;
}

public class ExecutionOptions
{
    public const string Section = "Execution";
    public int MaxResponseBytes { get; set; } = 5 * 1024 * 1024;
    public bool StoreResponseBody { get; set; } = true;
    public int MaxStoredBodyChars { get; set; } = 32_000;
}

public class AiOptions
{
    public const string Section = "Ai";
    /// <summary>Heuristic (offline, default) or Anthropic (sends endpoint metadata + sample payload to the Claude API).</summary>
    public string Provider { get; set; } = "Heuristic";
    public string Model { get; set; } = "claude-sonnet-5-5";
    public string? ApiKey { get; set; }
}
