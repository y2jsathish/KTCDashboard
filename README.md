# KTC API Explorer

An enterprise ASP.NET Core 8 MVC application that imports the **KTC REST API Postman collection** (v2.1) and turns its 805 endpoints into a Swagger/Postman-style
explorer: folder tree, request editor, response panel, KTC login with auto-renewing tokens, code generation, history, test cases, monitoring and a dashboard.

```
Search bar ───────────────────────────────────────────────────────────────
Folder tree (110 groups) │ Request editor (params/headers/body/code/AI) │ Response panel
```

## Quick start

Prerequisites: .NET 8 SDK, SQL Server (LocalDB/Express is fine).

```bash
# 1. create the database objects (or set Database:Initialize=EnsureCreated for a dev box)
sqlcmd -S . -d KtcApiExplorer -i database/001_schema.sql -i database/002_seed_roles.sql

# 2. configure (edit appsettings.json or use user-secrets / env vars)
cd src/KtcApiExplorer.Web
dotnet user-secrets init
dotnet user-secrets set "ConnectionStrings:Default" "Server=.;Database=KtcApiExplorer;Trusted_Connection=True;TrustServerCertificate=True"
dotnet user-secrets set "Seed:AdminPassword" "ChangeMe-Passw0rd!"      # optional - otherwise a password is generated and printed once

# 3. run
dotnet run          # http://localhost:5080
```

