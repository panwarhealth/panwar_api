# panwar_api

C# Azure Functions backend for the Panwar Portals project (client dashboard + employee dashboard) and other Panwar Health internal tools. Single API serving multiple frontends — same DB, same auth cookie domain, same Function App.

**Production:** `https://api.panwarhealth.com.au`
**Local dev:** `http://localhost:7071`

## Quick start

```bash
# 1. Copy and fill in local config (DB password, R2 keys, JWT secret)
cp local.settings.template.json local.settings.json

# 2. Apply migrations to create the panwar_portals schema in panwarhealth-db
dotnet ef database update

# 3. Run the host
func start

# 4. (Dev only) seed the database with real Reckitt data from the workbook
curl -X POST http://localhost:7071/api/dev/seed-reckitt
```

See `CLAUDE.md` for the full architecture, conventions, and endpoint reference.

## Stack

- .NET 9, Azure Functions v4, isolated worker
- PostgreSQL (shared `panwarhealth-db` server, dedicated `panwar_portals` schema)
- EF Core 9 + Npgsql
- Cloudflare R2 (private bucket, presigned URLs)
- Microsoft 365 SMTP for transactional email
- HMAC-signed JWTs in HttpOnly cookies on `.panwarhealth.com.au`

## Build conventions

- `TreatWarningsAsErrors=true` — every warning is a build break, fix the cause
- Nullable reference types enabled
- Snake_case tables, PascalCase columns
- All secrets in `local.settings.json` (gitignored), Azure App Settings in production

## Repo siblings

- [`panwar_client_dash`](../panwar_client_dash) — React 19 client portal
- [`panwar_employee_dash`](../panwar_employee_dash) — React 19 employee portal
- [`panwar_portals`](../panwar_portals) — project manager folder with the brief
