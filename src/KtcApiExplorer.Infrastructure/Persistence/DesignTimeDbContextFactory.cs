using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace KtcApiExplorer.Infrastructure.Persistence;

/// <summary>Used only by `dotnet ef` so that script generation never starts the web host or touches a database.</summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer("Server=.;Database=KtcApiExplorer;Trusted_Connection=True;TrustServerCertificate=True").Options);
}
