using KtcApiExplorer.Application.Abstractions;
using KtcApiExplorer.Application.Common;
using KtcApiExplorer.Application.Dtos;
using KtcApiExplorer.Domain.Entities;
using KtcApiExplorer.Domain.Interfaces;

namespace KtcApiExplorer.Application.Services;

public class AccountService : IAccountService
{
    const int MaxFailures = 5; static readonly TimeSpan LockoutFor = TimeSpan.FromMinutes(15);
    readonly IUserRepository _users; readonly IAuditRepository _auditRepo; readonly IPasswordHasher _hasher; readonly IUnitOfWork _uow; readonly IAuditService _audit;
    static string? _dummyHash;

    public AccountService(IUserRepository users, IAuditRepository auditRepo, IPasswordHasher hasher, IUnitOfWork uow, IAuditService audit)
    { _users = users; _auditRepo = auditRepo; _hasher = hasher; _uow = uow; _audit = audit; }

    public async Task<(UserDto? User, string? Error)> ValidateAsync(string userName, string password, CancellationToken ct = default)
    {
        var u = await _users.GetByUserNameAsync(userName.Trim(), ct);
        if (u == null)
        {
            _hasher.Verify(password, _dummyHash ??= _hasher.Hash("dummy-password-for-timing")); // equalise timing: don't reveal valid usernames
            await _audit.LogAnonymousAsync(userName, "Login.Failed", "Unknown user", ct);
            return (null, "Invalid user name or password.");
        }
        if (u.LockoutEndUtc > DateTime.UtcNow)
        {
            await _audit.LogAnonymousAsync(u.UserName, "Login.Locked", null, ct);
            return (null, "This account is temporarily locked. Try again later.");
        }
        if (!u.IsActive || !_hasher.Verify(password, u.PasswordHash))
        {
            u.FailedLoginCount++;
            if (u.FailedLoginCount >= MaxFailures) { u.LockoutEndUtc = DateTime.UtcNow.Add(LockoutFor); u.FailedLoginCount = 0; }
            await _uow.SaveChangesAsync(ct);
            await _audit.LogAnonymousAsync(u.UserName, "Login.Failed", u.IsActive ? "Bad password" : "Inactive account", ct);
            return (null, "Invalid user name or password.");
        }
        u.FailedLoginCount = 0; u.LockoutEndUtc = null; u.LastLoginUtc = DateTime.UtcNow;
        await _uow.SaveChangesAsync(ct);
        return (Map(u), null);
    }

    public async Task<List<UserDto>> GetUsersAsync(CancellationToken ct = default) => (await _users.ListAsync(ct)).Select(Map).ToList();
    public async Task<List<string>> GetRolesAsync(CancellationToken ct = default) => (await _users.ListRolesAsync(ct)).Select(r => r.Name).ToList();

    public async Task<UserDto> SaveUserAsync(SaveUserDto dto, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(dto.UserName) || dto.UserName.Length > 100) throw new ValidationException("User name is required (max 100 characters).");
        if (string.IsNullOrWhiteSpace(dto.DisplayName)) throw new ValidationException("Display name is required.");
        var role = await _users.GetRoleByNameAsync(dto.Role, ct) ?? throw new ValidationException("Unknown role.");
        if (!string.IsNullOrEmpty(dto.Password)) CheckPassword(dto.Password);

        AppUser? u;
        if (dto.Id is int id && id > 0)
        {
            u = await _users.GetByIdAsync(id, ct) ?? throw new NotFoundException("User not found.");
            if ((u.Role?.Name == RoleNames.Admin && u.IsActive) && (role.Name != RoleNames.Admin || !dto.IsActive))
                if (!(await _users.ListAsync(ct)).Any(x => x.Id != u.Id && x.IsActive && x.Role?.Name == RoleNames.Admin))
                    throw new ValidationException("At least one active Admin must remain.");
        }
        else
        {
            if (await _users.GetByUserNameAsync(dto.UserName.Trim(), ct) != null) throw new ValidationException("That user name is already taken.");
            if (string.IsNullOrEmpty(dto.Password)) throw new ValidationException("A password is required for new users.");
            u = new AppUser { UserName = dto.UserName.Trim() };
            _users.Add(u);
        }
        u.DisplayName = dto.DisplayName.Trim(); u.Email = string.IsNullOrWhiteSpace(dto.Email) ? null : dto.Email.Trim();
        u.RoleId = role.Id; u.Role = role; u.IsActive = dto.IsActive;
        if (!string.IsNullOrEmpty(dto.Password)) { u.PasswordHash = _hasher.Hash(dto.Password); u.FailedLoginCount = 0; u.LockoutEndUtc = null; }
        await _uow.SaveChangesAsync(ct);
        await _audit.LogAsync(dto.Id > 0 ? "User.Update" : "User.Create", "User", u.Id.ToString(), $"{u.UserName} role={role.Name} active={u.IsActive}{(string.IsNullOrEmpty(dto.Password) ? "" : " password-changed")}", ct);
        return Map(u);
    }

    public async Task<PagedResult<AuditLogDto>> GetAuditAsync(string? action, string? user, int skip, int take, CancellationToken ct = default)
    {
        var (items, total) = await _auditRepo.SearchAsync(action, user, skip, Math.Clamp(take, 1, 200), ct);
        return new PagedResult<AuditLogDto>
        {
            Total = total,
            Items = items.Select(a => new AuditLogDto { Id = a.Id, UserName = a.UserName, Action = a.Action, EntityType = a.EntityType, EntityId = a.EntityId, Details = a.Details, IpAddress = a.IpAddress, CreatedUtc = a.CreatedUtc }).ToList()
        };
    }

    public static void CheckPassword(string p)
    {
        if (p.Length < 10 || !p.Any(char.IsUpper) || !p.Any(char.IsLower) || !p.Any(char.IsDigit))
            throw new ValidationException("Password must be at least 10 characters and contain upper-case, lower-case and a digit.");
    }
    static UserDto Map(AppUser u) => new() { Id = u.Id, UserName = u.UserName, DisplayName = u.DisplayName, Email = u.Email, Role = u.Role?.Name ?? "", IsActive = u.IsActive, LastLoginUtc = u.LastLoginUtc };
}
