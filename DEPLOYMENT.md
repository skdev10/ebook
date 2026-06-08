# Deployment guide (DigitalOcean + ASP.NET Core BFF)

This app is an **ASP.NET Core 8** web app that calls a **Python FastAPI** upstream. Secrets must come from **environment variables** in production (not `appsettings.Local.json`, which is gitignored and only for local dev).

**Upstream API reference:** see [`docs/EXTERNAL_API.md`](docs/EXTERNAL_API.md) for all FastAPI endpoints, payloads, database tables, and troubleshooting.

## Configuration: nested keys → environment variables

ASP.NET Core maps nested JSON keys to env vars using **double underscores** (`__`):

| appsettings key | Environment variable |
|-----------------|----------------------|
| `ExternalApi:ApiKey` | `ExternalApi__ApiKey` |
| `ExternalApi:BaseUrl` | `ExternalApi__BaseUrl` |
| `ExternalApi:GenerateSpineBookCoverUrl` | `ExternalApi__GenerateSpineBookCoverUrl` |
| `BookPayment:PerPagePriceCents` | `BookPayment__PerPagePriceCents` |
| `BookPayment:MinimumChargeCents` | `BookPayment__MinimumChargeCents` |
| `BookPayment:MaximumChargeCents` | `BookPayment__MaximumChargeCents` |
| `PrintReadyCover:WhitePaperSpineInchesPerPage` | `PrintReadyCover__WhitePaperSpineInchesPerPage` |
| `PrintReadyCover:CreamPaperSpineInchesPerPage` | `PrintReadyCover__CreamPaperSpineInchesPerPage` |
| `PrintReadyCover:ColorPaperSpineInchesPerPage` | `PrintReadyCover__ColorPaperSpineInchesPerPage` |
| `PrintReadyCover:BleedInches` | `PrintReadyCover__BleedInches` |
| `PrintReadyCover:DefaultTrimWidthInches` | `PrintReadyCover__DefaultTrimWidthInches` |
| `PrintReadyCover:DefaultTrimHeightInches` | `PrintReadyCover__DefaultTrimHeightInches` |
| `Authentication:Google:ClientId` | `Authentication__Google__ClientId` |
| `Authentication:Google:ClientSecret` | `Authentication__Google__ClientSecret` |
| `Authentication:Facebook:AppId` | `Authentication__Facebook__AppId` |
| `Authentication:Facebook:AppSecret` | `Authentication__Facebook__AppSecret` |
| `ConnectionStrings:DefaultConnection` | `ConnectionStrings__DefaultConnection` |

**OAuth redirect URIs** (must match production HTTPS URL):

- Google: `https://<your-domain>/auth/google/callback` (default in this repo; configurable via `Authentication:Google:CallbackPath`).
- Facebook: `https://<your-domain>/signin-facebook` (default).

The app enables **`UseForwardedHeaders`** so `X-Forwarded-Proto` / `X-Forwarded-For` from the platform reverse proxy are honored (fixes OAuth redirect and HTTPS detection).

## DigitalOcean App Platform

1. Create an app from this repo (or connect GitHub and select branch).
2. Set **Run command** to publish output, e.g. `dotnet EBookDashboard.dll` after `dotnet publish -c Release -o ./publish` (adjust to your Dockerfile or build output).
3. Under **App Settings → Environment Variables**, add every required key. Mark secrets as **SECRET / encrypted**:
   - `ExternalApi__ApiKey` (SECRET)
   - `ExternalApi__GenerateSpineBookCoverUrl`
   - `BookPayment__PerPagePriceCents`, `BookPayment__MinimumChargeCents`, `BookPayment__MaximumChargeCents`
   - `ConnectionStrings__DefaultConnection` (SECRET)
   - `Authentication__Google__ClientSecret`, `Authentication__Facebook__AppSecret` (SECRET)
   - Optional: `OpenAI__ApiKey` (SECRET) — used as fallback if `ExternalApi__ApiKey` is empty for some flows
4. **HTTP health check** path: `/health` (returns JSON including upstream queue probe; HTTP status is **200** when the host is up).
5. **HTTPS**: App Platform terminates TLS; ensure your app URL uses `https://` for OAuth and microphone (`getUserMedia`) in browsers.

See `.do/app.yaml` for a starter spec (update `github.repo`, `source_dir`, and `build_command`/`run_command` to match this repo: project file is `newEbook.csproj` at repo root).

## DigitalOcean Droplet (systemd)

1. Publish on the server: `dotnet publish /path/to/newEbook.csproj -c Release -o /var/ebookdashboard`.
2. Create `/etc/ebookdashboard/ebookdashboard.env` (mode `600`, owner root or the service user):

   ```env
   ASPNETCORE_ENVIRONMENT=Production
   ExternalApi__ApiKey=...
   ExternalApi__BaseUrl=http://your-fastapi-host:8001
   ExternalApi__GenerateSpineBookCoverUrl=http://your-fastapi-host:8001/api/generate-spine-book-cover
   BookPayment__PerPagePriceCents=12
   BookPayment__MinimumChargeCents=999
   BookPayment__MaximumChargeCents=99999
   PrintReadyCover__WhitePaperSpineInchesPerPage=0.002252
   PrintReadyCover__CreamPaperSpineInchesPerPage=0.0025
   PrintReadyCover__ColorPaperSpineInchesPerPage=0.002347
   PrintReadyCover__BleedInches=0.125
   PrintReadyCover__DefaultTrimWidthInches=6.0
   PrintReadyCover__DefaultTrimHeightInches=9.0
   ConnectionStrings__DefaultConnection=...
   Authentication__Google__ClientId=...
   Authentication__Google__ClientSecret=...
   Authentication__Facebook__AppId=...
   Authentication__Facebook__AppSecret=...
   ```

3. Example **`/etc/systemd/system/ebookdashboard.service`**:

   ```ini
   [Unit]
   Description=eBook Dashboard
   After=network.target

   [Service]
   WorkingDirectory=/var/ebookdashboard
   ExecStart=/usr/bin/dotnet /var/ebookdashboard/EBookDashboard.dll
   Restart=always
   User=www-data
   EnvironmentFile=/etc/ebookdashboard/ebookdashboard.env

   [Install]
   WantedBy=multi-user.target
   ```

4. **HTTPS**: Put Nginx/Caddy in front with a Let’s Encrypt cert, or use Cloudflare. Redirect HTTP → HTTPS.
5. **Logs**: use `journalctl -u ebookdashboard -f` or ship stdout to a log drain (Vector, Fluent Bit, Datadog, etc.).

## Upstream API key rotation

If a key was ever exposed in chat or committed artifacts, **rotate it on the FastAPI side** and update `ExternalApi__ApiKey` everywhere before relying on production security.

## Smoke tests

Use `smoke-tests.http` in this folder (REST Client / VS Code) or run the equivalent `curl` commands against your FastAPI base URL with header `X-API-Key: <key>`.
