# ADR 0007 — Dashboard Multi-Tenant V1.0 (extensão do Web Inspector)

- **Status:** Draft
- **Date:** 2026-05-05
- **Author:** Lintty (V1+ slice)
- **Audience:** backend-dev-dotnet (implementador), qa-engineer (gates novos), product-owner (ratificar), security-compliance (LGPD/retenção), backend-dev-cloud (deploy quando o sinal vier)
- **Sprint:** V1+ — primeiro slice multi-tenant. MVP definido em conversa com PO 2026-05-05.
- **Canon pinado:** `1.0.0` (`docs/02-canon-v1.md`) — **não muda**.
- **Schema JSON do laudo:** `1.0` LOCKED — **não muda**.
- **Pré-requisitos atendidos:** ADR 0001 (motor CLI standalone, JSON `1.0`), ADR 0003 (PDF determinístico), ADR 0005 (CLI Self-Service como default), ADR 0006 (target resolution `.sln`/`.csproj`/`lintty.yml`).
- **Estende:** Web Inspector V0 (`docs/13-web-inspector.md` §12 — backend em `engine/src/Lintty.WebInspector/` com 17 testes verdes incluindo gate de determinismo cruzado).
- **Não substitui:** ADR 0005 (CLI Self-Service) continua como default. Dashboard é caminho de aquisição/recorrência, não de privacidade-zero-trust. `/inspect` anônimo continua vivo na mesma rota.

## Revisão de design (estrutura canônica)

1. **Cabe no canon arquitetural?** Sim. O dashboard **estende** o `Lintty.WebInspector` existente, mantendo o princípio #1 (mesmo motor nos dois caminhos) — dashboard, `/inspect` anônimo e CLI invocam o mesmo binário Roslyn via subprocess. Nenhuma regra LNTY-* muda. Schema JSON `1.0` permanece LOCKED.
2. **Princípios afetados (dos 10 do system prompt):**
   - **#1 (mesmo motor)** — preservado: `JobWorker` existente continua sendo o único caller do CLI.
   - **#2 (determinismo bit-a-bit)** — preservado e estendido: gate cruzado novo em `WorkerIntegrationTests` cobre fluxo "scan disparado pelo dashboard == scan CLI" — ver §3.7.
   - **#5 (efêmero ≤60s)** — preservado: clones continuam descartados em ≤60s; o que vira persistente é o **artefato** (PDF + JSON), não o source.
   - **#6 (privacy-zero-trust no CLI)** — intacto: dashboard só opera sobre **repos públicos** no V1.0 (`scopes: read:user user:email`). Repo privado fica V1.1.
   - **#7 (single source of truth = canon pinado)** — reforçado: cada `Scan` grava o `canon_version` no momento da execução.
   - **#8 (severidade não-runtime)** — preservado: dashboard só **exibe** o JSON do motor; nunca recomputa.
   - **#9 (schema JSON `1.0` LOCKED)** — preservado: zero campo novo no JSON do motor. Tabelas Postgres do dashboard são metadados sobre o JSON, não modificações nele.
   - **#10 (`hash_content` é o âncora)** — preservado: dashboard armazena `hash_content` ao lado do PDF para auditoria.
3. **Fronteira:** zero serviços novos. Mesmo binário `Lintty.WebInspector`, novas rotas (`/api/auth/*`, `/api/orgs/*`, `/api/repos/*`, `/api/scans/*`), novas tabelas (Postgres), middleware de auth + tenant context. Worker e CLI inalterados.
4. **V0 ou V1+?** V1+. PO confirmou "go" para o slice MVP em 2026-05-05. Piloto está em prospecção, não confirmado — otimização é flexibilidade > cliente específico.
5. **O que muda no contrato JSON ou no determinismo?** **Nada no contrato JSON do motor.** O determinismo cruzado ganha um gate adicional (CLI ↔ scan dashboard) — `qa-engineer` valida antes do merge.
6. **Próxima decisão (após este ADR aceito):** founder ratifica → backend-dev-dotnet abre PR Sprint 1 (foundations: Postgres + auth básica, sem regredir suíte V0).

## 1. Contexto

O Web Inspector V0 (`docs/13-web-inspector.md` §12) está em pé desde 2026-04-30: backend ASP.NET Core 8 minimal API, SQLite via `Microsoft.Data.Sqlite`, single-instance `JobWorker` (BackgroundService) que invoca o CLI por subprocess, 17 testes verdes incluindo gate de determinismo cruzado em `WorkerIntegrationTests` (`engine/tests/Lintty.WebInspector.Tests/`).

O fluxo `/inspect` anônimo é **gateway de aquisição** — funciona, mas não tem retenção. Para clientes que querem rodar scans recorrentes em repos próprios, o caminho default é o CLI Self-Service (ADR 0005). Falta o **terceiro modo de uso**: cliente cria conta, registra seu repo, dispara scans pelo navegador, vê histórico.

Founder deu "go" em 2026-05-05 para construir esse terceiro modo. PO recortou o MVP V1.0 (signup/login + 1 org + 1 repo público + scan + histórico + convite por link manual + RBAC mínimo + billing read-only). Cortes V1.1: SMTP, RBAC granular, webhook GitHub, diff entre scans, branding, repo privado, multi-org, Stripe.

Decisão de fronteira já fechada com o founder: **estende `Lintty.WebInspector`**, não cria serviço separado. Reuso de `JobStore` (vai virar interface), `JobWorker`, `EngineSubprocessRunner`, `GitCliClient`, `Ulid`, gate de determinismo. Migração SQLite → Postgres (decisão já fechada, esta ADR define o **como**).

`/inspect` anônimo continua vivo. Mesmo binário, mais rotas, mais tabelas, middleware novo.

## 2. Decisão (resumo executivo)

1. **Auth:** `Microsoft.AspNetCore.Identity` com `IdentityCore<User>` + cookie de sessão server-side, hashing PBKDF2 default (Identity), OAuth GitHub via `AspNet.Security.OAuth.GitHub`. Sem JWT. Sem 2FA no V1.0. Ver §3.1.
2. **Modelo de dados:** 7 tabelas Postgres — `users`, `external_logins`, `orgs`, `org_members`, `repos`, `scans`, `invitations` (audit log = V1.1, deferido). `Job` (legado V0) vira `Scan` no contexto multi-tenant — ver §3.2.
3. **Migração SQLite → Postgres:** **clean cut**. Dropa SQLite, novo schema Postgres é a única fonte. Jobs V0 anônimos pré-migração não são preservados (são efêmeros, 24h TTL, baixo valor histórico). Web Inspector anônimo (`/inspect`) passa a gravar em Postgres com `org_id = NULL`. Ver §3.3.
4. **Reuso JobStore/JobWorker:** `IJobStore` vira interface formal, `JobStore` (SQLite) é descartado, `PostgresJobStore` é a única implementação. `JobWorker` recebe `IJobStore` por DI — zero mudança no worker. Ver §3.4.
5. **Multi-tenancy:** filtragem por `org_id` em **todas as queries via repository** (disciplina de aplicação). Middleware `TenantContextMiddleware` injeta `OrgId` em `HttpContext.Items` após auth. Sem RLS Postgres no V1.0 (overkill para 1 org/usuário). Ataques: cookie Org A → recurso Org B retorna **404** (não 403, evita confirmar existência). Ver §3.5.
6. **OAuth GitHub:** scopes `read:user user:email`. Login GitHub cria `User` se email não existe; vincula a `User` existente via `external_logins` se email bate. Sem repo privado no V1.0 — `repo` scope fica V1.1. Ver §3.6.
7. **Determinismo:** `Scan.canon_version` gravado no momento do trigger (`POST /api/scans`). Gate cruzado novo `DashboardScan_Pdf_Equals_CliDirect_Pdf` em `WorkerIntegrationTests`. Ver §3.7.
8. **Storage de PDFs:** local-first em dev (`var/lintty/jobs/<id>/`, layout V0 preservado). Em prod: estrutura `IArtifactStore` com 2 implementações (`LocalArtifactStore`, `S3ArtifactStore` no V1.1+). Retenção configurável (default 90 dias para org-bound, 24h para anônimo). Ver §3.8.
9. **Secret manager (env vars do scan):** V1.0 não persiste env vars sensíveis (só repo público, sem secrets). `ScanConfig` aceita toggles de regras (LNTY-008 on/off etc.) — persistido em plaintext JSONB. Ver §3.9.
10. **Convite:** UUID v4 como token + `redeemed_at`/`expires_at` em tabela `invitations`. Owner copia link manualmente (sem SMTP). TTL 7 dias. Ver §3.10.

## 3. Decisões detalhadas

### 3.1 Auth — `AspNetCore.Identity` + cookie server-side + OAuth GitHub

**Decisão:** `Microsoft.AspNetCore.Identity` (com `IdentityCore<User>` + `IdentityRole<Guid>`), cookie de sessão server-side via `AddCookie`, OAuth GitHub via `AspNet.Security.OAuth.GitHub` (8.x, compatível .NET 8).

**Stack:**

```text
Microsoft.AspNetCore.Identity.EntityFrameworkCore  (8.0.x)  — schema + UserManager
Microsoft.EntityFrameworkCore.Design                          — migrations CLI
Npgsql.EntityFrameworkCore.PostgreSQL              (8.0.x)  — provider Postgres
AspNet.Security.OAuth.GitHub                       (8.x)    — handler OAuth GitHub
```

**Por que Identity + cookie e não JWT:**

- **Cookie revogável.** Logout server-side derruba sessão imediatamente. JWT exigiria blocklist em Redis ou aceitar TTL longo — ambos custam infra que não temos no V1.0.
- **Sem mobile / SPA cross-origin no V1.0.** Front-end mora same-origin com a API (servido pelo mesmo `Lintty.WebInspector` ou Cloudflare Pages com proxy). Cookie `SameSite=Lax` + `HttpOnly` + `Secure` resolve sem CSRF token explícito (verifico complementando com antiforgery do ASP.NET Core).
- **Hashing default do Identity = PBKDF2 (HMAC-SHA256, 10000 iter, 256-bit hash).** Argon2id seria mais forte mas exige `Konscious.Security.Cryptography` (NuGet de terceiro), e PBKDF2 do Identity é suficiente para perfil de ameaça V1.0 (sem dump público de senhas previsto). Revisitar em V1.1 se piloto exigir auditoria SOC 2 leve.

**Fluxos:**

