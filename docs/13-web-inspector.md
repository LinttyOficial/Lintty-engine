# 13 — Web Inspector (GitHub URL → PDF)

> Spec do segundo caminho de uso do V0. Complementa o CLI Self-Service ([ADR 0005](adr/0005-distribution-model.md)) — não substitui.

## 1. Em uma frase

`lintty.com/inspect` recebe uma URL de repositório GitHub, clona o conteúdo de forma efêmera no backend, **invoca o CLI determinístico já existente** (`Lintty.Engine.Cli`), devolve o PDF gerado para download e descarta o clone.

**Mesmo motor, mesmo Canon, mesmo PDF.** O Web Inspector não é uma "versão paralela do produto" — é um wrapper de conveniência.

## 2. Por que esse caminho existe

| Caminho | Atrito de uso | Quando vence |
|---------|---------------|--------------|
| **CLI Self-Service** (default — [`14-cli-distribution.md`](14-cli-distribution.md)) | Cliente baixa binário, instala SDK do .NET no equipamento, valida hash, roda. Onboarding técnico. | Cliente recorrente, repos privados, integração em CI. |
| **Web Inspector** (este doc) | Cola URL e clica "analisar". Onboarding zero. | **Demo ao vivo, repo público, PoC, primeiro contato.** |

O Web Inspector existe para o cenário "cliente quer ver o PDF agora, sem instalar nada". Para uso recorrente sério, o caminho default continua sendo o CLI local — porque **o código fonte nunca sai do equipamento dele**.

## 3. Fluxo do usuário

```
┌─────────────────────────────────────────────────────────────────┐
│                       lintty.com/inspect                        │
│  ┌───────────────────────────────────────────────────────┐      │
│  │ URL do GitHub:  github.com/______/_____               │      │
│  │ Branch/commit (opcional):  main                       │      │
│  │ □ Repo é privado (token GitHub temporário)            │      │
│  │                                                       │      │
│  │           [  Analisar arquitetura  ]                  │      │
│  └───────────────────────────────────────────────────────┘      │
└─────────────────────────────────────────────────────────────────┘
                               │
                               ▼  (cliente envia)
┌─────────────────────────────────────────────────────────────────┐
│ Backend Web Inspector                                            │
│  1. Validar URL (parse, github.com only no V0, sem self-hosted)  │
│  2. Validar tamanho/permissão via API GitHub (HEAD do repo)      │
│  3. Aceitar a fila → retornar job_id                             │
└─────────────────────────────────────────────────────────────────┘
                               │
                               ▼
┌─────────────────────────────────────────────────────────────────┐
│ Worker (subprocesso)                                             │
│  1. git clone --depth=1 --branch <ref> <url> /tmp/<job_id>/      │
│  2. Resolver target via TargetResolver (modo WebInspector,       │
│     ADR 0006): .sln na raiz → lintty.yml.projects → 1-csproj     │
│     exceção. Zero ou múltiplos .csproj sem lintty.yml falham    │
│     cedo com no_target ou ambiguous_target.                      │
│  3. lintty-engine analyze --target <path> \                      │
│         --pdf /tmp/<job_id>/laudo.pdf \                          │
│         --output-file /tmp/<job_id>/report.json                  │
│  4. Calcular sha256 do PDF e do JSON                             │
│  5. Mover artefatos para storage temporário (TTL 24h)            │
│  6. Apagar /tmp/<job_id>/ inteiro (clone + caches NuGet)         │
│  7. Marcar job como completed + URL assinada de download         │
└─────────────────────────────────────────────────────────────────┘
                               │
                               ▼
┌─────────────────────────────────────────────────────────────────┐
│  Frontend faz polling do status (ou SSE) e quando completed:    │
│   ┌──────────────────────────────────────────┐                  │
│   │ ✓ Análise concluída                      │                  │
│   │   Score: F (0/100)                       │                  │
│   │   Violações: 12  (3 hard locks)          │                  │
│   │   Canon: v1.0                            │                  │
│   │                                          │                  │
│   │   [ Baixar laudo.pdf ]  [ Ver JSON ]     │                  │
│   └──────────────────────────────────────────┘                  │
└─────────────────────────────────────────────────────────────────┘
```