1. Sign in as `admin`. 2. **Admin → Collections**: upload `samples/KTC_REST_API.postman_collection.json`, optionally `samples/KTC_REST_API_Documentation.html`
(adds real groups + descriptions) and the **base URL** of your KTC server (the Postman file has none; the doc's links suggest `https://<host>/KTCRESTAPI`).
3. Click the **KTC** badge → sign in with Username / Password / AppName / AuthMode. 4. Open **Explorer**.

No KTC server at hand? `python3 tools/mock_ktc_server.py` starts a fake one on `http://127.0.0.1:9099/KTCRESTAPI` (login password `Secret123`).

Tests: `dotnet test` (44 tests, including the real 805-endpoint collection and help document).

## Features

| Area | What you get |
|---|---|
| Import | Postman v2.1 upload; folders, methods, headers, bodies, examples, query/path parameters; optional help-doc enrichment; idempotent re-import |
| Explorer | Collapsible 3-panel layout; tree with search, method filter, folder filter, favourites/recent scopes; `/` focuses search, `Ctrl+Enter` executes |
| Execution | Path/query/header/body editing, live URL preview, time/status/size, response headers, highlighted JSON, masked-by-default body, copy, download (binary-safe), compare with previous run |
| KTC login | `POST api/v1/Login`; token kept server-side; expiry badge with countdown; silent renewal via `Relogin` (fallback `Login`); 401 → renew and retry once |
| Developer | cURL, C# `HttpClient`, RestSharp, JavaScript `fetch`, C# DTO models (Newtonsoft attributes) – bearer replaced by a variable, never the real token |
| AI assist | Explain API, generate request body, sample test data, identify required fields, generate documentation (offline heuristic engine by default; optional Claude) |
| Library | Favourites, recently used, request templates (private/shared), test cases (expected status / body text / stored baseline + diff) |
| Monitoring | Flag GET/HEAD endpoints → health check run, avg/last latency, failure counts, 14-day error & latency trend |
| Dashboard | Total / GET / POST / PUT / DELETE counts, usage by API, success vs failure, response-time trend; light/dark |
| Admin | Users & roles, audit log (filterable), collection base URL / deactivate |

## Security model

* **Roles** – `Admin` (import, users, audit, monitor flags) · `Developer` (all methods) · `Viewer` (GET/HEAD/OPTIONS only). Enforced **server-side** in `ApiExecutionService`, not just hidden in the UI.
* **KTC tokens** live only in server-side session state, additionally encrypted with ASP.NET Data Protection; the browser only holds an opaque, HttpOnly, SameSite cookie.
  The password is kept (encrypted, server-side) only to allow silent renewal – set `Ktc:StoreCredentialsForRefresh=false` to disable.
* **Masking** – Authorization/cookie headers and JSON keys matching password/token/secret/apikey/… are masked before anything is stored in history/audit or logged; the response panel masks by default (toggle to reveal).
  Set `Execution:StoreResponseBody=false` if responses must never be persisted.
* **SSRF/token-leak guard** – the URL is rebuilt server-side from the collection base URL + endpoint route (client URLs are ignored); path values are escaped; the result must stay on the configured host/path; redirects are not followed; clients cannot override `Authorization`/`Host`.
* **Web hardening** – cookie auth with lockout (5 failures → 15 min), PBKDF2-SHA256 (210k iterations), antiforgery on every POST (incl. AJAX header), login rate limiting, CSP (no inline scripts, no CDNs – libraries are vendored in `wwwroot/lib`), `X-Frame-Options`, `nosniff`, HSTS.
* **Audit** – login/logout, KTC login, imports, every API execution, template/test changes, user management; each entry carries IP and correlation id (also returned as `X-Correlation-ID` and shown on error pages).
* **Errors** – business errors become 4xx with safe messages; unexpected errors become a generic 500 with only the correlation id; details go to Serilog (console + rolling file `logs/`).

## Configuration reference (`appsettings.json`)

| Key | Default | Meaning |
|---|---|---|
| `ConnectionStrings:Default` | LocalDB-style | SQL Server connection |
| `Database:Initialize` | `None` | `EnsureCreated` creates tables automatically (dev) |
| `Database:Provider` | `SqlServer` | `Sqlite` exists **for local development/smoke tests only** |
| `Seed:AdminUserName / AdminPassword` | `admin` / *(generated)* | first administrator (created only if no users exist) |
| `Ktc:DefaultBaseUrl` | `https://localhost/KTCRESTAPI` | used when an import supplies none |
| `Ktc:TokenRefreshSkewSeconds` | `90` | renew when fewer seconds remain |
| `Ktc:DefaultTokenLifetimeMinutes` | `20` | used when the login response has no expiry |
| `Ktc:AllowInvalidCertificates` | `false` | **dev only** – self-signed KTC certificates |
| `Execution:MaxResponseBytes` / `MaxStoredBodyChars` / `StoreResponseBody` | 5 MB / 32 000 / `true` | response limits and history retention |
| `Ai:Provider` | `Heuristic` | `Anthropic` sends endpoint metadata + sample payload (never tokens/responses) to the Claude API; set `Ai:ApiKey` or `ANTHROPIC_API_KEY`; falls back to Heuristic on any failure |
| `DataProtection:KeysPath` | *(empty)* | **set this in production** so sessions survive restarts / multiple instances |

## Production notes

* Persist Data Protection keys (`DataProtection:KeysPath` or a shared store) and replace `AddDistributedMemoryCache` with Redis/SQL Server cache if you run more than one instance.
* Behind a reverse proxy add `UseForwardedHeaders` so audit entries record client IPs.
* Dashboard/health aggregates read a 14–30 day window into memory (portable, simple); for very high volumes move them to SQL group-by queries or a summary table. `database/003_maintenance.sql` has a retention job.
* Health checks run sequentially with the current user's KTC session; a scheduled unattended check would need a dedicated service account.

## Assumptions & known limits (please read)

* **The Login response schema is not in the supplied documentation.** `KtcTokenParser` accepts `access_token`/`ktc_access_token` (several spellings, nested, bare string) and derives expiry from `expires_in`, an expiry date, or the JWT `exp`. `Authorization: Bearer <access_token>` is used for calls; `ktc_access_token` is stored and available as the `{{ktc_access_token}}` variable. Adjust `KtcTokenParser`/`RequestBuilder` if KTC differs.
* `Relogin` semantics are inferred from the help text ("Refresh the auth token of an existing session"); if it fails the service falls back to `Login`.
* The collection has **no request-body schemas beyond sample strings** (`"sample string 2"`, `null`); "required field" detection and generated DTOs are therefore heuristic.
* Verified with automated tests (44) and an end-to-end browser run against a mock KTC server using SQLite. **Not verified against a live KTC server, a real SQL Server instance, or the Claude provider** (no credentials available here) – the SQL script is generated from the EF model but should be reviewed/run in a test database first.
* `multipart/form-data` file parts are not supported (text fields only); none appear in the supplied collection.

See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for diagrams and the data model.