| Endpoint | Método | Comportamento |
|----------|--------|---------------|
| `POST /api/auth/signup` | email + senha | Cria `User` + `Org` (1:1 inicial) + `OrgMember(role=owner)`. Loga (cookie). Retorna `200` com `user_id` e `org_id`. |
| `POST /api/auth/login` | email + senha | Valida credentials, emite cookie. Retorna `200` com `user_id` e `org_id` (busca org primária do user). |
| `POST /api/auth/logout` | — | Encerra cookie. Retorna `204`. |
| `GET /api/auth/me` | (cookie) | Retorna user atual + orgs membro. |
| `GET /api/auth/github/start` | — | Redireciona para GitHub OAuth com `state` HMAC. |
| `GET /api/auth/github/callback` | (code, state) | Troca code por access_token, lê `/user` e `/user/emails`, cria/loga User. |

**Sessões:** server-side via `AddDistributedMemoryCache` no V1.0 (single instance). Em V1.1+ (multi-instance), `AddDistributedSqlServerCache` ou Redis — decisão de deploy adiada para `[backend-dev-cloud]`.

**Senha policy V1.0:** `RequiredLength = 12`, `RequireDigit = true`, `RequireNonAlphanumeric = false`, `RequireUppercase = false`. Rationale: 12 chars + dígito é o mínimo aceitável para perfil B2B 2026 sem virar fricção de signup. Revisitar com `[security-compliance]` se piloto pedir senha policy stronger.

**Recuperação de senha:** **fora do V1.0** (não há SMTP). Owner perdeu senha → reset manual via DB ou re-signup. Documentado como pendência V1.1 explícita em §6.

### 3.2 Modelo de dados — 7 tabelas Postgres

**Princípios:**

- **Pluralizado snake_case** (convenção PG-friendly).
- **PK = `bigint identity` para entidades de domínio** (users, orgs, repos, scans). `Guid` (uuid) só onde precisa ser não-enumerável publicamente (token de invitation; `Scan.public_id` para URLs).
- **FK explícita com `ON DELETE` definido por tabela.** Cascata só onde faz sentido semântico (org → org_members cascade; user → external_logins cascade).
- **Índices nas FKs e nas colunas de filtro frequente** (`scans(repo_id, started_at DESC)`, `repos(org_id)`, `org_members(user_id)`).

```text
users
  id              bigserial PRIMARY KEY
  email           varchar(320) UNIQUE NOT NULL          -- RFC 5321 max
  email_normalized varchar(320) UNIQUE NOT NULL         -- lowercased; usado em lookup
  password_hash   varchar(512)                          -- nullable (user só OAuth não tem senha)
  security_stamp  varchar(64) NOT NULL                  -- Identity
  created_at      timestamptz NOT NULL DEFAULT now()
  -- (campos extras do AspNetCore.Identity ficam preservados pelo schema padrão)

external_logins                                         -- 1 user → N providers
  id              bigserial PRIMARY KEY
  user_id         bigint NOT NULL REFERENCES users(id) ON DELETE CASCADE
  provider        varchar(32) NOT NULL                  -- 'github'
  provider_user_id varchar(128) NOT NULL                -- GitHub user id (numeric string)
  created_at      timestamptz NOT NULL DEFAULT now()
  UNIQUE (provider, provider_user_id)

orgs
  id              bigserial PRIMARY KEY
  slug            varchar(64) UNIQUE NOT NULL           -- usado em URL, gerado de email
  display_name    varchar(128) NOT NULL
  created_at      timestamptz NOT NULL DEFAULT now()
  -- billing_plan, branding, etc. ficam V1.1

org_members                                             -- N:M users <-> orgs com role
  id              bigserial PRIMARY KEY
  org_id          bigint NOT NULL REFERENCES orgs(id) ON DELETE CASCADE
  user_id         bigint NOT NULL REFERENCES users(id) ON DELETE CASCADE
  role            varchar(16) NOT NULL                  -- 'owner' | 'member'
  joined_at       timestamptz NOT NULL DEFAULT now()
  UNIQUE (org_id, user_id)
  INDEX        idx_org_members_user (user_id)

repos
  id              bigserial PRIMARY KEY
  org_id          bigint NOT NULL REFERENCES orgs(id) ON DELETE CASCADE
  github_url      varchar(512) NOT NULL                 -- normalizada (sem .git, lowercased)
  display_name    varchar(128) NOT NULL                 -- 'org/repo' default
  default_branch  varchar(128)                          -- cache do GitHub metadata
  added_by_user_id bigint NOT NULL REFERENCES users(id)
  added_at        timestamptz NOT NULL DEFAULT now()
  scan_config     jsonb NOT NULL DEFAULT '{}'::jsonb    -- toggles de regras; ver §3.9
  UNIQUE (org_id, github_url)                           -- mesma org não cadastra repo 2x
  INDEX        idx_repos_org (org_id)

scans                                                   -- substitui 'jobs' V0
  id              bigserial PRIMARY KEY
  public_id       uuid UNIQUE NOT NULL                  -- usado em URL pública obscura
  repo_id         bigint REFERENCES repos(id) ON DELETE CASCADE  -- nullable: anônimo /inspect
  org_id          bigint REFERENCES orgs(id) ON DELETE CASCADE   -- nullable: anônimo /inspect
  triggered_by_user_id bigint REFERENCES users(id)               -- nullable: anônimo
  github_url      varchar(512) NOT NULL                 -- snapshot, sobrevive a delete do repo
  ref             varchar(128)                          -- branch/sha solicitado
  status          varchar(16) NOT NULL                  -- queued|running|completed|failed
  stage           varchar(32)
  canon_version   varchar(16)                           -- pinado no momento do scan
  score           int
  grade           varchar(2)
  violation_count int
  hard_locks_open int
  hash_content    varchar(80)                           -- 'sha256:...' do JSON
  pdf_path        varchar(512)                          -- abstrato; LocalArtifactStore ou S3 key
  json_path       varchar(512)
  error_code      varchar(32)
  error_message   text
  created_at      timestamptz NOT NULL DEFAULT now()
  started_at      timestamptz
  completed_at    timestamptz
  expires_at      timestamptz                           -- 24h anônimo, 90d org-bound
  client_ip       inet                                  -- só anônimo; null para scans logados (privacidade)
  INDEX        idx_scans_repo_started (repo_id, started_at DESC)
  INDEX        idx_scans_org_created (org_id, created_at DESC)
  INDEX        idx_scans_status_created (status, created_at)        -- worker poll

invitations
  id              bigserial PRIMARY KEY
  token           uuid UNIQUE NOT NULL                  -- vai na URL
  org_id          bigint NOT NULL REFERENCES orgs(id) ON DELETE CASCADE
  invited_email   varchar(320) NOT NULL                 -- email previsto do convidado
  invited_by_user_id bigint NOT NULL REFERENCES users(id)
  role            varchar(16) NOT NULL DEFAULT 'member'
  created_at      timestamptz NOT NULL DEFAULT now()
  expires_at      timestamptz NOT NULL                  -- created_at + 7 days
  redeemed_at     timestamptz                           -- nullable
  redeemed_by_user_id bigint REFERENCES users(id)
  INDEX        idx_invitations_org (org_id)
```

**Notas de modelagem:**

- **`scans` é a tabela principal.** `Job` V0 é fundido em `scans` com `org_id IS NULL` para fluxo anônimo. Não há tabela `jobs` separada — simplifica o `JobWorker`, mantém uma única fila.
- **`rate_limits` V0** continua existindo em Postgres como tabela auxiliar (mesmo schema: `(ip, day, count)` PK composta). Aplica-se ao fluxo anônimo. Logged-in não tem rate_limit por IP (o limit é por org/plan, V1.1).
- **`audit_log` foi cortado do V1.0 pelo PO.** Trail de quem disparou o quê fica implícito em `scans.triggered_by_user_id` + `repos.added_by_user_id` + `invitations.invited_by_user_id`. Audit log dedicado entra V1.1 quando piloto pedir.
- **Soft delete: não.** V1.0 usa hard delete (`ON DELETE CASCADE`). Se piloto demandar "histórico mesmo após repo removido", introduz `deleted_at` timestamp em V1.1.
- **`repos.scan_config` JSONB**: estrutura inicial `{ "rules_disabled": ["LNTY-008"], "fail_on_grade": "D" }`. Validado contra schema fixo no `RepoService` antes de persistir. Sem migrations a cada toggle novo — JSONB absorve.

### 3.3 Migração SQLite → Postgres — clean cut

**Decisão: clean cut.** SQLite é descartado. Postgres é a única fonte de verdade desde o primeiro deploy V1.0.

**Justificativa:**

- **Dual-write é overhead caro.** Coordenação entre dois stores síncronos exige distributed transaction ou eventual consistency — nenhum dos dois cabe em "fundador solo, 1-2 semanas por sprint".
- **Dados V0 são efêmeros por design.** `expires_at = created_at + 24h`. Migrar dados que vão expirar em 24h é desperdício. Anúncio simples no `/inspect`: "scans anônimos expiram em 24h; dados anteriores ao upgrade não são preservados".
- **Schema Postgres é superset.** Toda info que cabia em SQLite (`jobs.id`, `github_url`, `status`, ...) cabe em `scans` com `org_id IS NULL`. Reaproveitamento de campo é trivial.

**Plano de migração (Sprint 1):**

1. Adicionar provider Npgsql + connection string em `appsettings.json` (ou env var `LINTTY_DB__CONNECTION`).
2. Adicionar EF Core + DbContext (`LinttyDbContext`) com schemas Identity + tabelas custom.
3. Gerar migration inicial (`dotnet ef migrations add InitialCreate`).
4. Substituir `JobStore` SQLite por `PostgresJobStore` (mesma `IJobStore` interface).
5. Remover dependência de `Microsoft.Data.Sqlite` do csproj.
6. Atualizar `JobStorageOptions.DatabaseFile` → ignorado; trocar por `JobStorageOptions.ConnectionString` ou ler via `IConfiguration["ConnectionStrings:Postgres"]`.
7. **Suíte V0 continua passando contra Postgres** — `WebApplicationFactory` em testes substitui connection string por `Testcontainers.PostgreSql` ou Postgres rodando em CI. Decisão de teste deferida para `[qa-engineer]` resolver no Sprint 1 (Testcontainers é o caminho default; alternativa é `Npgsql` + dropar/recriar schema entre testes).

**Compat com smoke `dotnet run` local:** Postgres em Docker Compose `engine/src/Lintty.WebInspector/docker-compose.yml` com Postgres 16. Único arquivo novo de infra. Dev sem Docker → README aponta para Postgres local instalado.

**Sem feature flag.** A migração é binária: build pré-Sprint-1 = SQLite; build pós-Sprint-1 = Postgres. Não há janela em que coexistem.