## 4. Arquitetura técnica (V0)

Princípio: **o mais simples possível** que entrega o fluxo. Uma VM ou um Cloud Run service único basta. Sem multi-tenant, sem fila distribuída, sem dashboard.

```
┌──────────────────┐       ┌──────────────────────────────┐
│  Browser         │       │  lintty-web (Node ou .NET)   │
│  /inspect form   │  POST │  - rotas /api/jobs           │
│                  │ ────► │  - valida URL e payload      │
│                  │       │  - persiste estado em SQLite │
└──────────────────┘       │  - dispara worker            │
                           └──────────────┬───────────────┘
                                          │
                                          ▼  (subprocess)
                           ┌──────────────────────────────┐
                           │  worker.sh / worker.cs       │
                           │  - git clone --depth=1       │
                           │  - dotnet lintty-engine ...  │
                           │  - cleanup /tmp/<job_id>     │
                           └──────────────┬───────────────┘
                                          │
                                          ▼
                           ┌──────────────────────────────┐
                           │  storage local (TTL 24h)     │
                           │  /var/lintty/jobs/<id>/      │
                           │    laudo.pdf                 │
                           │    report.json               │
                           └──────────────────────────────┘
```

**Stack sugerida (decidir em ADR):**
- **Backend:** ASP.NET Core 8 minimal API (mesmo runtime que o motor — reaproveita conhecimento e garante que `lintty-engine` está disponível).
- **Storage de jobs:** SQLite local (`Microsoft.Data.Sqlite`). Schema mínimo: `jobs(id, url, ref, status, created_at, completed_at, error, pdf_path, json_path)`.
- **Storage de artefatos:** disco local com TTL via cron de limpeza, ou GCS / R2 quando a primeira VM começar a encher.
- **Auth:** zero no V0. Rate limit por IP via middleware (ex: `AspNetCoreRateLimit`).
- **Fila:** processa um job por vez no V0. Se exceder, devolve `429 Too Many Requests` com posição na fila.
- **Hospedagem:** uma VM (DigitalOcean, Hetzner) com Docker, ou Cloud Run com um único container. Custo mensal-alvo < US$ 30.

## 5. Contrato HTTP

### `POST /api/jobs`

```json
{
  "github_url": "https://github.com/cliente/projeto",
  "ref": "main",                  // opcional; default = branch default do repo
  "github_token": "ghp_...",      // opcional, só se repo privado
  "solution_path": "src/X.sln"    // opcional; se ausente, descobre automaticamente
}
```

Resposta `202 Accepted`:

```json
{
  "job_id": "01HKRZQ8M3X9...",
  "status": "queued",
  "poll_url": "/api/jobs/01HKRZQ8M3X9..."
}
```

Erros possíveis:
- `400 Bad Request` — URL inválida, fora de `github.com`, repo too large
- `429 Too Many Requests` — rate limit por IP excedido
- `503 Service Unavailable` — fila lotada (worker ocupado)

### `GET /api/jobs/{id}`

Resposta enquanto roda:
```json
{
  "job_id": "01HKR...",
  "status": "running",        // queued | running | completed | failed
  "stage": "analyzing"        // cloning | restoring | analyzing | rendering
}
```

Resposta quando completa:
```json
{
  "job_id": "01HKR...",
  "status": "completed",
  "score": 0,
  "grade": "F",
  "canon_version": "1.0",
  "violation_count": 12,
  "hard_locks_open": 3,
  "pdf_url": "/api/jobs/01HKR.../laudo.pdf",
  "json_url": "/api/jobs/01HKR.../report.json",
  "expires_at": "2026-05-01T18:00:00Z"
}
```

Resposta quando falha:
```json
{
  "job_id": "01HKR...",
  "status": "failed",
  "error_code": "compile_failed",   // clone_failed | no_target | ambiguous_target | target_not_found | invalid_config | no_sln (legado) | compile_failed | layer_tagging_error | timeout | internal_error
  "error_message": "dotnet build falhou: 47 erros..."
}
```

