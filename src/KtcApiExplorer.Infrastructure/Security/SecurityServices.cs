using System.Security.Claims;
using System.Security.Cryptography;
using KtcApiExplorer.Application.Abstractions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;

namespace KtcApiExplorer.Infrastructure.Security;

/// <summary>PBKDF2-SHA256, per-user random salt, constant-time comparison. Format: v1.{iterations}.{salt}.{hash}</summary>
public class Pbkdf2PasswordHasher : IPasswordHasher
{
    const int Iterations = 210_000, SaltSize = 16, KeySize = 32;
    public string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
        return $"v1.{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(key)}";
    }
    public bool Verify(string password, string hash)
    {
        var p = hash.Split('.');
        if (p.Length != 4 || p[0] != "v1" || !int.TryParse(p[1], out var it)) return false;
        try
        {
            var salt = Convert.FromBase64String(p[2]); var expected = Convert.FromBase64String(p[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, it, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException) { return false; }
    }
}

public class HttpContextCurrentUser : ICurrentUser
{
    public const string CorrelationItem = "CorrelationId";
    readonly IHttpContextAccessor _ctx; public HttpContextCurrentUser(IHttpContextAccessor ctx) => _ctx = ctx;
    ClaimsPrincipal? P => _ctx.HttpContext?.User;
    public bool IsAuthenticated => P?.Identity?.IsAuthenticated == true;
    public int? UserId => int.TryParse(P?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    public string UserName => P?.Identity?.Name ?? "anonymous";
    public string? Role => P?.FindFirstValue(ClaimTypes.Role);
    public string? IpAddress => _ctx.HttpContext?.Connection.RemoteIpAddress?.ToString();
    public string? CorrelationId => _ctx.HttpContext?.Items[CorrelationItem] as string ?? _ctx.HttpContext?.TraceIdentifier;
}

/// <summary>
/// KTC bearer tokens (and, optionally, the password used for silent renewal) live only in server-side session state, and the blob
/// is additionally encrypted with ASP.NET Core Data Protection - so a leaked cache/Redis dump does not reveal usable tokens.
/// Nothing is ever sent to the browser except an opaque session cookie.
/// </summary>
public class ProtectedSessionKtcStore : IKtcSessionStore
{
    const string Key = "ktc.session.v1";
    readonly IHttpContextAccessor _ctx; readonly IDataProtector _protector;
    public ProtectedSessionKtcStore(IHttpContextAccessor ctx, IDataProtectionProvider dp) { _ctx = ctx; _protector = dp.CreateProtector("KtcApiExplorer.KtcSession.v1"); }

    ISession? S => _ctx.HttpContext?.Session;
    public KtcSession? Get()
    {
        var raw = S?.GetString(Key);
        if (string.IsNullOrEmpty(raw)) return null;
        try { return JsonConvert.DeserializeObject<KtcSession>(_protector.Unprotect(raw)); }
        catch (CryptographicException) { S!.Remove(Key); return null; }   // key ring rotated / tampered
    }
    public void Save(KtcSession session) => S?.SetString(Key, _protector.Protect(JsonConvert.SerializeObject(session)));
    public void Clear() => S?.Remove(Key);
}
