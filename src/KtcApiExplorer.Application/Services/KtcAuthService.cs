using KtcApiExplorer.Application.Abstractions;
using KtcApiExplorer.Application.Common;
using KtcApiExplorer.Application.Dtos;
using KtcApiExplorer.Application.Options;
using KtcApiExplorer.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace KtcApiExplorer.Application.Services;

public class KtcAuthService : IKtcAuthService
{
    readonly IKtcSessionStore _store; readonly IApiHttpClient _http; readonly ICollectionRepository _cols;
    readonly IAuditService _audit; readonly KtcOptions _opt; readonly ILogger<KtcAuthService> _log;
    static readonly SemaphoreSlim RefreshGate = new(1, 1);

    public KtcAuthService(IKtcSessionStore store, IApiHttpClient http, ICollectionRepository cols, IAuditService audit, IOptions<KtcOptions> opt, ILogger<KtcAuthService> log)
    { _store = store; _http = http; _cols = cols; _audit = audit; _opt = opt.Value; _log = log; }

    public async Task<KtcStatusDto> LoginAsync(KtcLoginRequestDto r, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(r.Username) || string.IsNullOrEmpty(r.Password)) throw new ValidationException("Username and password are required.");
        var col = await _cols.GetAsync(r.CollectionId, ct) ?? throw new NotFoundException("Select a collection (KTC environment) to sign in to.");
        if (!col.IsActive) throw new NotFoundException("That collection is no longer active.");

        var res = await CallLoginAsync(col.BaseUrl, "api/v1/Login", r.Username.Trim(), r.Password, r.AppName, r.AuthMode, r.PluginName, null, ct);
        if (res.Error != null) { await _audit.LogAsync("Ktc.Login.Failed", "ApiCollection", col.Id.ToString(), $"{r.Username}: {res.Error}", ct); throw new AppException("ktc_unreachable", "Could not reach the KTC server: " + res.Error); }
        if (!res.IsSuccess)
        {
            await _audit.LogAsync("Ktc.Login.Failed", "ApiCollection", col.Id.ToString(), $"{r.Username}: HTTP {res.StatusCode}", ct);
            throw new AppException("ktc_login_failed", $"KTC rejected the login (HTTP {res.StatusCode}).");
        }
        var tokens = KtcTokenParser.Parse(res.Body, DateTime.UtcNow)
            ?? throw new AppException("ktc_bad_response", "Login succeeded but no access_token was found in the response.");

