# Panwar API

Single backend serving the Panwar Portals project (client dashboard + employee dashboard) plus future Panwar Health internal tools (Word reference manager, brand planner, eDM sender, etc.). Don't rename `panwar-api` to `panwar-portals-api` — it's deliberately generic.

## Tech stack

- **Runtime:** Azure Functions v4 (.NET 9, isolated worker, C#)
- **Database:** PostgreSQL on shared `panwarhealth-db` Azure Postgres flexible server, dedicated `panwar_portals` schema, EF Core 9 + Npgsql
- **Storage:** Cloudflare R2 for placement artwork (single private bucket `panwar-portals-media`, presigned URLs only — no public access)
- **Auth — clients:** custom magic link → JWT in HttpOnly cookie scoped to `.panwarhealth.com.au`
- **Auth — employees:** Microsoft Entra ID SSO (TBD — app registration not yet created)
- **Email:** Microsoft 365 SMTP (`smtp.office365.com`) via OAuth2 client credentials → AUTH XOAUTH2, sending from `noreply@panwarhealth.com.au` (shared mailbox + Entra app reg also used by pharmachat_api and osteo_xchange_api)
- **JWT:** HMAC-SHA256 with shared secret in env (matches house pattern, NOT RSA in Key Vault — see project memory)
- **Hosting:** Azure Functions consumption plan, custom domain `api.panwarhealth.com.au`

## Project structure

```
panwar_api/
├── Functions/                # HTTP-triggered functions grouped by domain
│   ├── Auth/                 # MagicLinkRequest, MagicLinkVerify, Me, Logout
│   ├── Health/               # HealthCheck
│   └── Dev/                  # Dev-only endpoints (e.g. SeedReckitt)
├── Models/                   # EF Core entities + enums + DTOs
│   ├── Enums/                # PlacementObjective, MetricTemplateCode, etc.
│   └── DTOs/                 # Request/response shapes
├── Services/                 # Business logic (interface + impl pairs)
│   └── Seed/                 # ReckittSeedService (one-shot dev seeder)
├── Data/                     # AppDbContext + AppDbContextFactory (design-time)
├── Migrations/               # EF Core generated
├── Shared/
│   ├── Middleware/           # Correlation, Authentication, RateLimit
│   ├── Helpers/              # CookieHelper
│   └── Extensions/           # HttpRequestDataExtensions
├── Infrastructure/
│   └── CloudflareR2/         # CloudflareR2Service
├── EmailTemplates/           # MJML sources for email layouts
├── Program.cs
├── host.json
├── local.settings.template.json
└── Panwar.Api.csproj
```

## Database

`panwar_portals` schema in the existing `panwarhealth-db` Postgres database (Postgres 18). Sibling schemas: `pharmachat_schema` (PharmaChat), `lms_schema` (Clinical Studio — being spun out), `panwarhealth_users` (PharmaChat-only users — **do not** add new FKs into this).

**21 tables** for the panwar_portals schema. The data model lives in the project memory at `C:/Users/User/.claude/projects/F--Github-panwar-portals/memory/project_data_model.md` — read that for the canonical schema, **not** section 7 of `PANWAR_PORTALS_PROJECT_BRIEF.md` which is rushed and incomplete.

**Naming convention:** snake_case table names (`placement_actual`), PascalCase column names (`MediaCost`, `BrandId`). This matches PharmaChat + osteo_xchange. Raw SQL queries must double-quote column names: `SELECT "Name" FROM panwar_portals.placement`.

## Local development setup

1. **Postgres connection** — the `panwarhealth-db` Azure Postgres flexible server is shared. Get the password from `pharmachat_api/local.settings.json` or 1Password. The DB name is `postgres`, the schema is `panwar_portals`.

2. **Configuration** — copy the template and fill in:
   ```bash
   cp local.settings.template.json local.settings.json
   ```
   Required fields: `DATABASE_CONNECTION_STRING`, `JWT_SECRET` (run `openssl rand -base64 64`), `EMAIL_*` (copy from osteo_xchange_api/local.settings.json), `CLOUDFLARE_R2_*` (from Cloudflare R2 dashboard → Manage R2 API Tokens).

3. **Apply migrations** — creates the `panwar_portals` schema and all 21 tables:
   ```bash
   dotnet ef database update
   ```

4. **Run the host:**
   ```bash
   func start
   ```
   API listens on `http://localhost:7071`.

5. **Seed Reckitt data** (dev only — gated to `Development` environment):
   ```bash
   curl -X POST http://localhost:7071/api/dev/seed-reckitt
   ```
   Path to the workbook comes from `RECKITT_WORKBOOK_PATH` in `local.settings.json`. The seeder is **idempotent** — running it again wipes Reckitt data and re-imports.

## Common commands

```bash
func start                              # Run locally
dotnet build                            # Build (must be 0 errors, 0 warnings — TreatWarningsAsErrors)
dotnet ef migrations add <Name>         # Create new migration
dotnet ef database update               # Apply migrations
dotnet ef migrations remove             # Undo last unapplied migration
```

## Endpoints (current)

| Method | Path | Auth | Purpose |
|---|---|---|---|
| `GET` | `/api/health` | none | DB connectivity ping for monitoring |
| `POST` | `/api/auth/magic-link` | none | Request a magic link to `{ email }`. Always returns 200. Rate-limited 5/min/IP. |
| `POST` | `/api/auth/magic-link/verify` | none | Verify token, set `panwar_session` cookie, return user. |
| `GET` | `/api/auth/me` | cookie | Return current user (used by SPAs to bootstrap auth). |
| `POST` | `/api/auth/logout` | cookie | Clear the session cookie. |
| `POST` | `/api/dev/seed-reckitt` | none, dev only | One-shot seeder. Refuses unless `AZURE_FUNCTIONS_ENVIRONMENT=Development`. |

## CORS

Configured in `local.settings.json` → `Host.CORS`. Production origins:
- `https://portal.panwarhealth.com.au` (client dash)
- `https://a1.panwarhealth.com.au` (employee dash)

Local dev origins:
- `http://localhost:5173` (client dash Vite default)
- `http://localhost:5174` (employee dash Vite, +1 to avoid collision)

`CORSCredentials: true` so HttpOnly cookies work cross-origin.

## Auth flow (clients)

1. SPA POSTs `{ email }` to `/api/auth/magic-link`
2. API generates a random 32-byte token, stores SHA-256 hash, sends email with `https://portal.panwarhealth.com.au/auth/verify?token=<raw>`
3. User clicks link, SPA POSTs `{ token }` to `/api/auth/magic-link/verify`
4. API hashes the token, looks up the unused unexpired record, marks used, mints a JWT with claims `(sub, email, user_type=client, client_id, role[])`
5. JWT goes into the `panwar_session` HttpOnly cookie scoped to `.panwarhealth.com.au`
6. Browser sends the cookie automatically on subsequent requests
7. `AuthenticationMiddleware` reads the cookie, validates, injects `UserId`/`UserType`/`ClientId`/`Roles[]` into `FunctionContext.Items`
8. Functions grab the user via `req.GetUserId(context)` / `req.GetClientId(context)`

**Clients are NOT auto-provisioned by magic-link verification.** They must be added in the employee portal first (so the editor explicitly chooses which client they belong to). Magic-link verification for an unknown email returns null → 401.

**Employees** auto-provision on first Entra sign-in (TBD — Entra app reg not yet created).

## Reference projects

- **`F:/Github/pharmachat_api`** — `.NET 9 Functions reference. Cribbed for: project layout, `Program.cs`, `host.json`, EF Core setup, Cloudflare R2 service.
- **`F:/Github/osteo_xchange_api`** — magic-link auth reference. Cribbed for: `EmailService` (M365 SMTP/XOAUTH2), `JwtService`, `MagicLinkService`, `AuthService`, middleware, helpers.

When porting from these, **read the source first**, understand it, then mirror — don't blindly copy. Adapt for panwar's user types (client vs employee) and tenant scoping (`client_id` claim).

## Related repos

- **`F:/Github/panwar_client_dash`** — React 19 client portal SPA (`portal.panwarhealth.com.au`)
- **`F:/Github/panwar_employee_dash`** — React 19 employee portal SPA (`a1.panwarhealth.com.au`)
- **`F:/Github/panwar_portals`** — project manager folder with the brief, decision log, and cross-repo CLAUDE.md

## Reference

- `F:/Github/panwar_portals/PANWAR_PORTALS_PROJECT_BRIEF.md` — original brief. **Section 7 (data model) is superseded** by the project memory file.
- `F:/Github/panwar_portals/CLAUDE.md` — cross-repo coding standards (SOLID/KISS/YAGNI, root-cause-fix policy).
- Project memory at `C:/Users/User/.claude/projects/F--Github-panwar-portals/memory/` — running architectural decisions.
