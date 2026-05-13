# Cursor AI — Production readiness & DigitalOcean Droplet deploy (EbookAI / `Clean_Code`)

**Copy everything below the line into a new Cursor chat** after opening the repo  
`https://github.com/yitservices/EbookAI` on branch **`Clean_Code`** (clone/pull so local matches remote).

---

You are a senior staff engineer + DevOps lead. **Target runtime:** ASP.NET Core **8** app (**assembly `EBookDashboard`**, project likely **`newEbook.csproj`** or repo-root `.csproj` — **verify which file is the single publish entrypoint**). **Deployment target:** **DigitalOcean Droplet** (Ubuntu VM: **systemd + Kestrel + nginx reverse proxy + TLS**). This is **not** the same as **DigitalOcean App Platform** (`.do/app.yaml`); if only App Platform config exists, **still** produce Droplet-specific runbooks (systemd unit, nginx site, certbot, UFW).

## 0) Repo reality check (do first)

1. Confirm **canonical publish project**: `dotnet publish` on the correct `.csproj` (search for duplicate `Program.cs` / nested `newEbook/` folders — one tree must be authoritative).
2. Record **repository layout** in your answer: entry `Program.cs`, `appsettings.json`, `appsettings.Production.json`, `web.config` (IIS only; **optional on Droplet**), `Infrastructure/BookUpstreamHttpClientExtensions.cs`, `Middleware/SessionManagementMiddleware.cs`, `Health/`, `Controllers/`, `DigitalOcean-EnvironmentVariables.txt`, `API-DOCUMENTATION.md`.
3. **Branch:** all findings must apply to **`Clean_Code`** as pushed to `yitservices/EbookAI`.

## 1) Code quality & architecture review

Review and report (with **file paths** and **severity**):

| Area | What to check |
|------|----------------|
| **Secrets** | No committed API keys, DB passwords, Stripe secrets, JWT secrets. `ExternalApi:ApiKey`, `ConnectionStrings`, `OpenAI:ApiKey`, Stripe keys must be **env-only** on production. |
| **Startup validation** | `Models/Options/ExternalApiOptionsValidator.cs` — Production fails if `ExternalApi__ApiKey` / `OpenAI__ApiKey` unset; document required env for Droplet. |
| **HTTP clients** | `Infrastructure/BookUpstreamHttpClientExtensions.cs` — named clients `BookApiShort` / `BookApiLong`, Polly attempt/total/circuit-breaker rules; confirm long-running **chapter generate** uses **long** client (`Services/BookApi/BookApiClient.cs`, `BooksController.AIGenerateBook`). |
| **Auth** | Dual cookie schemes (`Program.cs`); Google/Facebook require **both** client id + secret in production or schemes mis-register. Callback URLs must match **public HTTPS** URL. |
| **Session** | Session + cookie `Secure` policy in non-Development; reverse proxy needs `UseForwardedHeaders` (already in `Program.cs`) — nginx must send `X-Forwarded-Proto`, `X-Forwarded-For`. |
| **Database** | `MySqlConnectionStringFactory`, `Database:*` SSL CA (`Certificates/` or `Database__SslCaPem`); migrations applied; connection string from **env** on Droplet. |
| **Long requests** | Chapter generate/edit: Kestrel limits in `Program.cs`; **nginx** `proxy_read_timeout` / `proxy_send_timeout` (recommend **≥ 3600s** for AI routes or use background jobs); client `ChapterGeneration:BrowserFetchTimeoutMinutes` in appsettings. |
| **Binary / native deps** | **PuppeteerSharp** — Chromium on Linux; document install or disable feature if unused. **Rotativa** / PDF — wkhtmltopdf or bundled binary paths on Linux. **NAudio** — Windows-centric APIs; flag any server-side usage on Linux. |
| **Publish output** | `artifacts/publish-release/` or build outputs must be **.gitignore**’d; never deploy stale committed DLLs instead of fresh `dotnet publish -c Release`. |

Output: **ordered backlog** (P0 / P1 / P2) with file references.

## 2) DigitalOcean Droplet — deployment blockers

Assume: **Ubuntu 22.04**, **nginx** → `http://127.0.0.1:5000` (or unix socket) → **Kestrel** `dotnet EBookDashboard.dll`.

Flag each as **BLOCKER** or **OK** with fix:

