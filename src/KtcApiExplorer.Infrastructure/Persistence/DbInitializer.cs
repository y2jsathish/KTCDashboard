using System.Security.Cryptography;
using KtcApiExplorer.Application.Abstractions;
using KtcApiExplorer.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KtcApiExplorer.Infrastructure.Persistence;

public static class DbInitializer
{
    /// <summary>
    /// Database:Initialize = None (default; run database/*.sql yourself) | EnsureCreated (dev convenience).
    /// Roles and the first admin are always ensured if the tables exist.
    /// </summary>
    public static async Task InitializeAsync(IServiceProvider sp, CancellationToken ct = default)
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var cfg = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var log = scope.ServiceProvider.GetRequiredService<ILogger<AppDbContext>>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        if (string.Equals(cfg["Database:Initialize"], "EnsureCreated", StringComparison.OrdinalIgnoreCase))
            await db.Database.EnsureCreatedAsync(ct);

        foreach (var (name, desc) in new[] { (RoleNames.Admin, "Import collections, manage users, view audit log"), (RoleNames.Developer, "Execute any API, save templates and test cases"), (RoleNames.Viewer, "Browse and execute read-only (GET/HEAD) requests") })
            if (!await db.Roles.AnyAsync(r => r.Name == name, ct)) db.Roles.Add(new Role { Name = name, Description = desc });
        await db.SaveChangesAsync(ct);

        if (!await db.Users.AnyAsync(ct))
        {
            var admin = await db.Roles.FirstAsync(r => r.Name == RoleNames.Admin, ct);
            var pwd = cfg["Seed:AdminPassword"];
            var generated = string.IsNullOrWhiteSpace(pwd);
            if (generated) pwd = Generate();
            db.Users.Add(new AppUser { UserName = cfg["Seed:AdminUserName"] ?? "admin", DisplayName = "Administrator", RoleId = admin.Id, PasswordHash = hasher.Hash(pwd!) });
            await db.SaveChangesAsync(ct);
            log.LogWarning("Created the initial administrator account '{User}'.", cfg["Seed:AdminUserName"] ?? "admin");
            // Printed straight to the console (not through Serilog) so the one-time password never lands in log files.
            if (generated) Console.WriteLine($"\n  >>> Initial admin password (shown once): {pwd}\n");
        }
    }

    static string Generate()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";
        var s = new string(Enumerable.Range(0, 16).Select(_ => chars[RandomNumberGenerator.GetInt32(chars.Length)]).ToArray());
        return s + "aA1";
    }
}
