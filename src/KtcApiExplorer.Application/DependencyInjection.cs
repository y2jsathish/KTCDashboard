using KtcApiExplorer.Application.Abstractions;
using KtcApiExplorer.Application.Ai;
using KtcApiExplorer.Application.Options;
using KtcApiExplorer.Application.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace KtcApiExplorer.Application;

public static class DependencyInjection
{
    /// <summary>Registers business-layer services (no framework or database specifics).</summary>
    public static IServiceCollection AddApplication(this IServiceCollection s, IConfiguration cfg)
    {
        s.Configure<KtcOptions>(cfg.GetSection(KtcOptions.Section));
        s.Configure<ExecutionOptions>(cfg.GetSection(ExecutionOptions.Section));
        s.Configure<AiOptions>(cfg.GetSection(AiOptions.Section));

        s.AddSingleton<ISensitiveDataMasker, SensitiveDataMasker>();
        s.AddScoped<IAuditService, AuditService>();
        s.AddScoped<IAccountService, AccountService>();
        s.AddScoped<IPostmanImportService, PostmanImportService>();
        s.AddScoped<IExplorerService, ExplorerService>();
        s.AddScoped<IKtcAuthService, KtcAuthService>();
        s.AddScoped<IApiExecutionService, ApiExecutionService>();
        s.AddScoped<ICodeGenerationService, CodeGenerationService>();
        s.AddScoped<IDashboardService, DashboardService>();
        s.AddScoped<ILibraryService, LibraryService>();
        s.AddScoped<HeuristicAiAssistant>();
        return s;
    }
}
