using KtcApiExplorer.Application.Abstractions;
using KtcApiExplorer.Domain.Entities;
using KtcApiExplorer.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace KtcApiExplorer.Application.Services;

public class AuditService : IAuditService
{
    readonly IAuditRepository _repo; readonly IUnitOfWork _uow; readonly ICurrentUser _user; readonly ISensitiveDataMasker _mask; readonly ILogger<AuditService> _log;
    public AuditService(IAuditRepository repo, IUnitOfWork uow, ICurrentUser user, ISensitiveDataMasker mask, ILogger<AuditService> log)
    { _repo = repo; _uow = uow; _user = user; _mask = mask; _log = log; }

    public Task LogAsync(string action, string? entityType = null, string? entityId = null, string? details = null, CancellationToken ct = default) =>
        WriteAsync(_user.UserId, _user.IsAuthenticated ? _user.UserName : "anonymous", action, entityType, entityId, details, ct);

    public Task LogAnonymousAsync(string userName, string action, string? details, CancellationToken ct = default) =>
        WriteAsync(null, userName, action, null, null, details, ct);

    async Task WriteAsync(int? userId, string userName, string action, string? et, string? eid, string? details, CancellationToken ct)
    {
        try
        {
            if (details is { Length: > 3800 }) details = details[..3800] + "…";
            _repo.Add(new AuditLog
            {
                UserId = userId, UserName = Trunc(userName, 100), Action = Trunc(action, 100), EntityType = et, EntityId = eid,
                Details = _mask.MaskText(details), IpAddress = _user.IpAddress, CorrelationId = _user.CorrelationId
            });
            await _uow.SaveChangesAsync(ct);
        }
        catch (Exception ex) { _log.LogError(ex, "Failed to write audit entry {Action}", action); }
    }
    static string Trunc(string s, int n) => s.Length <= n ? s : s[..n];
}
