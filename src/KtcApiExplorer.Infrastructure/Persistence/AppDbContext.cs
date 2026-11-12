using KtcApiExplorer.Domain.Entities;
using KtcApiExplorer.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace KtcApiExplorer.Infrastructure.Persistence;

public class AppDbContext : DbContext, IUnitOfWork
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Role> Roles => Set<Role>();
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<ApiCollection> ApiCollections => Set<ApiCollection>();
    public DbSet<ApiFolder> ApiFolders => Set<ApiFolder>();
    public DbSet<ApiEndpoint> ApiEndpoints => Set<ApiEndpoint>();
    public DbSet<ApiExecution> ApiExecutions => Set<ApiExecution>();
    public DbSet<ApiFavorite> ApiFavorites => Set<ApiFavorite>();
    public DbSet<RequestTemplate> RequestTemplates => Set<RequestTemplate>();
    public DbSet<TestCase> TestCases => Set<TestCase>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Role>(e =>
        {
            e.ToTable("Roles");
            e.Property(x => x.Name).HasMaxLength(50).IsRequired();
            e.Property(x => x.Description).HasMaxLength(250);
            e.HasIndex(x => x.Name).IsUnique();
        });

        b.Entity<AppUser>(e =>
        {
            e.ToTable("Users");
            e.Property(x => x.UserName).HasMaxLength(100).IsRequired();
            e.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
            e.Property(x => x.Email).HasMaxLength(256);
            e.Property(x => x.PasswordHash).HasMaxLength(500).IsRequired();
            e.HasIndex(x => x.UserName).IsUnique();
            e.HasOne(x => x.Role).WithMany(r => r.Users).HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<ApiCollection>(e =>
        {
            e.ToTable("ApiCollections");
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.BaseUrl).HasMaxLength(500).IsRequired();
            e.Property(x => x.SchemaVersion).HasMaxLength(300);
            e.Property(x => x.SourceFileName).HasMaxLength(260);
            e.HasIndex(x => x.Name).IsUnique();
        });

        b.Entity<ApiFolder>(e =>
        {
            e.ToTable("ApiFolders");
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.HasOne(x => x.Collection).WithMany(c => c.Folders).HasForeignKey(x => x.CollectionId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.ParentFolder).WithMany().HasForeignKey(x => x.ParentFolderId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.CollectionId, x.ParentFolderId, x.Name });
        });

        b.Entity<ApiEndpoint>(e =>
        {
            e.ToTable("ApiEndpoints");
            e.Property(x => x.Name).HasMaxLength(600).IsRequired();
            e.Property(x => x.Method).HasMaxLength(10).IsRequired();
            e.Property(x => x.RouteTemplate).HasMaxLength(300).IsRequired();
            e.Property(x => x.QuerySignature).HasMaxLength(200).IsRequired();
            e.Property(x => x.BodyMode).HasMaxLength(20);
            e.Property(x => x.BodyLanguage).HasMaxLength(20);
            e.HasOne(x => x.Collection).WithMany(c => c.Endpoints).HasForeignKey(x => x.CollectionId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Folder).WithMany().HasForeignKey(x => x.FolderId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.CollectionId, x.Method, x.RouteTemplate, x.QuerySignature }).IsUnique();
            e.HasIndex(x => x.FolderId);
        });

        b.Entity<ApiExecution>(e =>
        {
            e.ToTable("ApiExecutions");
            e.Property(x => x.Source).HasMaxLength(20).IsRequired();
            e.Property(x => x.Method).HasMaxLength(10).IsRequired();
            e.Property(x => x.Url).HasMaxLength(2000).IsRequired();
            e.Property(x => x.ErrorMessage).HasMaxLength(1000);
            e.HasOne(x => x.Endpoint).WithMany().HasForeignKey(x => x.EndpointId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => new { x.EndpointId, x.ExecutedUtc });
            e.HasIndex(x => x.ExecutedUtc);
            e.HasIndex(x => new { x.UserId, x.ExecutedUtc });
        });

        b.Entity<ApiFavorite>(e =>
        {
            e.ToTable("ApiFavorites");
            e.HasOne(x => x.Endpoint).WithMany().HasForeignKey(x => x.EndpointId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.UserId, x.EndpointId }).IsUnique();
        });

        b.Entity<RequestTemplate>(e =>
        {
            e.ToTable("RequestTemplates");
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Description).HasMaxLength(1000);
            e.HasOne(x => x.Endpoint).WithMany().HasForeignKey(x => x.EndpointId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.UserId, x.EndpointId });
        });

        b.Entity<TestCase>(e =>
        {
            e.ToTable("TestCases");
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.ExpectedBodyContains).HasMaxLength(1000);
            e.Property(x => x.LastResult).HasMaxLength(10);
            e.Property(x => x.LastMessage).HasMaxLength(1000);
            e.HasOne(x => x.Endpoint).WithMany().HasForeignKey(x => x.EndpointId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.UserId, x.EndpointId });
        });

        b.Entity<AuditLog>(e =>
        {
            e.ToTable("AuditLogs");
            e.Property(x => x.UserName).HasMaxLength(100).IsRequired();
            e.Property(x => x.Action).HasMaxLength(100).IsRequired();
            e.Property(x => x.EntityType).HasMaxLength(100);
            e.Property(x => x.EntityId).HasMaxLength(100);
            e.Property(x => x.Details).HasMaxLength(4000);
            e.Property(x => x.IpAddress).HasMaxLength(64);
            e.Property(x => x.CorrelationId).HasMaxLength(64);
            e.HasIndex(x => x.CreatedUtc);
            e.HasIndex(x => x.UserName);
        });
    }
}