Códigos de target resolution ([ADR 0006](adr/0006-target-resolution.md) §8.1):

| Code | Causa |
|------|-------|
| `no_target` | Clone não tem `.sln`, nem `.csproj` único, nem `lintty.yml` com `projects:`. |
| `ambiguous_target` | Clone tem múltiplas `.sln`, ou múltiplos `.csproj` sem `lintty.yml`, ou `.sln` E `lintty.yml.projects:` simultaneamente. |
| `target_not_found` | Path declarado em `projects:` não existe no repositório. |
| `invalid_config` | `lintty.yml` viola schema (path absoluto, glob, escape via `..`, duplicata, sufixo não-`.csproj`). |
| `no_sln` | Legado, mantido como caso particular de `no_target` para URLs de pilotos antigos. Removido em V1 do contrato HTTP. |

### `GET /api/jobs/{id}/laudo.pdf`

`200 OK` com `Content-Type: application/pdf`. Headers de cache: `Cache-Control: private, max-age=86400`.

URL é **pública mas obscura** (job_id ULID — 26 chars random). Sem auth no V0; quando virar pago, vira URL assinada com HMAC.

### `GET /api/jobs/{id}/report.json`

`200 OK` com `Content-Type: application/json` (compact, mesmo bytes que entraram no `hash_content`).

## 6. Validação e segurança

### URL e repositório

| Validação | Comportamento se falhar |
|-----------|--------------------------|
| URL parse OK | `400 Bad Request` |
| Host = `github.com` (sem self-hosted) | `400 Bad Request` |
| Repo público acessível OU token presente | `400 Bad Request` |
| Tamanho do repo ≤ 500 MB (V0) | `400 Bad Request` ("muito grande para análise web — use o CLI local") |
| LoC efetivo ≤ 200k (V0) | Aceita mas avisa: ">200k pode estourar timeout" |

### Sandbox do worker

- Roda como usuário não-privilegiado, sem `sudo`.
- `/tmp/<job_id>/` é o único path gravável.
- Sem rede outbound exceto: `github.com` (clone), `api.nuget.org` via proxy do .NET (restore).
- Timeout duro: **15 minutos por job** no V0. Excedeu → kill, status `timeout`.
- Limites: 4 vCPU, 8 GB RAM por worker. Se o motor estourar memória, vira `internal_error`.

### Tokens GitHub

- Aceitos via campo `github_token` (PAT) **somente para o request atual**.
- **Não persistido em disco.** Vive em memória do processo worker, é zerado após o `git clone`.
- Avisamos no formulário: "Use um PAT com escopo `repo` e expiração de 24h. Após o scan, revogue."

### LGPD / dados

- Web Inspector é **Operador de tratamento** (LGPD art. 5º VII) durante a janela do scan.
- Política: clone descartado em ≤ 60 segundos após o scan terminar. Artefatos (PDF + JSON) ficam 24h e expiram.
- Termos de uso e Política de Privacidade ([`landing/privacidade.html`](../landing/privacidade.html)) precisam mencionar explicitamente o fluxo do Web Inspector.
- DPA disponível para uso recorrente ([`compliance/dpa-template.md`](compliance/dpa-template.md)).

## 7. Casos de erro a tratar bem

| Erro | UX no front-end |
|------|-----------------|
| Repo não tem `.sln` | "Não encontramos uma `.sln` na raiz. Você pode passar `solution_path` ou usar o CLI local." |
| `no_target` | "Não encontramos `.sln`, `.csproj` único, ou `lintty.yml` com `projects:` na raiz. [Ver como configurar →] Ou rode o CLI localmente: `lintty-engine analyze --target <path>`" |
| `ambiguous_target` | "Encontramos múltiplas `.sln` ou `.csproj` no repo. Adicione um `lintty.yml` com `projects:` na raiz para declarar o escopo. [Ver exemplo →] Ou rode o CLI localmente: `lintty-engine analyze --target <path>`" |
| `target_not_found` | "Um caminho declarado em `projects:` não existe no repo: `<path>`. Verifique o `lintty.yml`. Ou rode o CLI localmente: `lintty-engine analyze --target <path>`" |
| `invalid_config` | "`lintty.yml` inválido: `<motivo>`. [Ver schema →] Ou rode o CLI localmente: `lintty-engine analyze --target <path>`" |
| Repo precisa de NuGet privado | "Falha no `dotnet restore`. Configure `nuget.config` no repo ou use o CLI local." |
| Repo muito grande | "Repos > 500 MB precisam do CLI local. [Baixar CLI →]" |
| Falha de compilação | Mostra os primeiros 20 erros do `dotnet build` + link "documentação sobre por que falhamos cedo" |
| Layer tagging falha | "Não conseguimos classificar os projetos. Adicione um `lintty.yml` na raiz com `explicit_map` ([exemplo →])" |
| Timeout de 15 min | "Análise excedeu 15 min. Para projetos maiores, use o CLI local sem limite de tempo." |