        var now = DateTime.UtcNow;
        var session = new KtcSession
        {
            CollectionId = col.Id, BaseUrl = col.BaseUrl, UserName = r.Username.Trim(), AppName = r.AppName, AuthMode = r.AuthMode, PluginName = r.PluginName,
            AccessToken = tokens.AccessToken, KtcAccessToken = tokens.KtcAccessToken, IssuedUtc = now,
            ExpiresUtc = tokens.ExpiresUtc ?? now.AddMinutes(_opt.DefaultTokenLifetimeMinutes),
            Password = _opt.StoreCredentialsForRefresh ? r.Password : null
        };
        _store.Save(session);
        await _audit.LogAsync("Ktc.Login", "ApiCollection", col.Id.ToString(), $"{session.UserName} ({session.AuthMode}/{session.AppName})", ct);
        return ToStatus(session);
    }

    public KtcStatusDto GetStatus() => ToStatus(_store.Get());

    public async Task<KtcStatusDto> RefreshAsync(CancellationToken ct = default)
    {
        var s = _store.Get() ?? throw new KtcLoginRequiredException();
        return ToStatus(await RefreshCoreAsync(s, ct) ?? throw new KtcLoginRequiredException("The KTC session expired and could not be renewed. Please sign in again."));
    }

    public async Task<KtcSession?> GetValidSessionAsync(CancellationToken ct = default)
    {
        var s = _store.Get();
        if (s == null) return null;
        var remaining = (s.ExpiresUtc - DateTime.UtcNow).TotalSeconds;
        if (remaining > _opt.TokenRefreshSkewSeconds) return s;
        var refreshed = await RefreshCoreAsync(s, ct);
        if (refreshed != null) return refreshed;
        return remaining > 0 ? s : null;    // still valid for a few seconds: let the call try
    }

    public async Task LogoutAsync(CancellationToken ct = default)
    {
        var s = _store.Get();
        if (s != null)
        {
            // best effort server-side logoff; never block the local sign-out
            try
            {
                await _http.SendAsync(new ApiHttpRequest
                {
                    Method = "POST", Url = Join(s.BaseUrl, "api/v1/Logoff"), Body = "{}", BodyMode = "raw",
                    Headers = new() { new() { Key = "Authorization", Value = "Bearer " + s.AccessToken }, new() { Key = "Content-Type", Value = "application/json" } }
                }, ct);
            }
            catch (Exception ex) { _log.LogDebug(ex, "KTC logoff failed (ignored)"); }
            await _audit.LogAsync("Ktc.Logout", "ApiCollection", s.CollectionId.ToString(), s.UserName, ct);
        }
        _store.Clear();
    }

    async Task<KtcSession?> RefreshCoreAsync(KtcSession stale, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(stale.Password)) return null; // cannot renew silently
        await RefreshGate.WaitAsync(ct);
        try
        {
            var current = _store.Get() ?? stale;
            if ((current.ExpiresUtc - DateTime.UtcNow).TotalSeconds > _opt.TokenRefreshSkewSeconds && current.AccessToken != stale.AccessToken) return current; // someone else refreshed

            // Relogin ("refresh the auth token of an existing session") first, plain Login as fallback
            var res = await CallLoginAsync(current.BaseUrl, "api/v1/Relogin", current.UserName, current.Password!, current.AppName, current.AuthMode, current.PluginName, current.AccessToken, ct);
            var tokens = res.IsSuccess ? KtcTokenParser.Parse(res.Body, DateTime.UtcNow) : null;
            if (tokens == null)
            {
                res = await CallLoginAsync(current.BaseUrl, "api/v1/Login", current.UserName, current.Password!, current.AppName, current.AuthMode, current.PluginName, null, ct);
                tokens = res.IsSuccess ? KtcTokenParser.Parse(res.Body, DateTime.UtcNow) : null;
            }
            if (tokens == null) { _log.LogWarning("KTC token refresh failed for {User}", current.UserName); return null; }

            var now = DateTime.UtcNow;
            current.AccessToken = tokens.AccessToken; current.KtcAccessToken = tokens.KtcAccessToken ?? current.KtcAccessToken;
            current.IssuedUtc = now; current.ExpiresUtc = tokens.ExpiresUtc ?? now.AddMinutes(_opt.DefaultTokenLifetimeMinutes);
            _store.Save(current);
            _log.LogInformation("KTC token refreshed for {User}", current.UserName);
            return current;
        }
        finally { RefreshGate.Release(); }
    }

    Task<ApiHttpResponse> CallLoginAsync(string baseUrl, string route, string user, string pass, string app, string mode, string? plugin, string? bearer, CancellationToken ct)
    {
        var body = JsonConvert.SerializeObject(new { Username = user, Password = pass, AppName = app, AuthMode = mode, PluginName = string.IsNullOrWhiteSpace(plugin) ? null : plugin });
        var h = new List<KeyValueDto> { new() { Key = "Content-Type", Value = "application/json" }, new() { Key = "Accept", Value = "application/json" } };
        if (bearer != null) h.Add(new() { Key = "Authorization", Value = "Bearer " + bearer });
        return _http.SendAsync(new ApiHttpRequest { Method = "POST", Url = Join(baseUrl, route), Headers = h, Body = body, BodyMode = "raw" }, ct);
    }

    static string Join(string b, string r) => b.TrimEnd('/') + "/" + r;

    KtcStatusDto ToStatus(KtcSession? s)
    {
        if (s == null) return new KtcStatusDto { State = "none" };
        var secs = (int)Math.Max(0, (s.ExpiresUtc - DateTime.UtcNow).TotalSeconds);
        return new KtcStatusDto
        {
            IsLoggedIn = secs > 0 || !string.IsNullOrEmpty(s.Password), UserName = s.UserName, AppName = s.AppName, AuthMode = s.AuthMode, CollectionId = s.CollectionId, BaseUrl = s.BaseUrl,
            ExpiresUtc = s.ExpiresUtc, SecondsRemaining = secs, CanAutoRefresh = !string.IsNullOrEmpty(s.Password),
            State = secs <= 0 ? "expired" : secs <= Math.Max(_opt.TokenRefreshSkewSeconds, 120) ? "warning" : "ok"
        };
    }
}
