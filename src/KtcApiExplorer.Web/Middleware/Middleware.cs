using KtcApiExplorer.Application.Common;
using KtcApiExplorer.Infrastructure.Security;
using Serilog.Context;

namespace KtcApiExplorer.Web.Middleware;

/// <summary>Assigns every request a correlation id (honours an incoming X-Correlation-ID), echoes it back, and tags all log events with it.</summary>
public class CorrelationIdMiddleware
{
    public const string Header = "X-Correlation-ID";
    readonly RequestDelegate _next; public CorrelationIdMiddleware(RequestDelegate next) => _next = next;
    public async Task Invoke(HttpContext ctx)
    {
        var id = ctx.Request.Headers[Header].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(id) || id.Length > 64 || !id.All(c => char.IsLetterOrDigit(c) || c is '-' or '_')) id = Guid.NewGuid().ToString("N");
        ctx.Items[HttpContextCurrentUser.CorrelationItem] = id;
        ctx.Response.Headers[Header] = id;
        using (LogContext.PushProperty("CorrelationId", id)) await _next(ctx);
    }
}

public class SecurityHeadersMiddleware
{
    readonly RequestDelegate _next; public SecurityHeadersMiddleware(RequestDelegate next) => _next = next;
    public Task Invoke(HttpContext ctx)
    {
        var h = ctx.Response.Headers;
        h["X-Content-Type-Options"] = "nosniff";
        h["X-Frame-Options"] = "DENY";
        h["Referrer-Policy"] = "no-referrer";
        h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
        // All JS/CSS/fonts are vendored in wwwroot - no third-party origins are needed.
        h["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self'; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
        // API responses can contain KTC data. (Pages that emit antiforgery tokens get no-store from the framework itself.)
        if (ctx.Request.Path.StartsWithSegments("/api")) h["Cache-Control"] = "no-store";
        return _next(ctx);
    }
}

/// <summary>
/// Central error handling. Business exceptions (AppException) become 4xx with a safe message; anything else becomes a generic 500
/// that carries only the correlation id - details go to the log, never to the client. API calls get JSON, page requests get the error page.
/// </summary>
public class ExceptionHandlingMiddleware
{
    readonly RequestDelegate _next; readonly ILogger<ExceptionHandlingMiddleware> _log;
    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> log) { _next = next; _log = log; }

    public async Task Invoke(HttpContext ctx)
    {
        try { await _next(ctx); }
        catch (OperationCanceledException) when (ctx.RequestAborted.IsCancellationRequested) { ctx.Response.StatusCode = 499; }
        catch (Exception ex)
        {
            if (ctx.Response.HasStarted) throw;
            var (status, code, message) = ex switch
            {
                ValidationException v => (400, v.Code, v.Message),
                NotFoundException n => (404, n.Code, n.Message),
                ForbiddenException f => (403, f.Code, f.Message),
                KtcLoginRequiredException k => (409, k.Code, k.Message),
                AppException a => (502, a.Code, a.Message),
                _ => (500, "server_error", "An unexpected error occurred.")
            };

            if (status >= 500 && ex is not AppException) _log.LogError(ex, "Unhandled exception for {Method} {Path}", ctx.Request.Method, ctx.Request.Path);
            else _log.LogInformation("Handled {Code} for {Method} {Path}: {Message}", code, ctx.Request.Method, ctx.Request.Path, message);

            var correlation = ctx.Items[HttpContextCurrentUser.CorrelationItem] as string;
            ctx.Response.Clear();
            ctx.Response.StatusCode = status;
            if (ctx.Request.Path.StartsWithSegments("/api") || ctx.Request.Headers.Accept.ToString().Contains("application/json"))
            {
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync(Newtonsoft.Json.JsonConvert.SerializeObject(new { error = new { code, message, correlationId = correlation } }));
            }
            else if (status == 403) ctx.Response.Redirect("/Account/Denied");
            // other page requests: status-code pages middleware re-executes /Home/Error
        }
    }
}