### 3.4 Reuso JobStore/JobWorker — `IJobStore` formaliza, `JobWorker` intacto

**Decisão:** `IJobStore` (que já existe como interface em `engine/src/Lintty.WebInspector/Jobs/JobStore.cs:26`) ganha um único conjunto de métodos compatível com Postgres. `JobStore` (SQLite, classe atual) é **deletado**. `PostgresJobStore` (novo) é a única implementação.

**Por que descartar SQLite e não manter como duas implementações:**

- **Tentação descartada:** "deixar `SqliteJobStore` para dev local sem Docker". Custa manter dois caminhos de teste, dois SQLs paralelos, dois behaviors de tipos (`timestamptz` vs `TEXT ISO`). Pelo princípio de simplicidade do PO, **uma implementação só**. Dev local sem Docker → instala Postgres ou usa Docker Compose.
- Single implementation = single source of truth para queries.

**Mudanças concretas em `IJobStore`:**

```csharp
// EXISTENTE (vai ficar) — methods já presentes em JobStore.cs:26
Task InitializeAsync(CancellationToken ct);          // schema bootstrap (no-op em Postgres se já migrado)
Task InsertJobAsync(Job job, CancellationToken ct);  // será InsertScanAsync + tabela scans
Task<Job?> GetAsync(string jobId, CancellationToken ct);
Task UpdateAsync(Job job, CancellationToken ct);
Task<Job?> ClaimNextQueuedAsync(CancellationToken ct);
Task<int> CountActiveAsync(CancellationToken ct);
Task<int> IncrementRateLimitAsync(string ip, string day, CancellationToken ct);
Task<int> GetRateLimitCountAsync(string ip, string day, CancellationToken ct);

// NOVOS para multi-tenant (V1.0)
Task<Scan> InsertScanAsync(Scan scan, CancellationToken ct);  // org-bound ou anônimo
Task<IReadOnlyList<Scan>> ListScansForRepoAsync(long repoId, int limit, CancellationToken ct);
Task<Scan?> GetScanByPublicIdAsync(Guid publicId, CancellationToken ct);
```

**`JobWorker` (`Jobs/JobWorker.cs`) — zero mudança lógica.**

- Continua sendo `BackgroundService`.
- Continua chamando `_store.ClaimNextQueuedAsync` em loop com poll a cada 500ms.
- Continua invocando `EngineSubprocessRunner` e `GitCliClient`.
- A única diferença: `Job` (POCO) é renomeado para `Scan` no domínio multi-tenant. Internamente o worker trata um `Scan` exatamente como tratava um `Job` — só ganha campos extras (`org_id`, `repo_id`, `triggered_by_user_id`, `canon_version`).

**`Job` POCO migration:** `Jobs/Job.cs` é renomeado para `Scan` em PR de migração. Para preservar URL legada (`/api/jobs/{id}` continua respondendo), o termo "job" sobrevive **apenas no contrato HTTP V0** (ver §5 e §10 — endpoint `/api/jobs` é mantido como alias de `/api/scans` por uma versão).

**Gate de regressão V0:** `WorkerIntegrationTests.Saint_*` e `Sinner_*` continuam passando contra Postgres. Não há mudança no comportamento observável das rotas V0 — só schema interno e store backend.

### 3.5 Multi-tenancy — disciplina de aplicação, sem RLS

**Decisão:** filtragem por `org_id` em **todas as queries via repository pattern**. Middleware `TenantContextMiddleware` injeta `OrgId` em `HttpContext.Items["OrgId"]` após `AuthenticationMiddleware`. Repositories (`RepoService`, `ScanService`) recebem `IOrgContext` por DI e filtram automaticamente.

**Por que NÃO Postgres Row-Level Security (RLS) no V1.0:**

- Overkill para o perfil de uso V1.0 (1 user = 1 org típico, owner único, ataques esperados são "cookie cruzado", não "DBA mal-intencionado").
- RLS exige conexão Postgres autenticada com role por tenant — incompatível com pool de conexões single-user que o EF Core usa por default. Dá pra fazer com `SET LOCAL app.current_org_id = $1` antes de cada query, mas é fricção a cada repository call.
- Disciplina de aplicação + testes de isolamento (cookie A → 404 em recurso B) cobre os ataques realistas.
- Reabrível em V1.2 se compliance demandar (SOC 2 Type II costuma pedir defense-in-depth).

**Padrão de filtragem (canônico):**

```csharp
// IOrgContext é resolvido por scope; lê HttpContext.Items["OrgId"]
public class ScanService(LinttyDbContext db, IOrgContext orgCtx)
{
    public Task<Scan?> GetByPublicIdAsync(Guid publicId, CancellationToken ct)
        => db.Scans
             .Where(s => s.PublicId == publicId && s.OrgId == orgCtx.OrgId)  // ALWAYS
             .FirstOrDefaultAsync(ct);
}
```

**Caso anônimo (`/inspect`):** `OrgId` é `null` em `HttpContext.Items`. Queries para fluxo anônimo usam um service distinto (`AnonymousScanService`) que filtra `Scan.OrgId IS NULL`. Sem cross-contamination.

**404 vs 403 — decisão:**

- Cookie da Org A pedindo `/api/scans/{publicId}` onde `publicId` pertence à Org B → **404 not_found**.
- Justificativa: 403 confirma existência do recurso (vetor de enumeração — atacante tenta UUIDs e mapeia o que existe). 404 não confirma. Custo zero de implementação (a query simplesmente não retorna).
- 401 só é emitido quando não há cookie válido (não autenticado). Nesse caso, 401 → redirect para login pelo front.

**Teste obrigatório (qa-engineer, Sprint 3):**

```text
TenantIsolationTests.Cookie_OrgA_Cannot_See_OrgB_Scan        → 404
TenantIsolationTests.Cookie_OrgA_Cannot_See_OrgB_Repo         → 404
TenantIsolationTests.Cookie_OrgA_Cannot_Trigger_Scan_OnRepoB  → 404
TenantIsolationTests.Anonymous_Cannot_See_OrgScan             → 404
TenantIsolationTests.OrgUser_Cannot_See_AnonymousScan_Of_OtherIp → 404
```

### 3.6 OAuth GitHub — fluxo e linking

**Decisão:** scopes mínimos `read:user user:email`. Sem `repo`. Sem `read:org`.

**Razão dos scopes:**

- `read:user` — necessário para `GET /user` (id, login, name, avatar).
- `user:email` — necessário para `GET /user/emails` (email principal verificado, cobre o caso "email primário não é público").
- **`repo` (read access) NÃO é pedido no V1.0.** Repo privado fica V1.1. Pedir scope mais amplo do que precisamos = sinal vermelho de privacy review.

**Fluxos:**

| Cenário | Comportamento |
|---------|---------------|
| Primeiro login GitHub, email não existe em `users` | Cria `User` (sem senha local) + `Org` (slug = `gh-{login}`) + `OrgMember(role=owner)` + `external_logins(provider='github', provider_user_id=<id>)`. Loga (cookie). |
| Primeiro login GitHub, email **existe** em `users` (signup prévio com email/senha) | **Vincula** automaticamente: cria `external_logins` apontando para o user existente. Loga. **Decisão:** vincula sem confirmação, porque GitHub já validou o email. Riscos cobertos: GitHub sempre retorna emails verificados em `user:email`. |
| Login GitHub subsequente | `external_logins.lookup(provider, provider_user_id)` → `User` → cookie. Email não é fonte da verdade aqui (user pode mudar email no GitHub). |

**Callback URL:**

- Dev: `http://localhost:5180/api/auth/github/callback`
- Prod: `https://lintty.com/api/auth/github/callback` (decidido quando deploy entrar)

**`state` parameter:** HMAC-SHA256 do `csrf_token` armazenado em cookie de sessão temporária. Sem proteção CSRF aqui = vetor de OAuth login fixation. Implementação default do `AspNet.Security.OAuth.GitHub` já cobre.

**Token GitHub do usuário:** **descartado após o callback.** Não persistido. V1.0 não faz nada com o token (não clona repos privados em nome do user).

### 3.7 Determinismo preservado — gate cruzado novo

**Invariante:** scan disparado pelo dashboard produz PDF byte-idêntico ao scan CLI executado contra o mesmo `(github_url, ref, canon_version)`.

**Fluxo:**

1. `POST /api/scans` (com cookie de Org) recebe `repo_id` + `ref` (opcional).
2. `ScanService.TriggerAsync`:
   - Lê `repo` da DB.
   - Resolve `canon_version` da `ScanConfig` do repo (default: latest pinado no `appsettings.json`, V1.0 = `1.0.0`).
   - Cria `Scan` com `status=queued`, `canon_version` snapshot, `org_id`, `repo_id`, `triggered_by_user_id`.
3. `JobWorker` (mesmo do V0) faz claim, chama `EngineSubprocessRunner` com **exatamente os mesmos args** que `/inspect` anônimo usaria.
4. PDF e JSON salvos via `IArtifactStore`.

**Por que o gate cruzado é não-trivial:**

`Scan.canon_version` é gravado **no momento do trigger** (não no momento do build do worker). Se canon `1.0.0` é pinado hoje e amanhã o canon vira `1.1.0`, o scan que ficou queued ontem tem que rodar com `1.0.0`. Ou seja: `EngineSubprocessRunner` precisa receber `--canon-version <Scan.CanonVersion>`. Verificar se essa flag já está sendo passada no V0 — se não, é parte deste sprint.

**Gate de teste novo (Sprint 3, qa-engineer):**

```text
WorkerIntegrationTests.DashboardScan_Saint_Pdf_Equals_CliDirect
  1. Cria User + Org + Repo apontando para fixture local (FixtureCopyGitClient, like V0).
  2. Loga (cookie).
  3. POST /api/scans { repo_id }.
  4. Espera completed (poll com timeout).
  5. Baixa PDF via GET /api/scans/{public_id}/laudo.pdf.
  6. Roda CLI direto: `lintty-engine analyze --target fixtures/the-saint/Saint.sln --canon-version 1.0.0 --pdf out.pdf`.
  7. Asserta sha256(PDF dashboard) == sha256(PDF CLI).

WorkerIntegrationTests.DashboardScan_Sinner_Pdf_Equals_CliDirect       (idem)
WorkerIntegrationTests.DashboardScan_PinnedCanon_Old_Survives_NewCanon (regression: scan queued com 1.0.0 não muda quando canon avança)
```

Gate cruzado V0 (`Saint_*`, `Sinner_*` em `WorkerIntegrationTests`) **continua válido** — scan anônimo via `/inspect` ainda existe. Ganho aditivo, não substituição.