1. **.NET 8 runtime** on Droplet (`dotnet --version`).
2. **`ASPNETCORE_ENVIRONMENT=Production`** for systemd service.
3. **Working directory** = publish folder containing `EBookDashboard.dll`, `appsettings.Production.json`, `wwwroot`, `Certificates/` if used.
4. **Linux user** for service: read-only where possible; **write** access for `wwwroot/uploads`, logs, any temp paths.
5. **`ConnectionStrings__DefaultConnection`** (and optional `Database__*`) set in `/etc/systemd/system/*.service` `Environment=` or `EnvironmentFile=` — **never** only in committed appsettings for prod.
6. **`ExternalApi__ApiKey`** (or `OpenAI__ApiKey` fallback) **required** — validator + upstream `X-API-Key` via `BookApiAuthenticationHandler`.
7. **`App__PublicBaseUrl`** = public `https://yourdomain.com` (Stripe/OAuth redirects, absolute links).
8. **nginx** TLS (certbot), `client_max_body_size` for uploads/audio/covers, gzip optional, **timeouts** for `/Books/AIGenerateBook` and long POSTs.
9. **UFW**: allow 22, 80, 443; app listens **localhost** only if nginx fronts.
10. **Health**: app maps **`/health`** (`Program.cs`) — use in nginx `location` or monitoring; ensure health not blocked by auth middleware incorrectly.
11. **MySQL** reachable from Droplet (managed DB firewall allows Droplet IP); SSL CA if required.
12. **Upstream book API** (`ExternalApi__*Url`) reachable from Droplet (not only from dev laptop); no `localhost` URLs unless worker is co-located.
13. **Stripe webhooks** URL must hit public HTTPS endpoint.

Deliverable: **pre-flight checklist** (checkboxes) + **example `systemd` unit** + **example `nginx` server block** snippets tailored to this app’s paths and `/health`.

## 3) Critical issues (must fix before go-live)

List any **P0** from sections 1–2. Minimum bar:

- Production secrets **only** via env / secret manager.
- DB connectivity + migrations.
- External API key + reachable upstream URLs.
- nginx timeouts + forwarded headers for HTTPS cookies.
- OAuth callback URLs match production domain.

## 4) Actionable fix order (execute in Cursor)

1. **P0 blockers** — config, secrets, publish path, systemd, nginx, DB.
2. **P0 correctness** — wrong HTTP client timeout for generate vs edit; missing `[IgnoreAntiforgeryToken]` on JSON APIs if antiforgery breaks clients (verify `BooksController` POST routes used by SPA/fetch).
3. **P1** — logging (structured), correlation IDs for upstream calls, reduce `Console.WriteLine` in hot paths for production log drivers.
4. **P1** — remove or ignore committed `artifacts/`; tighten `.gitignore`.
5. **P2** — background job queue for chapter generate (if product requires >60s UX), caching read-only metadata.

For each item: **what to change**, **which file**, **how to verify** (`curl`, `journalctl`, integration test).

## 5) Performance & reliability (3+ minute chapter generation)

- Measure: upstream vs BFF vs DB (`BookApiLoggingHandler` timings in logs).
- **Polly:** avoid redundant retries on **timeouts**; tune `ChapterGeneration:*` and `BookUpstreamHttpClientExtensions` to match nginx + browser limits (`ChapterGeneration:BrowserFetchTimeoutMinutes`).
- **Optional:** async job + polling for chapter jobs; **queue** visibility via `GET /Books/GetQueueData` / upstream `queue-data`.
- **DB:** indexes on hot queries; truncate or stream large `APIRawResponse` writes if they block the thread pool.

## 6) Monitoring & prevention

- Alerts: **5xx rate**, **health check down**, **disk full** (`wwwroot/uploads`), **MySQL connection errors**, **upstream non-200** on `BookApi` logs.
- Uptime check on `https://domain/health` every 60s.
- Log aggregation (DO Insights, Grafana Loki, or CloudWatch) — **no secrets in logs** (never log `X-API-Key` or full Stripe payloads).

## 7) Final deliverable format

Respond in this structure:

1. **Executive summary** (5 bullets: deployable yes/no + top risks)  
2. **P0 / P1 / P2 tables** (issue | file | fix | verify)  
3. **Droplet runbook** (systemd + nginx + env file template + certbot one-liner)  
4. **App Platform note** (if `.do/app.yaml` exists: say it targets **App Platform**, not Droplet; either adapt or ignore for VM deploy)  
5. **Smoke test script** (5–10 `curl` commands: `/health`, login page, static asset, optional authenticated API with cookie note)

**Constraints:** Do not invent files — if a path differs on `Clean_Code`, **search the repo** and correct. **Do not** suggest committing secrets. Assume the human deploys to **Droplet** unless they explicitly say App Platform.

---

_End of prompt — paste from the title through this line._