A regra geral: **toda mensagem de erro do Web Inspector tem que apontar para o fallback do CLI local**. O Web Inspector é conveniência; o CLI é a verdade.

## 8. O que NÃO está no V0 do Web Inspector

| Feature | Por quê fica fora |
|---------|-------------------|
| Login / contas / histórico de scans | Sem multi-tenant ainda. Em V1 com primeiro pagante recorrente. |
| Webhook GitHub auto-trigger no push | É outro produto (subscription). V1+. |
| Comparação entre dois commits | Útil mas não para validar a tese. V1. |
| Painel de tendências por repo | V1+. Spec antiga em [`futuro/product-vision-blueprint.md`](futuro/product-vision-blueprint.md). |
| Suporte a self-hosted GitHub Enterprise | V1+. |
| Suporte a GitLab / Bitbucket / Azure DevOps | V1+. |
| Análise de múltiplas `.sln` no mesmo repo | V0: usa a primeira. V1: lista para o usuário escolher. |
| Análise incremental / cache de NuGet entre jobs | V0: cada job é from-scratch. V1: avalia se a latência exige cache. |

## 9. Esforço estimado (V0)

| Etapa | Esforço solo |
|-------|--------------|
| Estrutura ASP.NET minimal API + SQLite + frontend simples (HTML+JS, sem framework) | 3–4 dias |
| Worker subprocess wrapper (clone + invocar CLI + cleanup) | 2–3 dias |
| Frontend de form + polling + página de resultado com download | 2–3 dias |
| Validações (URL, tamanho, rate limit por IP) | 1–2 dias |
| Deploy (Docker + uma VM, ou Cloud Run) + domínio `lintty.com/inspect` | 1–2 dias |
| Testes manuais com os 3 fixtures publicados em `lintty-demo/the-*` | 1 dia |
| **Total** | **2–3 semanas** |

Ver [`15-roadmap-curto.md`](15-roadmap-curto.md) para enquadramento no calendário.

## 10. Critério de "pronto"

- [ ] Cola URL `https://github.com/lintty-demo/the-saint` → recebe PDF score A em < 90s.
- [ ] Cola URL `https://github.com/lintty-demo/the-sinner` → recebe PDF score F em < 120s.
- [ ] Cola URL `https://github.com/lintty-demo/the-ninja-01` → detecta LNTY-002 via constant folding.
- [ ] PDF gerado pelo Web Inspector é **byte-idêntico** ao PDF gerado pelo CLI local rodando no mesmo `.sln` na mesma versão do motor (gate de determinismo cruzado).
- [x] PDF do Web Inspector contra fixture **the-saint-no-sln** (sem `.sln`, com `lintty.yml.projects:`) é byte-idêntico ao PDF do CLI local — gate cruzado em `WorkerIntegrationTests.SaintNoSln_Runs_End_To_End_And_Pdf_Matches_Cli_Direct_Invocation`.
- [ ] Repo privado com PAT funciona end-to-end.
- [ ] Erro de compilação devolve mensagem clara + link para o CLI local.
- [ ] Rate limit por IP funciona (3 jobs/dia anônimo).
- [ ] Clone é apagado em ≤ 60s após o scan terminar (verificável por log + `ls /tmp`).

## 11. Riscos e mitigação