### 3.8 Storage de PDFs — abstração `IArtifactStore` + retenção configurável

**Decisão:** abstrair acesso a artefatos atrás de `IArtifactStore`. Duas implementações desde V1.0:

- `LocalArtifactStore` — escreve em `var/lintty/jobs/<scan_public_id>/laudo.pdf` (preserva layout V0). Default em dev.
- `S3ArtifactStore` — placeholder de interface, **implementação stub no V1.0** (retorna `NotImplementedException` se config aponta para `s3://`). Implementação real entra em V1.1 quando deploy em cloud entrar.

**Por que abstrair desde V1.0 mesmo sem implementação cloud:**

- Custo de abstrair é meia hora. Custo de não abstrair e refatorar 30 chamadas a `File.ReadAllBytesAsync(job.PdfPath)` em V1.1 é dia inteiro.
- Interface `IArtifactStore { Stream Open(...); Task Save(...); Task Delete(...); Uri? GetSignedUrl(...) }`.
- Endpoints `/api/scans/{id}/laudo.pdf` e `/api/scans/{id}/report.json` consultam `IArtifactStore.Open` em vez de `File.ReadAllBytesAsync`.

**Retenção:**

- **Default V1.0 configurável** via `appsettings.json`:
  - `Storage:RetentionDaysAnonymous` = 1 (24h) — fluxo `/inspect` legado.
  - `Storage:RetentionDaysOrgBound` = 90 — scans logados.
- **Hosted service `RetentionCleanupService`** (deferido para V1.1 se piloto não exigir agora — V1.0 deixa expirar mas não apaga proativamente; cron/manual ok). PR atual tem o serviço com TODO no body se PO quiser commitar adiantado.

**`expires_at` continua gravado no scan no momento da conclusão.** Frontend usa para mostrar "expira em X dias".

**Decisão de retenção valor concreto fica em aberto** — PO marcou como "pré-requisito comercial". `[product-owner]` define em conversa com `[security-compliance]` antes de Sprint 5. Estrutura está pronta; valor é toggle.

### 3.9 Secret manager para env vars do scan

**Decisão V1.0:** **não há env vars sensíveis.** V1.0 só processa repo público — sem `nuget.config` privado, sem connection string customizada, sem token de scan custom.

`repos.scan_config` JSONB aceita só toggles **públicos**:

```json
{
  "rules_disabled": ["LNTY-008"],
  "fail_on_grade": "D",
  "target": "src/MyCompany.sln"
}
```

Validação no `RepoService` antes de persistir:

