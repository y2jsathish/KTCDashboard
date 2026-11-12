using KtcApiExplorer.Application.Abstractions;
using KtcApiExplorer.Application.Ai;
using KtcApiExplorer.Application.Options;
using KtcApiExplorer.Domain.Interfaces;
using KtcApiExplorer.Infrastructure.Ai;
using KtcApiExplorer.Infrastructure.Http;
using KtcApiExplorer.Infrastructure.Persistence;
using KtcApiExplorer.Infrastructure.Repositories;
using KtcApiExplorer.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace KtcApiExplorer.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection s, IConfiguration cfg)
    {
        // ---- persistence: SQL Server (production). Sqlite is supported ONLY for local development / smoke tests. ----
        var provider = cfg["Database:Provider"] ?? "SqlServer";
        var conn = cfg.GetConnectionString("Default") ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");
        s.AddDbContext<AppDbContext>(o =>
        {
            if (provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase)) o.UseSqlite(conn);
            else o.UseSqlServer(conn, sql => sql.EnableRetryOnFailure(3));
        });
        s.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<AppDbContext>());
        s.AddScoped<IUserRepository, UserRepository>();
        s.AddScoped<ICollectionRepository, CollectionRepository>();
        s.AddScoped<IEndpointRepository, EndpointRepository>();
        s.AddScoped<IExecutionRepository, ExecutionRepository>();
        s.AddScoped<IFavoriteRepository, FavoriteRepository>();
        s.AddScoped<ILibraryRepository, LibraryRepository>();
        s.AddScoped<IAuditRepository, AuditRepository>();

        // ---- security / session ----
        s.AddHttpContextAccessor();
        s.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        s.AddScoped<ICurrentUser, HttpContextCurrentUser>();
        s.AddScoped<IKtcSessionStore, ProtectedSessionKtcStore>();

        // ---- outbound HTTP ----
        var ktc = cfg.GetSection(KtcOptions.Section).Get<KtcOptions>() ?? new KtcOptions();
        s.AddHttpClient(KtcHttpGateway.ClientName, c => c.Timeout = TimeSpan.FromSeconds(ktc.RequestTimeoutSeconds))
         .ConfigurePrimaryHttpMessageHandler(() =>
         {
             var h = new HttpClientHandler { AllowAutoRedirect = false, AutomaticDecompression = System.Net.DecompressionMethods.All, UseCookies = false };
             if (ktc.AllowInvalidCertificates) h.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
             return h;
         });
        s.AddScoped<IApiHttpClient, KtcHttpGateway>();

        // ---- AI provider (offline heuristic unless configured) ----
        var ai = cfg.GetSection(AiOptions.Section).Get<AiOptions>() ?? new AiOptions();
        if (ai.Provider.Equals("Anthropic", StringComparison.OrdinalIgnoreCase))
            s.AddScoped<IAiAssistant, AnthropicAiAssistant>();
        else
            s.AddScoped<IAiAssistant>(sp => sp.GetRequiredService<HeuristicAiAssistant>());
        return s;
    }
}
