using System.Security.Claims;
using KtcApiExplorer.Application.Abstractions;
using KtcApiExplorer.Application.Common;
using KtcApiExplorer.Application.Dtos;
using KtcApiExplorer.Application.Services;
using KtcApiExplorer.Web.ViewModels;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace KtcApiExplorer.Web.Controllers;

[AllowAnonymous]
public class AccountController : Controller
{
    readonly IAccountService _accounts; readonly IKtcAuthService _ktc; readonly IAuditService _audit;
    public AccountController(IAccountService accounts, IKtcAuthService ktc, IAuditService audit) { _accounts = accounts; _ktc = ktc; _audit = audit; }

    [HttpGet] public IActionResult Login(string? returnUrl = null) => User.Identity?.IsAuthenticated == true ? RedirectToAction("Index", "Home") : View(new LoginViewModel { ReturnUrl = returnUrl });

    [HttpPost, EnableRateLimiting("login")]
    public async Task<IActionResult> Login(LoginViewModel m, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View(m);
        var (user, error) = await _accounts.ValidateAsync(m.UserName, m.Password, ct);
        if (user == null) { m.Password = ""; m.Error = error; return View(m); }

        HttpContext.Session.Clear();   // never reuse a pre-login session
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, user.Id.ToString()), new(ClaimTypes.Name, user.UserName), new("display_name", user.DisplayName), new(ClaimTypes.Role, user.Role) };
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)));
        await _audit.LogAnonymousAsync(user.UserName, "Login", $"role={user.Role}", ct);
        return !string.IsNullOrEmpty(m.ReturnUrl) && Url.IsLocalUrl(m.ReturnUrl) ? LocalRedirect(m.ReturnUrl) : RedirectToAction("Index", "Home");
    }

    [HttpPost, Authorize]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        await _audit.LogAsync("Logout", ct: ct);
        try { await _ktc.LogoutAsync(ct); } catch (Exception) { /* local sign-out must always succeed */ }
        HttpContext.Session.Clear();
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(Login));
    }

    [Authorize] public IActionResult Denied() => View();
}

public class HomeController : Controller
{
    public IActionResult Index() => View();

    [AllowAnonymous, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Error(int? code)
    {
        var c = code ?? 500;
        Response.StatusCode = c;
        return View(new ErrorViewModel
        {
            Code = c, CorrelationId = HttpContext.Items["CorrelationId"] as string,
            Title = c switch { 403 => "Access denied", 404 => "Page not found", 429 => "Too many requests", _ => "Something went wrong" }
        });
    }
}

public class KtcController : Controller
{
    readonly IExplorerService _explorer; readonly IKtcAuthService _auth;
    public KtcController(IExplorerService explorer, IKtcAuthService auth) { _explorer = explorer; _auth = auth; }
    public async Task<IActionResult> Login(CancellationToken ct) => View(new KtcLoginViewModel { Collections = await _explorer.GetCollectionsAsync(ct), Status = _auth.GetStatus() });
}

public class ExplorerController : Controller { public IActionResult Index() => View(); }
public class HistoryController : Controller { public IActionResult Index() => View(); }
public class LibraryController : Controller { public IActionResult Index() => View(); }
public class MonitoringController : Controller { public IActionResult Index() => View(); }

[Authorize(Policy = "Admin")]
public class CollectionsController : Controller
{
    readonly IExplorerService _explorer; readonly IPostmanImportService _import;
    public CollectionsController(IExplorerService explorer, IPostmanImportService import) { _explorer = explorer; _import = import; }

    public async Task<IActionResult> Index(CancellationToken ct) =>
        View(new CollectionsViewModel { Collections = await _explorer.GetCollectionsAsync(ct), LastImport = TempData.Peek("ImportResult") is string s ? Newtonsoft.Json.JsonConvert.DeserializeObject<ImportResultDto>(s) : null });

    [HttpPost, RequestSizeLimit(30 * 1024 * 1024)]
    public async Task<IActionResult> Import(ImportFormModel m, CancellationToken ct)
    {
        if (m.CollectionFile is not { Length: > 0 }) { TempData["Error"] = "Choose a Postman collection (.json) file."; return RedirectToAction(nameof(Index)); }
        if (m.CollectionFile.Length > 25 * 1024 * 1024 || !m.CollectionFile.FileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) { TempData["Error"] = "The collection must be a .json file up to 25 MB."; return RedirectToAction(nameof(Index)); }
        if (m.HelpFile != null && (m.HelpFile.Length > 25 * 1024 * 1024 || !(m.HelpFile.FileName.EndsWith(".html", StringComparison.OrdinalIgnoreCase) || m.HelpFile.FileName.EndsWith(".htm", StringComparison.OrdinalIgnoreCase)))) { TempData["Error"] = "The documentation must be an .html file up to 25 MB."; return RedirectToAction(nameof(Index)); }
        try
        {
            using var r1 = new StreamReader(m.CollectionFile.OpenReadStream());
            string? html = null;
            if (m.HelpFile is { Length: > 0 }) { using var r2 = new StreamReader(m.HelpFile.OpenReadStream()); html = await r2.ReadToEndAsync(ct); }
            var result = await _import.ImportAsync(new ImportRequest
            {
                CollectionJson = await r1.ReadToEndAsync(ct), FileName = Path.GetFileName(m.CollectionFile.FileName), HelpHtml = html,
                BaseUrl = m.BaseUrl, CollectionName = m.CollectionName
            }, ct);
            TempData["ImportResult"] = Newtonsoft.Json.JsonConvert.SerializeObject(result);
            TempData["Success"] = $"Imported '{result.CollectionName}': {result.Endpoints} endpoints in {result.Folders} folders.";
        }
        catch (AppException ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> BaseUrl(int id, string baseUrl, CancellationToken ct)
    {
        try { await _explorer.UpdateBaseUrlAsync(id, baseUrl, ct); TempData["Success"] = "Base URL updated."; } catch (AppException ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Deactivate(int id, CancellationToken ct)
    {
        try { await _explorer.DeleteCollectionAsync(id, ct); TempData["Success"] = "Collection deactivated (history is kept)."; } catch (AppException ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Index));
    }
}

[Authorize(Policy = "Admin")]
public class AdminController : Controller
{
    readonly IAccountService _accounts; public AdminController(IAccountService accounts) => _accounts = accounts;

    public async Task<IActionResult> Users(CancellationToken ct) => View(new UsersViewModel { Users = await _accounts.GetUsersAsync(ct), Roles = await _accounts.GetRolesAsync(ct) });

    [HttpPost]
    public async Task<IActionResult> SaveUser(SaveUserDto dto, CancellationToken ct)
    {
        try { await _accounts.SaveUserAsync(dto, ct); TempData["Success"] = "User saved."; } catch (AppException ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Users));
    }

    // NB: the filter must not be called "action" - MVC binds that name from the route (the action name).
    public async Task<IActionResult> Audit(string? actionName, string? user, int skip = 0, int take = 50, CancellationToken ct = default) =>
        View(new AuditViewModel { Page = await _accounts.GetAuditAsync(actionName, user, Math.Max(0, skip), take, ct), Action = actionName, User = user, Skip = Math.Max(0, skip), Take = Math.Clamp(take, 10, 200) });
}
