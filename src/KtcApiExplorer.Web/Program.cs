using System.Security.Claims;
using System.Threading.RateLimiting;
using KtcApiExplorer.Application;
using KtcApiExplorer.Application.Options;
using KtcApiExplorer.Infrastructure;
using KtcApiExplorer.Infrastructure.Persistence;
using KtcApiExplorer.Web.Middleware;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Serilog;

Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateBootstrapLogger();
try
{
    var builder = WebApplication.CreateBuilder(args);
    builder.Host.UseSerilog((ctx, sp, cfg) => cfg.ReadFrom.Configuration(ctx.Configuration).ReadFrom.Services(sp).Enrich.FromLogContext());

    // ---------- layers ----------
    builder.Services.AddApplication(builder.Configuration);
    builder.Services.AddInfrastructure(builder.Configuration);

    // ---------- MVC / JSON ----------
    builder.Services.AddControllersWithViews(o => o.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()))
        .AddNewtonsoftJson(o =>
        {
            o.SerializerSettings.ContractResolver = new DefaultContractResolver { NamingStrategy = new CamelCaseNamingStrategy { ProcessDictionaryKeys = false } };
            o.SerializerSettings.NullValueHandling = NullValueHandling.Ignore;
        });
    builder.Services.AddAntiforgery(o => { o.HeaderName = "RequestVerificationToken"; o.Cookie.Name = ".KtcExplorer.Csrf"; o.Cookie.SameSite = SameSiteMode.Strict; });

    // ---------- authn / authz ----------
    builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
        .AddCookie(o =>
        {
            o.Cookie.Name = ".KtcExplorer.Auth"; o.Cookie.HttpOnly = true; o.Cookie.SameSite = SameSiteMode.Lax;
            o.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
            o.ExpireTimeSpan = TimeSpan.FromHours(8); o.SlidingExpiration = true;
            o.LoginPath = "/Account/Login"; o.AccessDeniedPath = "/Account/Denied";
            o.Events.OnRedirectToLogin = c => IsApi(c.Request) ? Status(c.Response, 401) : Redirect(c);
            o.Events.OnRedirectToAccessDenied = c => IsApi(c.Request) ? Status(c.Response, 403) : Redirect(c);
        });
    builder.Services.AddAuthorization(o =>
    {
        o.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
        o.AddPolicy("Admin", p => p.RequireRole("Admin"));
        o.AddPolicy("Developer", p => p.RequireRole("Admin", "Developer"));
    });

    // ---------- session (server-side holder of KTC tokens) ----------
    var ktc = builder.Configuration.GetSection(KtcOptions.Section).Get<KtcOptions>() ?? new KtcOptions();
    builder.Services.AddDistributedMemoryCache();   // single instance. Scale-out: AddStackExchangeRedisCache / AddDistributedSqlServerCache.
    builder.Services.AddSession(o =>
    {
        o.Cookie.Name = ".KtcExplorer.Session"; o.Cookie.HttpOnly = true; o.Cookie.IsEssential = true; o.Cookie.SameSite = SameSiteMode.Strict;
        o.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        o.IdleTimeout = TimeSpan.FromMinutes(ktc.SessionIdleMinutes);
    });
    var dp = builder.Services.AddDataProtection().SetApplicationName("KtcApiExplorer");
    if (builder.Configuration["DataProtection:KeysPath"] is { Length: > 0 } keys) dp.PersistKeysToFileSystem(new DirectoryInfo(keys));

    builder.Services.AddRateLimiter(o =>
    {
        o.RejectionStatusCode = 429;
        o.AddPolicy("login", ctx => RateLimitPartition.GetFixedWindowLimiter(ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    });
    builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = 30 * 1024 * 1024);

    var app = builder.Build();
    await DbInitializer.InitializeAsync(app.Services);

    // ---------- pipeline ----------
    if (!app.Environment.IsDevelopment()) { app.UseHsts(); app.UseHttpsRedirection(); }
    app.UseMiddleware<CorrelationIdMiddleware>();
    app.UseMiddleware<SecurityHeadersMiddleware>();
    app.UseSerilogRequestLogging(o => o.EnrichDiagnosticContext = (d, http) =>
    {
        d.Set("UserName", http.User.Identity?.IsAuthenticated == true ? http.User.Identity.Name : "-");
        d.Set("RemoteIp", http.Connection.RemoteIpAddress?.ToString());
    });
    app.UseMiddleware<ExceptionHandlingMiddleware>();
    app.UseStatusCodePagesWithReExecute("/Home/Error", "?code={0}");
    app.UseStaticFiles();
    app.UseRouting();
    app.UseRateLimiter();
    app.UseSession();
    app.UseAuthentication();
    app.UseAuthorization();
    app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");
    await app.RunAsync();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Host terminated unexpectedly");
    throw;
}
finally { Log.CloseAndFlush(); }

static bool IsApi(HttpRequest r) => r.Path.StartsWithSegments("/api");
static Task Status(HttpResponse r, int code) { r.StatusCode = code; return Task.CompletedTask; }
static Task Redirect(Microsoft.AspNetCore.Authentication.RedirectContext<CookieAuthenticationOptions> c) { c.Response.Redirect(c.RedirectUri); return Task.CompletedTask; }
