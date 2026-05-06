# Lintty Engine

This is the .NET 8 solution behind the Lintty product:

- `Lintty.Engine.Core` — Roslyn analyzer pipeline (the 7 LNTY-* rules).
- `Lintty.Engine.Cli` — `lintty-engine analyze` (the standalone, deterministic CLI).
- `Lintty.Engine.Reporter` — QuestPDF audit-PDF generator (the laudo).
- `Lintty.Docs.Pdf` — separate brand PDF tool (ADRs/manuals, not the laudo).
- `Lintty.WebInspector` — ASP.NET Core minimal API that wraps the same CLI for `lintty.com/inspect`.

## Common commands (run from `engine/`)

```bash
dotnet build Lintty.Engine.sln
dotnet test  Lintty.Engine.sln

# CLI against a fixture (note: --target path is relative to cwd):
dotnet run --project src/Lintty.Engine.Cli -- analyze \
  --target ../fixtures/the-sinner/Sinner.sln \
  --pdf laudo-sinner.pdf
```

## Local Postgres for the Web Inspector (ADR 0007)

The Web Inspector backing store moved from SQLite to Postgres in Sprint 1 of
the V1+ Dashboard work. To run the host locally you need a Postgres reachable
on `localhost:5432`. The repo ships a one-shot dev stack:

```bash
# from the repo root:
docker compose -f engine/docker-compose.yml up -d        # starts Postgres 16
docker compose -f engine/docker-compose.yml ps           # check status
docker compose -f engine/docker-compose.yml down         # stop
docker compose -f engine/docker-compose.yml down -v      # stop + wipe data volume
```

Default credentials (matching `src/Lintty.WebInspector/appsettings.json`):

```text
Host=localhost
Port=5432
Database=lintty_dev
Username=lintty
Password=lintty_dev
```

To override the connection string without editing `appsettings.json`, set:

```bash
export LINTTY_POSTGRES__CONNECTIONSTRING="Host=...;..."
```

Then run the Web Inspector:

```bash
dotnet run --project src/Lintty.WebInspector
# - http://localhost:5180/healthz
# - http://localhost:5180/swagger
# - http://localhost:5180/inspect.html  (when landing/ is reachable)
```

## Schema management (ADR 0007 Sprint 2)

Sprint 2 moved schema ownership from raw SQL bootstrap to **EF Core
migrations**. The single migration `InitialIdentityAndTenant` covers Identity
tables (`users`, `roles`, `user_claims`, `user_logins`, `user_tokens`,
`role_claims`, `user_roles`), tenant tables (`orgs`, `org_members`,
`external_logins`), and absorbs the V0 tables (`jobs`, `rate_limits`).

### Apply the migration

In **Development** (or under the test factory), the host applies pending
migrations at startup automatically:

```bash
dotnet run --project src/Lintty.WebInspector
```

In **Production**, the auto-migrate is intentionally disabled. Operators run
the EF tooling manually before booting the app:

```bash
# from engine/
export LINTTY_POSTGRES__CONNECTIONSTRING="Host=...;Port=5432;Database=...;Username=...;Password=..."
dotnet ef database update --project src/Lintty.WebInspector
```

The `dotnet-ef` global tool needs to be installed once:

```bash
dotnet tool install --global dotnet-ef --version 8.0.10
```

### Adding a new migration

```bash
# from engine/
dotnet ef migrations add <Name> --project src/Lintty.WebInspector --output-dir Persistence/Migrations
```

Review the generated `*.cs` files in `src/Lintty.WebInspector/Persistence/Migrations/`
before committing.

## GitHub OAuth (ADR 0007 Sprint 2)

The Web Inspector supports GitHub OAuth login at
`POST /api/auth/github/start` → callback at `/api/auth/github/callback`.
Without credentials configured, the start endpoint returns **503**
(`github_oauth_not_configured`) — the host does not crash.

### Local setup

1. Create a GitHub OAuth app at <https://github.com/settings/developers>:
   * **Application name**: anything (e.g. `Lintty (Local Dev)`).
   * **Homepage URL**: `http://localhost:5180`.
   * **Authorization callback URL**: `http://localhost:5180/api/auth/github/callback`.
2. Copy the Client ID and generate a Client Secret.
3. Export the env vars before running the app:

```bash
export LINTTY_GITHUB__CLIENTID="Iv1.xxxxxxxxxxxxxxxx"
export LINTTY_GITHUB__CLIENTSECRET="xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx"
dotnet run --project src/Lintty.WebInspector
```

Scopes requested by the app: **`read:user user:email`** only (ADR 0007 §3.6).
No `repo` scope, no `read:org`.

## Environment variables

| Var | Purpose | Required |
|---|---|---|
| `LINTTY_POSTGRES__CONNECTIONSTRING` | Postgres connection string. Overrides `appsettings.json`. | Yes (prod) |
| `LINTTY_GITHUB__CLIENTID` | OAuth GitHub client id. | No (returns 503 if absent) |
| `LINTTY_GITHUB__CLIENTSECRET` | OAuth GitHub client secret. | No |

### Tests

The xUnit test suite uses [Testcontainers.PostgreSql](https://dotnet.testcontainers.org/),
which spins up an ephemeral Postgres per test class against the locally
installed Docker daemon. **You do not need the dev compose stack running for
tests** — only Docker itself. If Docker isn't running, the Web Inspector tests
will fail at fixture initialization with a clear error pointing here.

```bash
dotnet test Lintty.Engine.sln                                           # full suite
dotnet test tests/Lintty.WebInspector.Tests                             # web inspector only
dotnet test tests/Lintty.Engine.Reporter.Tests                          # determinism gate
```