| Risco | Mitigação |
|-------|-----------|
| Cliente ver o Web Inspector e achar que mandamos código para um terceiro | Copy do site é explícito: "rodamos no nosso backend, descartamos em < 60s, **ou** baixe o CLI e rode no seu equipamento". |
| Repo gigante derrubar o worker | Limite duro de tamanho + LoC checados via API GitHub antes de clonar. |
| Alguém abusar para minerar / processar repo malicioso | Rate limit + sandbox sem rede outbound + timeout de 15 min + worker não privilegiado. |
| Custo da VM escapar | Métrica simples: jobs/mês × tempo médio. Acima de 1000 jobs/mês, migra para Cloud Run com auto-scaling. |
| PDF do Web Inspector divergir do CLI local | Gate de determinismo cruzado nos testes (item 10). Se quebrar, é bug de boot do .NET (locale, font, MSBuild) — investigar antes de deploy. |

## 12. Estado de implementação V0

### O que está pronto (2026-04-30)

Backend mínimo testável via HTTP em `engine/src/Lintty.WebInspector/`. Roda com `dotnet run`, sem container. Mora na mesma `Lintty.Engine.sln`, mesmas regras de Central Package Management e `Directory.Build.props`.

| Camada | Arquivo principal | Status |
|--------|-------------------|--------|
| Endpoints (minimal API) | `Endpoints/JobsEndpoints.cs` | `POST /api/jobs`, `GET /api/jobs/{id}`, `GET /.../laudo.pdf`, `GET /.../report.json`, `GET /healthz` |
| Bootstrap | `Program.cs` | DI, options, `InvariantCulture` forçado, schema SQLite criado no startup |
| Persistência de jobs | `Jobs/JobStore.cs` | `Microsoft.Data.Sqlite` direto (sem EF). Tabelas `jobs` + `rate_limits`. WAL on. |
| Worker (single-job) | `Jobs/JobWorker.cs` | `BackgroundService` com poll a cada 500ms, claim atômico via UPDATE, pipeline clone→engine→cleanup |
| Wrapper do CLI | `Jobs/EngineSubprocessRunner.cs` | `dotnet exec lintty-engine.dll analyze --solution X --pdf Y --output-file Z` com `LANG=C` + invariant globalization no filho |
| Wrapper do git | `Jobs/GitCliClient.cs` | Subprocess `git clone --depth=1 --branch ref`, timeout configurável, PAT vai só na URL e nunca em log |
| Validação | `Validation/UrlValidator.cs` + `GitHubMetadataClient.cs` | Parse + host=`github.com` + `HEAD` no `api.github.com` para checar 404 / size > 500 MB |
| Rate limit | Tabela `rate_limits` no JobStore | 3 jobs/IP/dia (config), `Retry-After: 86400` quando excedido |
| ULID | `Jobs/Ulid.cs` | Implementação inline 26 chars Crockford Base32, validação `IsValid` |
| Documentação OpenAPI/Swagger | `Program.cs` (`AddSwaggerGen` / `UseSwaggerUI`) | UI em `/swagger`, JSON cru em `/swagger/v0/swagger.json`. Habilitado em dev **e** prod (V0 sem auth, API é pública mesmo). Swashbuckle 6.9.0, XML doc comments dos DTOs incluídos. |

Configurável por `appsettings.json` (e env vars no padrão ASP.NET):
- `JobStorage:Root` — onde mora `jobs.sqlite` e `jobs/<id>/{laudo.pdf,report.json}` (default `var/lintty`)
- `Engine:CliDllPath` — caminho absoluto pro `lintty-engine.dll`. Quando vazio, o worker procura em `../Lintty.Engine.Cli/bin/<cfg>/net8.0/`.
- `Engine:JobTimeoutSeconds` — 900s (15 min) por spec §6.2
- `Engine:CloneTimeoutSeconds` — 120s pra clone, separado pra hung clone não consumir o budget todo
- `Queue:MaxLength` — 5 (devolve `503` no POST)
- `RateLimit:JobsPerIpPerDay` — 3

### Suíte de testes (`engine/tests/Lintty.WebInspector.Tests/`, 13 testes)