- `rules_disabled` ⊆ regras canônicas conhecidas. Hard locks (`LNTY-001`, `LNTY-002`, `LNTY-007`) **não suprimíveis** mesmo via toggle (preserva princípio #6).
- `fail_on_grade` ∈ {A, B, C, D, F}.
- `target` ∈ string-livre (validado pelo motor via `TargetResolver` — ADR 0006).

**Quando V1.1 introduzir env vars sensíveis (NuGet privado, GitHub PAT salvo por repo):**

- Coluna nova `repos.encrypted_secrets` (BYTEA). Encriptação AES-GCM com chave ambient via `IDataProtectionProvider` do ASP.NET Core (pasta de chaves em `var/lintty/data-protection/` em dev, KMS-backed em prod via `[backend-dev-cloud]`).
- Decisão deferida para `[security-compliance]` ratificar quando V1.1 entrar.

**Pendência V1.0 marcada explicitamente:** se algum piloto pedir repo privado antes de V1.1 estar pronto, a saída é "use o CLI Self-Service (ADR 0005), código não sai do equipamento". Esse fallback já é o argumento de venda do produto.

### 3.10 Convite por link mágico — UUID + tabela

**Decisão:** UUID v4 como token + tabela `invitations` (já no schema §3.2). Sem JWT, sem HMAC, sem assinatura — porque o token vive em DB e a expiração é explícita.

**Fluxo:**

1. `POST /api/orgs/{org_id}/invitations { email, role }` (autorizado: owner da org).
   - Cria row em `invitations` com `token = NEWID()`, `expires_at = now() + 7 days`.
   - Retorna `{ invitation_url: "https://lintty.com/invite/{token}" }`.
   - Owner copia link e cola em chat/email manualmente. **Sem SMTP.**
2. Convidado clica → `GET /invite/{token}` (front-end).
   - Front chama `GET /api/invitations/{token}` para validar (não-redeemed, não-expirado).
   - Se token válido E user já logado: `POST /api/invitations/{token}/redeem` → cria `OrgMember`, marca `redeemed_at`, redireciona para `/org/{slug}`.
   - Se token válido E user não logado: front mostra "Faça login para aceitar o convite" → após login, retoma o redeem.
   - Se token inválido (não existe ou expirou): 404 / 410.

**Por que não JWT/HMAC:**

- DB lookup já é necessário (precisa marcar `redeemed_at`). Adicionar HMAC só serve se quiser stateless pre-validation — não é o caso.
- UUID v4 = 122 bits de entropia. Não-enumerável. Suficiente para link compartilhável.

**Reuso após redeem:** `redeemed_at` setado → token vira inválido. Owner gera novo convite se precisar. Não há "convite re-utilizável" no V1.0.

**Email do convite vs email do user:** validação solta. Token vincula à `org_id` + `role`, não exige que o user logado tenha o email igual ao `invited_email`. Isso facilita fluxo "owner manda link para alice@gmail.com mas alice usa GitHub login com alice@empresa.com". Trade-off aceito: link compartilhado por canal inseguro pode ser usado por terceiro. Mitigação: TTL 7 dias + token único.

## 4. Consequências

### Ganhos

- **Aquisição → recorrência sem fricção.** Quem já testou `/inspect` cria conta e vira retido sem precisar instalar CLI.
- **Histórico de scans = baseline para conversa de venda.** "Olha aqui, score subiu 10 pontos em 2 sprints" é argumento muito mais forte que um PDF avulso.
- **Reuso de 100% do motor.** Princípio #1 intacto. Mesmo binário, mesmo PDF, mesmo determinismo.
- **Postgres preparado para crescer.** Schema 7 tabelas é simples, índices certos, espaço para V1.1 sem refactor grande (audit_log, branding, billing real, RBAC granular).
- **Sem LLM, sem GCP completo, sem PAdES.** Fica fiel ao stack V0 estendido — não reabre decisões já tomadas.

### Doloridos

- **Migração SQLite → Postgres é 1 sprint inteiro.** Suíte V0 (17 testes) tem que continuar verde contra Postgres antes de qualquer feature dashboard ser construída. Sem atalho.
- **Identity + EF Core + Npgsql adiciona ~3 dependências NuGet pesadas.** Build size do `Lintty.WebInspector` cresce.
- **OAuth GitHub callback URL vira pendência de deploy.** Em dev é localhost; em prod precisa domínio definido. Coordenação com `[backend-dev-cloud]` quando "go" de deploy vier.
- **`/inspect` anônimo passa a gravar em Postgres.** Single point of failure: se Postgres cai, `/inspect` cai junto. Mitigação: graceful degradation que retorna 503 com link para CLI Self-Service (ADR 0005).

### Adia

- **SMTP transacional** — V1.1 (convite por email automático).
- **RBAC granular** (Admin, Maintainer, Viewer) — V1.1.
- **Webhook GitHub auto-trigger** — V1.1.
- **Diff entre scans (delta de violações)** — V1.1.
- **Branding por org** (logo no PDF) — V1.1.
- **Repo privado** — V1.1 com PAT salvo cifrado.
- **Multi-org por user** — V1.1.
- **Stripe + billing real** — V2 (até lá: TED + NF-e manual).
- **Audit log dedicado** — V1.1 ou V1.2.
- **2FA** — V1.2 ou V2.
- **SOC 2 / SAML / SSO** — V2+.

### Pendências (vira PR ou doc separado)

- Atualização de `docs/13-web-inspector.md` §4 ("Arquitetura técnica") + §12 ("Estado de implementação V0") refletindo que o componente agora também serve dashboard. PR de docs **separado** do PR de código (drift se ADR ainda mudar em revisão).
- `docs/00-onde-estamos.md` ganha bullet "V1+ Dashboard em construção" mantendo V0 como verdade operacional.
- Política de privacidade (`landing/privacidade.html`) atualizada: trata de scans org-bound (90 dias) além de anônimos (24h).
- ADR 0008 futuro vai cobrir deploy hosting (`[backend-dev-cloud]`).
- ADR 0009 futuro vai cobrir SMTP + email-based recovery quando V1.1 entrar.

## 5. Alternativas consideradas

### 5.1 Serviço HTTP separado (`lintty-dashboard` standalone)

**Tentação:** separation-of-concerns puro; dashboard escala diferente do Web Inspector; deploy independente.

**Vetado pelo founder em conversa de fechamento.** Justificativa preservada aqui:

- **2 serviços = 2 deploys, 2 monitoring, 2 suítes de teste.** Founder solo. Custo operacional dobra.
- **Compartilhamento real é grande** (Postgres, JobWorker, EngineSubprocessRunner, GitCliClient, Ulid, gate de determinismo). Fronteira artificial cria duplicação.
- **Compõe-se em V1.2+** se carga justificar (ex: dashboard separado em Cloud Run, worker em VM dedicada). Refactor "1 serviço → 2" é caminho natural; "2 → 1" é doloroso. Evolutivo, não arquitetural-de-cara.

### 5.2 Supabase / Auth0 / Clerk para auth

**Tentação:** signup/login/OAuth/recovery/email — tudo "de graça", front-end pega SDK pronta.

**Vetado:**

- **Lock-in.** Migrar Auth0 → custom Identity meses depois é doloroso (schema deles, opinionado em campos).
- **Custo.** Auth0 grátis até 7000 MAU, depois US$ 240/mês para 10000. Supabase pricing também escala. V1.0 não tem caixa para terceirizar comodity.
- **Privacy positioning.** Lintty vende "código não sai do equipamento" — terceirizar identidade do cliente para vendor americano dilui o argumento, mesmo que tecnicamente inofensivo.
- **`Microsoft.AspNetCore.Identity` é o caminho .NET nativo** e cobre 100% dos requisitos V1.0 + V1.1 sem pagar por isso. Reabrir só se V2 trouxer demanda real (SSO empresarial, MFA serio, compliance).

### 5.3 MongoDB / DynamoDB no lugar de Postgres

**Tentação:** "schema flexible para V1.0, decide tabela quando precisar".

**Vetado:**

- **Modelo é relacional.** Org → Members → Repos → Scans são N:M e 1:N clássicos. JOINs são frequentes (mostrar histórico = scan + repo + user). Document store força denormalização que degrada quando V1.1 trouxer audit log.
- **Ferramental .NET para Postgres é maduro** (EF Core + Npgsql). MongoDB driver para .NET é OK mas tem mais arestas (LINQ subset, transactions multi-doc são limitadas).
- **Postgres é o "default boring" certo.** Lintty vende rigor — DB rigoroso bate com a tese.

### 5.4 SQLite continua, troca o file lock por WAL distribuído

**Tentação:** "já está rodando, só estende; SQLite + Litestream replica para S3".

**Vetado:**

- **Multi-tenant exige queries com JOIN multi-tabela em runtime hot path.** SQLite faz, mas custa (sem paralelismo intra-query, sem index hints).
- **Sem extensions Postgres** (`uuid-ossp`, `pg_stat_statements`, `pgcrypto`) — todas úteis a partir de V1.1.
- **Backups e recuperação ponto-no-tempo** são commodity em Postgres managed; SQLite + Litestream é gambiarra sustentável só até ~50 users.

### 5.5 Multi-tenancy por database separado por org

**Tentação:** isolamento absoluto, "compliance check" óbvio, sem disciplina de filtragem.

**Vetado:**

- **N orgs = N databases = N migrations a cada release.** Custo operacional explode.
- **Convite cross-org / multi-org user** vira complicação séria (auth tem que saber qual DB consultar).
- **Cabe num único DB com `org_id` em todas as tabelas.** Bilhões de rows? Sim, single DB Postgres aguenta milhões de scans por org. V1.0 não precisa nem suar.
- **Reabrível em V2+** se compliance exigir (alguns clientes enterprise requerem "data residency por tenant"), mas com schema-per-tenant via Postgres schemas, não com N databases.

## 6. Pendências e riscos abertos

| # | Item | Bloqueia o quê | Responsável | Quando endereçar |
|---|------|----------------|-------------|------------------|
| 1 | **LGPD: classificação de dado pessoal em `users`, `org_members`, `scans.client_ip`.** | Política de privacidade publicada antes do go-live. | `[security-compliance]` | Antes do Sprint 5 (release V1.0). |
| 2 | **Retenção: valor concreto** (anônimo 24h e org-bound 90d são default — confirmar comercial). | Texto da política de privacidade + UX que mostra "expira em X". | `[product-owner]` + `[security-compliance]` | Antes do Sprint 5. |
| 3 | **Senha policy:** PBKDF2 default Identity vs Argon2id. | Audit security review pré-piloto. | `[security-compliance]` | V1.0 ok com PBKDF2; revisitar em V1.1 se piloto exigir. |
| 4 | **Recovery de senha sem SMTP.** | Owner que esquece senha → reset manual via DB pelo founder. | `[product-owner]` (decide se é showstopper) | Solução real V1.1 com SMTP. |
| 5 | **Deploy hosting:** Cloud Run vs VM (Hetzner/DigitalOcean). | Ambiente de produção, callback URL OAuth GitHub, secret management. | `[backend-dev-cloud]` | Sprint 6 ou após V1.0 fechar. ADR 0008. |
| 6 | **CSRF em endpoints state-changing.** | Antiforgery token do ASP.NET Core ativo por padrão em formulários; reverificar em `POST /api/*` JSON. | `[backend-dev-dotnet]` | Sprint 1 (auth). |
| 7 | **Rate limit logged-in.** | V1.0 mantém rate limit IP-based só para `/inspect` anônimo. Logged-in não tem cap (UX-friendly mas pode virar abuse). | `[product-owner]` | V1.1 com plan-based quota. |
| 8 | **OAuth GitHub: validação de email primário verificado.** | Se GitHub retorna email não-verificado, NÃO criar user automaticamente — pedir confirmação via outro canal. | `[backend-dev-dotnet]` | Sprint 4 (OAuth). |
| 9 | **Onboarding < 5 min:** scan default toma ~3 min (PO critério aceite p95). Se primeiro scan demora 5 min, UX quebra a promessa. | `[qa-engineer]` mede contra fixtures grandes (200k LoC). | Sprint 5 (validação fim-a-fim). |
| 10 | **Smoke test de migração:** V0 → V1.0 não regride suíte. | Bloqueador de Sprint 1. Se quebra um teste V0, conserta antes de avançar. | `[qa-engineer]` | Sprint 1. |

---

## Apêndice A — Plano de sprints (1-2 semanas cada, founder solo)

Cada sprint tem **gate** que precisa estar verde antes do próximo começar. Sprints não são prazos calendário — são pacotes de entrega coesos.

### Sprint 1 — Foundations: Postgres + Identity esqueleto

**Goal:** SQLite morto. Postgres rodando. Identity instalado. V0 anônimo continua 100% funcional.

- `[backend-dev-dotnet]` adiciona Npgsql + EF Core + AspNetCore.Identity ao `Lintty.WebInspector.csproj`.
- `[backend-dev-dotnet]` cria `LinttyDbContext` com schema Identity + tabelas `orgs`, `org_members`, `repos`, `scans`, `invitations`, `external_logins`, `rate_limits`. Migration inicial.
- `[backend-dev-dotnet]` substitui `JobStore` (SQLite) por `PostgresJobStore`. Renomeia `Job` POCO para `Scan`, mantém alias na rota `/api/jobs` (compat V0).
- `[backend-dev-dotnet]` adiciona Docker Compose com Postgres 16 em `engine/src/Lintty.WebInspector/docker-compose.yml`.
- `[qa-engineer]` substitui setup de teste para usar Testcontainers.PostgreSQL (ou Postgres ambiente CI).
- `[qa-engineer]` valida que **17 testes V0 verdes contra Postgres** (`HealthEndpointTests`, `JobsApiContractTests`, `WorkerIntegrationTests`, `SwaggerDocumentTests`).

**Gate Sprint 1:** suíte V0 verde + `dotnet run` local com Postgres em compose responde no `/inspect` e gera PDF idêntico ao V0 SQLite (gate de determinismo cruzado anônimo continua válido).

### Sprint 2 — Auth: signup, login, sessão, /me

**Goal:** Founder consegue criar conta, logar, deslogar, ver `/api/auth/me`.

- `[backend-dev-dotnet]` configura `AddIdentityCore<User>` + `AddCookie` no DI.
- `[backend-dev-dotnet]` implementa `POST /api/auth/signup`, `POST /api/auth/login`, `POST /api/auth/logout`, `GET /api/auth/me`.
- `[backend-dev-dotnet]` no signup: cria `User` + `Org` (slug derivado de email) + `OrgMember(role=owner)` em transação.
- `[backend-dev-dotnet]` middleware `TenantContextMiddleware` lê cookie → resolve `OrgId` → `HttpContext.Items`.
- `[qa-engineer]` testes: signup OK, signup duplicado → 409, login OK, login senha errada → 401, logout invalida cookie, `/me` sem cookie → 401.

**Gate Sprint 2:** signup + login + logout funcionam ponta-a-ponta. Cookie é HTTPOnly + SameSite=Lax. V0 anônimo continua intacto.

### Sprint 3 — Org, Repo, Scan: o ciclo principal logged-in

**Goal:** User logado adiciona repo, dispara scan, vê histórico, baixa PDF.

- `[backend-dev-dotnet]` `RepoService` + endpoints: `POST /api/repos { github_url }`, `GET /api/repos`, `GET /api/repos/{id}`, `DELETE /api/repos/{id}`. Validação reusa `UrlValidator` + `GitHubMetadataClient` (V0).
- `[backend-dev-dotnet]` `ScanService` + endpoints: `POST /api/scans { repo_id, ref? }` → enfileira; `GET /api/scans/{public_id}`; `GET /api/scans/{public_id}/laudo.pdf`; `GET /api/scans/{public_id}/report.json`; `GET /api/repos/{id}/scans` (histórico).
- `[backend-dev-dotnet]` `IArtifactStore` + `LocalArtifactStore`. Endpoints de download usam `IArtifactStore.Open`.
- `[backend-dev-dotnet]` `JobWorker` agora processa scans org-bound junto com anônimos. `Scan.canon_version` snapshot no trigger.
- `[qa-engineer]` `TenantIsolationTests` (5 testes mínimos de §3.5).
- `[qa-engineer]` `WorkerIntegrationTests.DashboardScan_Saint_Pdf_Equals_CliDirect` (gate determinismo cruzado dashboard ↔ CLI).
- `[qa-engineer]` `WorkerIntegrationTests.DashboardScan_PinnedCanon_Survives_NewCanon` (regressão canon snapshot).

**Gate Sprint 3:** founder loga, adiciona `https://github.com/lintty-demo/the-saint`, clica "Rodar scan", baixa PDF byte-idêntico ao CLI direto. 5 testes de tenant isolation verdes.

### Sprint 4 — OAuth GitHub + Convite por link

**Goal:** signup via GitHub funciona; owner convida member; member loga e entra na org.

- `[backend-dev-dotnet]` integra `AspNet.Security.OAuth.GitHub`. Endpoints `GET /api/auth/github/start`, `GET /api/auth/github/callback`. Email primário verificado lookup.
- `[backend-dev-dotnet]` linking: GitHub email == User existente → vincula `external_logins`. Senão → cria novo User + Org.
- `[backend-dev-dotnet]` `InvitationService`: `POST /api/orgs/{id}/invitations { email, role }`, `GET /api/invitations/{token}`, `POST /api/invitations/{token}/redeem`.
- `[backend-dev-dotnet]` RBAC mínimo: owner pode convidar/remover/ler tudo; member pode trigger scan + ler tudo da org. Implementação: atributo `[RequireOrgRole("owner")]` em endpoints sensíveis.
- `[qa-engineer]` testes OAuth (mock GitHub via WireMock ou stub `IGitHubAuthClient`).
- `[qa-engineer]` testes invitation: criar, redimir, expirar, redimir 2x → 410 (gone).

**Gate Sprint 4:** founder cria org, convida `vinicius+teste@landtech.com.br`, copia link, abre em outra sessão browser, faz signup/login, vê org compartilhada. Login GitHub funciona end-to-end com fixture.

### Sprint 5 — Polimento: billing read-only + onboarding < 5 min

**Goal:** UX coerente. Pronto para piloto controlado.

- `[frontend-dev]` página `/billing` read-only com mocks (PO definir copy: "Plano Beta — 50 scans/mês incluídos · Próxima cobrança: TED em DD/MM").
- `[frontend-dev]` páginas: `/signup`, `/login`, `/orgs/{slug}`, `/orgs/{slug}/repos/new`, `/orgs/{slug}/repos/{id}`, `/orgs/{slug}/repos/{id}/scans/{public_id}`, `/invite/{token}`, `/billing`. Stack V0 (HTML+Tailwind CDN, sem build step) — manter consistência com landing.
- `[qa-engineer]` cronometra onboarding (signup → primeiro PDF baixado) com fixture Saint. Critério aceite: < 5 min.
- `[qa-engineer]` p95 do scan ≤ 3 min para fixture Sinner ou repo público com ~200k LoC.
- `[backend-dev-dotnet]` `RetentionCleanupService` (BackgroundService) opcional — se PO confirmar valor de retenção (pendência #2 §6), commita; senão fica TODO.
- `[security-compliance]` ratifica política de privacidade atualizada e LGPD review.
- `[product-owner]` ratifica todos os critérios de aceite do MVP (8 itens da spec).

**Gate Sprint 5 = Release V1.0:** todos os critérios PO atendidos. Founder convoca primeiro piloto controlado.

### Sprint 6 (opcional, fora do MVP) — Deploy

**Goal:** algo público em `lintty.com/app`.

- `[backend-dev-cloud]` decide hosting (Cloud Run vs VM Hetzner). ADR 0008 abre.
- `[backend-dev-cloud]` Dockerfile multi-stage para `Lintty.WebInspector`. Postgres managed (Cloud SQL ou Hetzner DB).
- `[backend-dev-dotnet]` callback URL OAuth GitHub atualizada para domínio prod.
- `[backend-dev-cloud]` data-protection keys persistentes (não memória ephemeral).
- `[security-compliance]` final review pré-go-live.

**Gate Sprint 6:** founder cria conta em `https://lintty.com/app/signup` real, do navegador da casa dele, sem proxy.

### Caminho crítico

**Sprint 3 é o gargalo.** É onde o gate de determinismo cruzado dashboard ↔ CLI estreia, é onde tenant isolation tem que ser provada com testes, é onde o `JobWorker` migra de "1 modo (anônimo)" para "2 modos (anônimo + org-bound)" sem regressão. Se Sprint 3 quebra, V1.0 não fecha.

Sprint 1 é trabalhoso (migração SQLite → Postgres) mas direto. Sprint 2 é boilerplate Identity. Sprint 4 é integração externa (GitHub OAuth) — risco moderado de "edge case do provedor". Sprint 5 é UX, não tem incógnita técnica grande.

---

## Apêndice B — Mapeamento de arquivos atuais → mudanças

Lista de orientação para o backend-dev-dotnet — identifica arquivos do Web Inspector V0 que tocam neste slice.

| Arquivo atual (V0) | Mudança em V1.0 |
|--------------------|-----------------|
| `engine/src/Lintty.WebInspector/Program.cs` | Adiciona DI: `LinttyDbContext`, Identity, OAuth GitHub. Remove `Microsoft.Data.Sqlite` config. Mantém Swagger ligado em prod (tag novos endpoints). |
| `engine/src/Lintty.WebInspector/Configuration/JobStorageOptions.cs` | `DatabaseFile` removido. Connection string vem de `IConfiguration["ConnectionStrings:Postgres"]`. |
| `engine/src/Lintty.WebInspector/Jobs/JobStore.cs` | **Substituído por** `PostgresJobStore` (EF Core ou Dapper — decisão local, EF preferido). Interface `IJobStore` ganha métodos novos (§3.4). |
| `engine/src/Lintty.WebInspector/Jobs/Job.cs` | Renomeado para `Scan`. Campos novos: `OrgId`, `RepoId`, `TriggeredByUserId`, `CanonVersion`, `PublicId`, `HashContent`. |
| `engine/src/Lintty.WebInspector/Jobs/JobWorker.cs` | Zero mudança lógica. Usa `IJobStore` por DI. `Scan.CanonVersion` é repassado para `EngineSubprocessRunner`. |
| `engine/src/Lintty.WebInspector/Jobs/EngineSubprocessRunner.cs` | Adiciona `--canon-version` na chamada CLI. Verificar se já está sendo passado. |
| `engine/src/Lintty.WebInspector/Endpoints/JobsEndpoints.cs` | Mantido (compat V0 `/api/jobs`). Novos endpoints em arquivos separados: `AuthEndpoints.cs`, `OrgsEndpoints.cs`, `ReposEndpoints.cs`, `ScansEndpoints.cs`, `InvitationsEndpoints.cs`. |
| `engine/src/Lintty.WebInspector/Validation/UrlValidator.cs` | Reuso. |
| `engine/src/Lintty.WebInspector/Validation/GitHubMetadataClient.cs` | Reuso. Eventualmente recebe token do user logado para checagem extra (V1.1). |
| `engine/src/Lintty.WebInspector/Jobs/Ulid.cs` | Reuso (continua usado para `public_id` UUID v4 — wait, conflito). **Decisão:** `Scan.public_id` é UUID v4 (`Guid.NewGuid()`) **não** ULID, porque `Guid` é nativo Postgres (`uuid` column). ULID `Ulid.cs` continua existindo para outros usos internos. |
| `engine/tests/Lintty.WebInspector.Tests/WorkerIntegrationTests.cs` | Adiciona testes `DashboardScan_*` (§3.7). V0 testes mantidos. |

---

**Próxima decisão (após este ADR aceito):** founder ratifica → backend-dev-dotnet abre PR Sprint 1 (Postgres + Identity install + clean cut SQLite). Em paralelo, qa-engineer prepara setup Testcontainers.PostgreSQL e mantém suíte V0 verde como gate de regressão.

---

## Resumo executivo (≤200 palavras)

**Decisões mais importantes:**

1. **Estende o `Lintty.WebInspector` V0** — não cria serviço separado. Mesmo binário ganha 5 grupos de rotas novos (auth/orgs/repos/scans/invitations) e middleware de tenant context. Reuso de 100% do `JobWorker`, `EngineSubprocessRunner`, `GitCliClient`.
2. **Clean cut SQLite → Postgres no Sprint 1.** Sem dual-write, sem feature flag. Suíte V0 (17 testes) é o gate de regressão; precisa estar verde contra Postgres antes de qualquer feature dashboard começar.
3. **Auth via `AspNetCore.Identity` + cookie server-side + OAuth GitHub.** Sem Auth0/Supabase (lock-in + custo). Sem JWT (revogação trivial). PBKDF2 default (suficiente V1.0).
4. **Multi-tenancy por disciplina de aplicação**, não Postgres RLS. Filtro `org_id` em todo repository. Cookie cruzado → 404 (não 403).
5. **Determinismo preservado:** `Scan.canon_version` snapshot no trigger; gate cruzado novo `DashboardScan_Saint_Pdf_Equals_CliDirect` em `WorkerIntegrationTests`.

**Pendências em aberto:**

- LGPD + retenção concreta (PDF anônimo 24h, org 90d são default — `[security-compliance]` + `[product-owner]` ratificam).
- Deploy hosting (Cloud Run vs VM) → `[backend-dev-cloud]` resolve em ADR 0008 quando "go" comercial vier.
- Senha policy (PBKDF2 vs Argon2id) → `[security-compliance]` revisita V1.1 se piloto exigir.

**Caminho crítico do plano:** **Sprint 3** é o gargalo — onde tenant isolation, gate cruzado dashboard↔CLI, e fusão "1 modo de scan → 2 modos" estreiam todos juntos no `JobWorker`. Sprints 1, 2, 4 são lineares; Sprint 5 é UX.

---

## Apêndice C — Sprint 1 — Postgres cut (entregue 2026-05-06)

Anexa-se à ADR sem reabrir as decisões; documenta o **como** do cut SQLite → Postgres já decidido em §3.3 e §3.4.

### Decisões locais tomadas pelo `[backend-dev-dotnet]`

1. **Npgsql + Dapper, não EF Core no Sprint 1.** O `IJobStore` tem 8 métodos com SQL hand-written; `ClaimNextQueuedAsync` virou `UPDATE ... WHERE id = (SELECT ... FOR UPDATE SKIP LOCKED) RETURNING *` num único round trip — expressão direta em Dapper, fricção em EF (precisaria `FromSqlRaw` de qualquer jeito). EF Core entra em Sprint 2 quando `AspNetCore.Identity` for adicionado: os dois stacks coexistem na mesma Postgres connection — EF para tabelas Identity (schema-managed, opinionado), Dapper para o domínio Lintty (queries hand-tuned). Padrão canônico .NET; não há conflito.
2. **Schema bootstrap via SQL embedded + `IF NOT EXISTS`**, não `dotnet ef migrations`. Sprint 1 mantém só `jobs` + `rate_limits` (paridade 1:1 com SQLite) — uma migration EF para isso é overhead. Quando Sprint 2 trouxer `LinttyDbContext` para Identity + tabelas multi-tenant (orgs, repos, scans, etc), o caminho é: (a) gerar migration inicial cobrindo Identity + multi-tenant; (b) opcionalmente absorver `jobs`/`rate_limits` na migration ou deixar como "pre-EF schema" (decisão local de quem fizer Sprint 2).
3. **Testcontainers.PostgreSql, container compartilhado por assembly + TRUNCATE entre testes.** Container per-class era 5x mais lento; container shared sem reset gera flakiness no `Rate_Limit_Triggers_429_On_Fourth_Post_From_Same_Ip` (acumula contagem). `PostgresFixture : IAsyncLifetime` + `[Collection("Postgres")]` + `WebInspectorTestBase.ResetAsync` no início de cada `[Fact]` é o ponto de equilíbrio. tmpfs no `/var/lib/postgresql/data` evita I/O do host.
4. **Cut absoluto.** `JobStore.cs` SQLite deletado; `Microsoft.Data.Sqlite` removido de `Directory.Packages.props` e do csproj. Arquivo `var/lintty/jobs.sqlite*` apagado. Sem feature flag, sem fallback, sem dual write — exatamente o que §3.3 prescreveu.

### Arquivos tocados

| Arquivo | Ação |
|---------|------|
| `engine/Directory.Packages.props` | Remove `Microsoft.Data.Sqlite`. Adiciona `Npgsql 8.0.5`, `Dapper 2.1.35`, `Testcontainers.PostgreSql 3.10.0`. |
| `engine/src/Lintty.WebInspector/Lintty.WebInspector.csproj` | Troca `<PackageReference Microsoft.Data.Sqlite>` por `<Npgsql>` + `<Dapper>`. Adiciona `<EmbeddedResource Include="Persistence\Schema\001_initial.sql">`. |
| `engine/src/Lintty.WebInspector/Persistence/Schema/001_initial.sql` | **Novo.** Schema Postgres das duas tabelas V0 (`jobs`, `rate_limits`) com idempotência `IF NOT EXISTS`. |
| `engine/src/Lintty.WebInspector/Jobs/IJobStore.cs` | **Novo.** Interface extraída do antigo `JobStore.cs` (sem mudança no contrato — preservada bit-a-bit). |
| `engine/src/Lintty.WebInspector/Jobs/PostgresJobStore.cs` | **Novo.** Substitui `JobStore.cs`. SQL Postgres com `FOR UPDATE SKIP LOCKED` em `ClaimNextQueuedAsync` (race-safe se um dia rodar multi-worker). |
| `engine/src/Lintty.WebInspector/Jobs/JobStore.cs` | **Deletado.** Cut limpo. |
| `engine/src/Lintty.WebInspector/Configuration/PostgresOptions.cs` | **Novo.** Bind do `Postgres:ConnectionString`; doc-comment aponta para o env var `LINTTY_POSTGRES__CONNECTIONSTRING`. |
| `engine/src/Lintty.WebInspector/Configuration/JobStorageOptions.cs` | Remove `DatabaseFile` (não tem mais SQLite). `Root` + `JobsDir` permanecem porque o disco é a única coisa que segura PDF/JSON; `IArtifactStore` (§3.8) entra em Sprint 3. |
| `engine/src/Lintty.WebInspector/Program.cs` | Registra `PostgresOptions`; troca `IJobStore = JobStore` por `IJobStore = PostgresJobStore`; doc-comment atualizado. |
| `engine/src/Lintty.WebInspector/appsettings.json` | Adiciona seção `Postgres.ConnectionString` apontando para o compose local; remove referência a `JobStorage:DatabaseFile`. |
| `engine/src/Lintty.WebInspector/var/lintty/jobs.sqlite*` | **Deletados** (arquivo + WAL + SHM). |
| `engine/docker-compose.yml` | **Novo.** Postgres 16-alpine local para `dotnet run`. Credenciais batem com `appsettings.json`. |
| `engine/README.md` | **Novo.** Como subir o compose, como rodar os testes (Testcontainers só precisa do daemon Docker — não do compose). |
| `engine/tests/Lintty.WebInspector.Tests/Lintty.WebInspector.Tests.csproj` | Adiciona `Testcontainers.PostgreSql`, `Npgsql`, `Dapper`. |
| `engine/tests/Lintty.WebInspector.Tests/PostgresFixture.cs` | **Novo.** Container shared via `IAsyncLifetime` + `[CollectionDefinition]`. `ResetAsync` faz TRUNCATE com guarda `IF EXISTS` (primeira chamada vem antes do schema ter sido aplicado por qualquer fixture). |
| `engine/tests/Lintty.WebInspector.Tests/WebInspectorTestBase.cs` | **Novo.** Base class para todo teste WebInspector — pinada na collection "Postgres", expõe `CreateFactory()` + `ResetAsync()`. |
| `engine/tests/Lintty.WebInspector.Tests/WebInspectorFactory.cs` | Construtor passa a exigir `connectionString` (recebido da fixture compartilhada). Configuração `Postgres:ConnectionString` injetada no DI antes do build. |
| `engine/tests/Lintty.WebInspector.Tests/{Health,Swagger,JobsApiContract,WorkerIntegration}Tests.cs` | Herdam `WebInspectorTestBase`, usam `CreateFactory()` em vez de `new WebInspectorFactory()`. Cada teste chama `ResetAsync()` no início. Lógica de assertion **inalterada**. |

### Determinismo cruzado: validado

`WorkerIntegrationTests.Saint_*`, `Sinner_*`, `SaintNoSln_*` continuam verdes. PDF do Web Inspector segue byte-idêntico ao PDF do CLI direto contra a mesma `.sln`/`lintty.yml` — Postgres não acrescenta timestamps, IDs ou metadados ao payload do laudo (decisão preservada do princípio #2 do system prompt). Schema JSON `1.0` LOCKED, `hash_content` LOCKED.

### Como subir local

```bash
# repo root → sobe Postgres
docker compose -f engine/docker-compose.yml up -d

# engine/ → roda Web Inspector
dotnet run --project src/Lintty.WebInspector
# health: http://localhost:5180/healthz
# swagger: http://localhost:5180/swagger
# UI: http://localhost:5180/inspect.html

# tear down
docker compose -f engine/docker-compose.yml down -v
```

Tests **não dependem** do compose stack — usam Testcontainers contra o daemon Docker direto. CI vai precisar de Docker disponível no runner (já é o padrão em GitHub Actions `ubuntu-latest`).

### Suíte verde no Sprint 1

```text
Lintty.Engine.Cli.Tests        2/2     verde
Lintty.Engine.Core.Tests      16/16    verde
Lintty.Engine.Reporter.Tests  10/10    verde   (gate de determinismo do PDF)
Lintty.WebInspector.Tests     17/17    verde   (gate de determinismo cruzado)
                              ─────
                              45/45    verde, zero warning, zero erro
```

### Pendências geradas (não-bloqueantes)

- **Sprint 2** vai precisar decidir se a migration EF Core inicial absorve `jobs`/`rate_limits` ou deixa como "schema pré-EF" para evitar drift entre o SQL embedded e o snapshot EF. Recomendação: absorver. Custo de manter dois lados é maior que o de uma migration grande.
- **`var/lintty/jobs/` (artefatos PDF/JSON em disco)** continua não-abstraído. `IArtifactStore` é §3.8 e fica para Sprint 3 — Sprint 1 tocaria o caminho do laudo, e isso quebraria o gate de determinismo cruzado por hash de path. Decisão: empurra para depois.
- **Connection string em `appsettings.json` carrega senha em plaintext.** Aceitável em dev (compose com credenciais fixas conhecidas); em prod, `[backend-dev-cloud]` substitui via env var ou secret manager (Sprint 6 / ADR 0008).

## Apêndice D — Sprint 2 — Auth + tenant esqueleto (entregue 2026-05-06)

Anexa-se à ADR sem reabrir as decisões; documenta o **como** do cut Identity + tenant scaffold já decidido em §3.1, §3.2, §3.5 e §3.6.

### Decisões locais tomadas pelo `[backend-dev-dotnet]`

1. **`EFCore.NamingConventions` para snake_case, não Fluent API por coluna.** O ADR §3.2 requer `users`, `display_name`, `org_members(role)` etc. — mapear isso à mão em `OnModelCreating` para 7 tabelas Identity + 3 tabelas custom + 2 tabelas V0 sairia em ~150 linhas de boilerplate. O package oficial do ecossistema Npgsql (`UseSnakeCaseNamingConvention()`) faz a conversão automática de PascalCase→snake_case em nomes de coluna, índice e FK. Os nomes de **tabela** Identity (`AspNetUsers`, etc.) são hard-coded pelo `IdentityDbContext` e não passam pelo conversor — esses ficam por `builder.Entity<User>().ToTable("users")` em `LinttyDbContext.OnModelCreating` (escopo: 7 tabelas Identity, ~14 linhas).

2. **Migration única `InitialIdentityAndTenant` absorve `jobs`/`rate_limits`** (recomendação do Apêndice C #1). EF passa a ser o single source of truth do schema; `Persistence/Schema/001_initial.sql` foi deletado. `PostgresJobStore.InitializeAsync` virou no-op com log de debug — interface preservada bit-a-bit para outras impls (in-memory tests, futuro).

3. **Coexistência EF + Dapper na mesma DB** funciona sem fricção. `LinttyDbContext` modela o schema de `jobs` e `rate_limits` apenas para a migration gerar o DDL correto; `PostgresJobStore` continua lendo/escrevendo via Dapper com SQL hand-tuned (UPDATE...RETURNING + FOR UPDATE SKIP LOCKED em `ClaimNextQueuedAsync`, ON CONFLICT DO UPDATE em `IncrementRateLimitAsync`). Os dois stacks compartilham o mesmo `Postgres:ConnectionString` resolvido por DI — Npgsql faz pooling internamente; não há contenção.

4. **`AspNet.Security.OAuth.GitHub` registrado com placeholder + `IPostConfigureOptions`.** O brief exigia o pacote, mas registrar `AddGitHub(opts => { opts.ClientId = ...; opts.ClientSecret = ...; })` com leitura early-time de `builder.Configuration` quebra o teste com `WebApplicationFactory` (a config in-memory dos testes vence só DEPOIS do `ConfigureServices`). Solução: registra com placeholders, e o binder `GitHubOAuthOptionsBinder : IPostConfigureOptions<GitHubAuthenticationOptions>` lê creds de `IConfiguration` no momento certo. Como a callback real é hand-rolled em `AuthEndpoints.GitHubCallback` (chama `IGitHubOAuthClient` direto), o handler do framework nunca é exercitado — o pacote fica disponível para um futuro switch para a flow oficial sem dependency churn.

5. **DbContext lazy connection string via `(sp, options) => ...`.** Mesmo motivo do binder OAuth: `WebApplicationFactory` adiciona a config in-memory do test fixture através do `IHostBuilder.ConfigureAppConfiguration` em ordem que depende de detalhes do WAF. Resolver `IConfiguration` por DI no momento de criar o `DbContext` é determinístico e funciona em prod, dev e test sem caso especial.

6. **Endpoints de auth são minimal API + handlers async em `AuthEndpoints.cs`** (não controllers MVC). Mantém o estilo dos `JobsEndpoints` do V0; cada handler é um método estático que aceita `IFormFile` ou record body e retorna `IResult`. DTOs (`SignupRequest`, `MeResponse`, `MembershipDto`, `ValidationErrorResponse`) ficam no mesmo arquivo — quando passar de ~5 endpoints novos, divide.

7. **Slug uniqueness via loop de retry.** `EnsureUniqueSlugAsync` consulta `orgs.slug` e tenta `<seed>`, `<seed>-2`, ..., até 1000. Sob race condition (dois signups simultâneos com mesmo nome de org) o índice unique do DB resolve via exception → o segundo retry pega o próximo número. V1.1 pode trocar por advisory lock se virar gargalo; V1.0 não vê esse cenário (founder solo, taxa baixa).

### Arquivos tocados

| Arquivo | Ação |
|---------|------|
| `engine/Directory.Packages.props` | Adiciona `Microsoft.EntityFrameworkCore`, `Microsoft.EntityFrameworkCore.Design`, `Npgsql.EntityFrameworkCore.PostgreSQL`, `Microsoft.AspNetCore.Identity.EntityFrameworkCore`, `EFCore.NamingConventions`, `AspNet.Security.OAuth.GitHub`. |
| `engine/src/Lintty.WebInspector/Lintty.WebInspector.csproj` | Adiciona PackageReference para os 6 pacotes acima. Remove `<EmbeddedResource Include="Persistence\Schema\001_initial.sql">`. |
| `engine/src/Lintty.WebInspector/Persistence/LinttyDbContext.cs` | **Novo.** `IdentityDbContext<User, Role, long>` + `DbSet<Org>`, `<OrgMember>`, `<ExternalLogin>`, `<Job>`, `<RateLimit>`. `OnModelCreating` renomeia tabelas Identity para snake_case e configura FKs/índices das tabelas custom. |
| `engine/src/Lintty.WebInspector/Persistence/Entities/User.cs` | **Novo.** `User : IdentityUser<long>` + `DisplayName`, `CreatedAt`. |
| `engine/src/Lintty.WebInspector/Persistence/Entities/Role.cs` | **Novo.** `Role : IdentityRole<long>` (placeholder para roles globais V1.1). |
| `engine/src/Lintty.WebInspector/Persistence/Entities/Org.cs` | **Novo.** Tenant boundary; `Slug`, `Name`, `OwnerId`. |
| `engine/src/Lintty.WebInspector/Persistence/Entities/OrgMember.cs` | **Novo.** N:M com `Role` string (`OrgRole.Owner|Admin|Member`). |
| `engine/src/Lintty.WebInspector/Persistence/Entities/ExternalLogin.cs` | **Novo.** `(provider, provider_user_id)` único, FK CASCADE para `users`. |
| `engine/src/Lintty.WebInspector/Persistence/Entities/RateLimit.cs` | **Novo.** Composite PK `(ip, day)` modelado para EF gerar a tabela; runtime continua via Dapper. |
| `engine/src/Lintty.WebInspector/Persistence/DesignTimeLinttyDbContextFactory.cs` | **Novo.** `IDesignTimeDbContextFactory<LinttyDbContext>` para `dotnet ef migrations add` / `database update`. Lê `LINTTY_POSTGRES__CONNECTIONSTRING` ou cai no compose default. |
| `engine/src/Lintty.WebInspector/Persistence/Migrations/20260506170527_InitialIdentityAndTenant.cs` (+ Designer + Snapshot) | **Novo.** Migration única com Identity + tenant + V0 jobs/rate_limits. Geração via `dotnet ef migrations add InitialIdentityAndTenant`. |
| `engine/src/Lintty.WebInspector/Persistence/Schema/001_initial.sql` | **Deletado.** Substituído pela migration EF. |
| `engine/src/Lintty.WebInspector/Jobs/Job.cs` | Trocado `init` por `set` em `Id`, `GithubUrl`, `CreatedAt` para EF. POCO permanece compatível com Dapper. |
| `engine/src/Lintty.WebInspector/Jobs/PostgresJobStore.cs` | `InitializeAsync` virou no-op (schema agora é da migration). Remove `LoadEmbeddedSchema` + imports `System.IO`/`System.Reflection`. |
| `engine/src/Lintty.WebInspector/Configuration/GitHubOAuthOptions.cs` | **Novo.** Bind para `Github:ClientId` + `Github:ClientSecret` (env: `LINTTY_GITHUB__CLIENTID`/`__CLIENTSECRET`). |
| `engine/src/Lintty.WebInspector/Auth/ITenantContext.cs` + `TenantContext.cs` | **Novos.** Per-request scope de `OrgId`/`UserId` resolvido via `HttpContext.Items`. |
| `engine/src/Lintty.WebInspector/Auth/TenantContextMiddleware.cs` | **Novo.** Roda depois de Authentication/Authorization, antes dos endpoints. Resolve org via claim ou primeira membership. Anônimo passa intocado. |
| `engine/src/Lintty.WebInspector/Auth/IGitHubOAuthClient.cs` + `GitHubOAuthClient.cs` | **Novos.** Wrapper sobre `github.com/login/oauth/access_token` + `api.github.com/user` + `/user/emails`. Stub-able em testes. |
| `engine/src/Lintty.WebInspector/Auth/GitHubOAuthOptionsBinder.cs` | **Novo.** `IPostConfigureOptions<GitHubAuthenticationOptions>` que injeta creds reais via `IConfiguration` em runtime. |
| `engine/src/Lintty.WebInspector/Auth/SlugGenerator.cs` | **Novo.** ASCII-fold + lowercase + collapse não-alfanum em `-` + truncate 64. Determinístico. |
| `engine/src/Lintty.WebInspector/Endpoints/AuthEndpoints.cs` | **Novo.** Signup, Login, Logout, Me, GitHubStart, GitHubCallback. DTOs no mesmo arquivo (`SignupRequest`, `LoginRequest`, `MeResponse`, `UserDto`, `MembershipDto`, `ValidationErrorResponse`). |
| `engine/src/Lintty.WebInspector/Program.cs` | Wire `AddDbContext` lazy, `AddIdentityCore<User>().AddRoles<Role>().AddEntityFrameworkStores<LinttyDbContext>()`, cookie `lintty_auth` (HttpOnly, Lax, 14 dias sliding), `AddGitHub` placeholder + binder, `AddHttpContextAccessor`, `UseAuthentication`/`UseAuthorization`/`UseMiddleware<TenantContextMiddleware>`/`MapAuth`. `InitializeStorage` agora roda `db.Database.Migrate()` em dev/test (manual em prod). |
| `engine/src/Lintty.WebInspector/appsettings.json` | (sem mudança — Sprint 1 já tinha conn string) |
| `engine/tests/Lintty.WebInspector.Tests/PostgresFixture.cs` | `ApplyMigrationsAsync` substitui o load do SQL embedded — usa `db.Database.MigrateAsync()`. `ResetAsync` faz TRUNCATE em todas as tabelas (Identity + tenant + V0). |
| `engine/tests/Lintty.WebInspector.Tests/WebInspectorFactory.cs` | Adiciona `EnableGitHubOAuth` (injeta `Github:ClientId/Secret` no in-mem config) e `FakeGitHubOAuthClient` swap unconditional. |
| `engine/tests/Lintty.WebInspector.Tests/Fakes/FakeGitHubOAuthClient.cs` | **Novo.** Stub de `IGitHubOAuthClient`. |
| `engine/tests/Lintty.WebInspector.Tests/AuthSignupTests.cs` | **Novo.** 5 testes (happy path, dup email, weak password, missing email, slug collision). |
| `engine/tests/Lintty.WebInspector.Tests/AuthLoginTests.cs` | **Novo.** 4 testes (sucesso, senha errada, user inexistente, /me sem cookie). |
| `engine/tests/Lintty.WebInspector.Tests/OAuthCallbackTests.cs` | **Novo.** 3 testes (503 sem creds, primeiro login, login subsequente reutiliza user). |
| `engine/tests/Lintty.WebInspector.Tests/TenantContextMiddlewareTests.cs` | **Novo.** 3 testes (anonimato preserva V0, `/me` autenticado, logout limpa cookie). |
| `engine/tests/Lintty.WebInspector.Tests/V0RegressionTests.cs` | **Novo.** 3 testes (POST /api/jobs anônimo sem cookie, GET por path token, /healthz). |
| `engine/tests/Lintty.WebInspector.Tests/Lintty.WebInspector.Tests.csproj` | Adiciona `Microsoft.EntityFrameworkCore`, `.Design`, `Npgsql.EntityFrameworkCore.PostgreSQL`, `Microsoft.AspNetCore.Identity.EntityFrameworkCore`, `EFCore.NamingConventions`. |
| `engine/README.md` | Seções "Schema management", "GitHub OAuth", "Environment variables". |

### Determinismo cruzado: validado

`WorkerIntegrationTests.Saint_*`, `Sinner_*`, `SaintNoSln_*` continuam verdes contra a base Postgres já com schema EF. Identity + tenant não tocam o caminho do laudo: o subprocess do CLI é invocado igual ao Sprint 1, recebe o mesmo `.sln`/`lintty.yml`, escreve o mesmo PDF. `hash_content` LOCKED, schema JSON `1.0` LOCKED.

### Como subir local end-to-end (Sprint 2)

```bash
# 1. Postgres dev
docker compose -f engine/docker-compose.yml up -d

# 2. Migration (uma vez)
cd engine
dotnet tool install --global dotnet-ef --version 8.0.10   # se nunca instalou
dotnet ef database update --project src/Lintty.WebInspector

# 3. (Opcional) GitHub OAuth — sem isso, /api/auth/github/start retorna 503
export LINTTY_GITHUB__CLIENTID="Iv1.xxx"
export LINTTY_GITHUB__CLIENTSECRET="xxx"

# 4. Sobe o app
dotnet run --project src/Lintty.WebInspector
# health: http://localhost:5180/healthz
# swagger: http://localhost:5180/swagger
# auth/me: http://localhost:5180/api/auth/me  (401 sem cookie)
# inspect.html: http://localhost:5180/inspect.html (V0 preservado, anônimo)

# Smoke signup:
curl -X POST http://localhost:5180/api/auth/signup \
  -H 'Content-Type: application/json' \
  -d '{"email":"alice@example.com","password":"Strong-Password-1!","orgName":"Acme"}'
```

### Suíte verde no Sprint 2

```text
Lintty.Engine.Cli.Tests        2/2     verde
Lintty.Engine.Core.Tests      16/16    verde
Lintty.Engine.Reporter.Tests  10/10    verde   (gate de determinismo do PDF)
Lintty.WebInspector.Tests     35/35    verde   (17 V0 + 18 Sprint 2 novos)
                              ─────
                              63/63    verde, zero warning, zero erro
```

Sprint 2 novos:
- `AuthSignupTests` × 5
- `AuthLoginTests` × 4
- `OAuthCallbackTests` × 3
- `TenantContextMiddlewareTests` × 3
- `V0RegressionTests` × 3

### Pendências geradas (não-bloqueantes)

- **Sprint 3** vai adicionar os endpoints multi-tenant scan (POST `/api/orgs/{slug}/scans`) com filtragem por `org_id` via `ITenantContext`. O esqueleto já está pronto — middleware popula `HttpContext.Items["lintty.tenant.org_id"]`, repos só precisam consumir.
- **Recovery de senha V1.0 manual via DB** preserva a decisão do brief; `landing/login.html` já tem modal explicando o flow.
- **Multi-org switcher (claim `org_id`)** ficou como hook no middleware mas sem endpoint que mude o claim. Sprint 3 traz POST `/api/auth/switch-org` quando os endpoints multi-tenant precisarem.
- **Email verification (`SignIn.RequireConfirmedEmail`)** está `false` no V1.0 conforme decisão do PO. Reabre quando piloto pedir.