Roda em ~22s na minha máquina, full Release.

- `HealthEndpointTests` (1) — `GET /healthz` → 200 `{"status":"ok"}`.
- `JobsApiContractTests` (8) — host inválido → 400; URL bem-formada → 202 com ULID válido; `id` desconhecido → 404; `id` malformado → 404; 4º POST do mesmo IP → 429 com `Retry-After`; repo > 500MB → 400 `repo_too_large`; repo 404 da API → 400 `repo_not_accessible`; body vazio → 400. Usa `WebApplicationFactory<Program>` + `FakeGitHubMetadataClient`.
- `WorkerIntegrationTests` (2) — Saint e Sinner, **gate de determinismo cruzado**: substitui `IGitClient` por `FixtureCopyGitClient` (copia `fixtures/the-*/` em vez de clonar), roda o worker real, espera `completed`, valida grade/score/`hard_locks_open`, e **compara `sha256` do PDF gerado pelo worker com o `sha256` do PDF gerado por invocação direta do CLI** contra o mesmo `.sln`. Bate. Também valida que `/tmp/lintty-<id>/` foi apagado.
- `SwaggerDocumentTests` (2) — `GET /swagger/v0/swagger.json` → 200 `application/json` com `info.title="Lintty Web Inspector API"` e `info.version="v0"`; documento contém todos os 5 paths esperados (`/healthz`, `/api/jobs`, `/api/jobs/{jobId}`, `/api/jobs/{jobId}/laudo.pdf`, `/api/jobs/{jobId}/report.json`).

### O que ficou de fora deliberadamente (próximos tickets)

- **Frontend `/inspect`** — sem HTML, sem JS de polling, sem página de resultado. Backend já está pronto pra ser consumido por qualquer cliente HTTP.
- **Dockerfile / deploy** — sem multi-stage build, sem Cloud Run / VM provisioning. Roda só em `dotnet run` local.
- **Workflow de CI** — sem GitHub Actions pra esta camada (a suíte de testes é o gate, mas falta integrar no `.github/workflows/`).
- **PAT do GitHub no clone** — o endpoint **aceita** `github_token` no body e usa pra checar a metadata API, mas o token **não é repassado pro worker em V0**. Repos privados via Web Inspector exigem o ticket "secret cache em memória keyed by job_id com TTL curto" (próximo). Cliente com repo privado usa o CLI local.
- **TTL cleanup dos artefatos** — `expires_at` é gravado mas não tem cron / hosted service deletando os PDFs depois das 24h. Próximo ticket.
- **Endurecimento sandbox em Linux** — usuário não-privilegiado, ulimit, seccomp, etc. ficam pro Dockerfile.
- **SSE / WebSocket pra status** — V0 só polling via `GET /api/jobs/{id}`.

### Como testar localmente em 3 comandos curl

Pré-requisito: ter rodado `dotnet build engine/Lintty.Engine.sln -c Release` (o worker procura `lintty-engine.dll` no output do Cli).

```bash
# Terminal 1 — sobe a API
cd engine/src/Lintty.WebInspector
ASPNETCORE_URLS="http://localhost:5180" dotnet run --no-build -c Release

# Terminal 2 — três comandos
curl -s http://localhost:5180/healthz
# → {"status":"ok"}

curl -s -X POST http://localhost:5180/api/jobs \
  -H "Content-Type: application/json" \
  -d '{"github_url":"https://github.com/dotnet/samples"}'
# → 202 Accepted; {"job_id":"01K...", "status":"queued", "poll_url":"/api/jobs/01K..."}

curl -s http://localhost:5180/api/jobs/<job_id_da_resposta_anterior>
# → status corrente; quando "completed", traz pdf_url e json_url

# 4. Abre a documentação OpenAPI no navegador:
#      http://localhost:5180/swagger          (UI interativa do Swashbuckle)
#      http://localhost:5180/swagger/v0/swagger.json   (documento OpenAPI 3.0 cru)
```

Se quiser rodar a suíte completa em vez do smoke manual:

```bash
cd engine
dotnet test Lintty.Engine.sln -c Release        # 28/28 incluindo gate de determinismo cruzado
```
