# 06 — Modelo de Dados e Fluxo de Auditoria

## 1. Multi-tenancy hierárquica

```
Organization (contratante | agency)
    │
    ├── Membership ←──── User
    │
    └── Project (◆ aggregate root)
            │
            ├── Repository (1..N)
            ├── ProjectRuleToggle (overrides do Canon)
            └── Scan (1..N)
                    │
                    ├── AnalysisRun (1:1, dados efêmeros)
                    ├── Violation (0..N)
                    ├── Suppression (0..N)
                    └── Report (1:1 para Milestones)
```

- Um Contratante pode ter múltiplos Projetos.
- Cada Projeto vincula uma Agência (External Contributor).
- Cada Projeto tem 1+ Repositórios.
- Cada scan é vinculado a 1 Repositório.

---

## 2. Entidades — schema lógico

### Identity & Access Context

```
Organization
  id              UUID PK
  name            TEXT
  type            ENUM (contratante | agency)
  gh_org_slug     TEXT
  billing_email   TEXT
  lgpd_dpa_signed_at TIMESTAMPTZ
  created_at      TIMESTAMPTZ

User
  id              UUID PK
  email           TEXT UNIQUE
  name            TEXT
  auth_provider_id TEXT  -- GitHub user id no MVP

Membership (User ↔ Organization)
  user_id         UUID FK
  org_id          UUID FK
  role            ENUM (admin | member | viewer)
  invited_by      UUID
  accepted_at     TIMESTAMPTZ
  PRIMARY KEY (user_id, org_id)

GitHubInstallation
  installation_id BIGINT PK     (do GitHub)
  org_id          UUID FK
  account_type    TEXT
  permissions     JSONB
  installed_at    TIMESTAMPTZ
```

### Contract Context (core do produto)

```
Project (◆ aggregate root)
  id                  UUID PK
  contratante_org_id  UUID FK (Organization)
  agency_org_id       UUID FK (Organization, NULL até aceite)
  name                TEXT
  canon_version_id    UUID FK ▲ PINNED IMMUTABLE
  status              ENUM (draft | active | paused | archived)
  created_at          TIMESTAMPTZ

Repository
  id                  UUID PK
  project_id          UUID FK
  gh_installation_id  BIGINT FK
  gh_repo_id          BIGINT
  default_branch      TEXT
  has_subscription    BOOLEAN  -- Continuous Feedback (PR scans ilimitados)

ProjectRuleToggle (overrides per-projeto sobre o Canon pinado)
  project_id          UUID FK
  rule_id             TEXT  (ex: "LNTY-005")
  enabled             BOOLEAN
  llm_min_confidence  NUMERIC  (override do default por regra)
  disabled_reason     TEXT
  PRIMARY KEY (project_id, rule_id)
```

### Rules Catalog Context

```
CanonVersion (◆ imutável)
  id              UUID PK
  semver          TEXT UNIQUE  (ex: "1.0.0")
  published_at    TIMESTAMPTZ
  release_notes   TEXT
  -- Após published_at, nenhum campo desta tabela ou de CanonRule muda

CanonRule
  id              UUID PK
  canon_version_id UUID FK
  code            TEXT  (ex: "LNTY-001")
  name            TEXT
  severity        ENUM (critical | high | medium | low)
  ignorable       BOOLEAN  (FALSE para hard locks)
  category        TEXT
  description     TEXT
  motor           ENUM (roslyn | llm | hybrid)
  default_enabled BOOLEAN
  UNIQUE (canon_version_id, code)
```

### Scan Orchestration Context

```
Scan (◆ aggregate root)
  id                  UUID PK
  project_id          UUID FK
  repository_id       UUID FK
  type                ENUM (pr | milestone)
  trigger_source      ENUM (webhook | dashboard | api)
  commit_sha          TEXT
  branch              TEXT
  pr_number           INT
  status              ENUM (queued | cloning | building | extracting | analyzing 
                            | scoring | reporting | completed | failed_compile 
                            | failed_system | failed_llm_outage)
  canon_version_id    UUID FK  -- snapshot, NÃO segue mudanças no projeto
  requested_by_user_id UUID FK
  started_at          TIMESTAMPTZ
  completed_at        TIMESTAMPTZ
  score_raw           INT  -- 0..100 antes de arredondar
  grade               CHAR(1)  -- A | B | C | D | F
  consumed_credit_id  UUID FK  -- transação na CreditWallet
  rescan_index        INT      -- quantas vezes este Milestone foi repetido (impresso no PDF)

AnalysisRun (1:1 com Scan, separado para teardown rápido de dados efêmeros)
  scan_id             UUID FK
  sandbox_id          TEXT
  graph_metrics       JSONB    (nodes, edges, contexts_detected, cyclomatic_hotspots)
  llm_calls_count     INT
  llm_tokens_used     INT
  llm_cost_usd        NUMERIC
  duration_ms         BIGINT
  compile_status      ENUM (success | failed)
```

### Analysis Output Context

```
Violation
  id                  UUID PK
  scan_id             UUID FK
  rule_id             TEXT
  severity            ENUM (critical | high | medium | low)
  file_path           TEXT
  line_start          INT
  line_end            INT
  evidence            JSONB
  snippet             TEXT  -- nullable; lifecycle 90d via job diário
  fingerprint         CHAR(64)  -- SHA-256 — chave de cache
  detected_by         ENUM (roslyn | llm)
  llm_confidence      NUMERIC
  inference_signature JSONB  -- só para detected_by=llm
  is_inconclusive     BOOLEAN  -- true para resultados onde Prompt A e B discordaram

Suppression
  id                  UUID PK
  scan_id             UUID FK
  violation_fingerprint CHAR(64)
  rule_id             TEXT
  justification       TEXT  (>=30 chars)
  author_email_git    TEXT
  valid               BOOLEAN  -- false se rule é hard lock ou justificativa inválida
  surfaced_in_pdf     BOOLEAN
```

### Report Context

```
Report
  scan_id           UUID FK (1:1 — só Milestones)
  pdf_gcs_path      TEXT
  pdf_sha256        CHAR(64)
  signature_pkcs7   BYTEA  -- assinatura PAdES
  tsa_timestamp     BYTEA  -- token RFC 3161
  shareable_token   TEXT   -- HMAC + expiração
  issued_at         TIMESTAMPTZ
```

### Billing Context

```
CreditWallet
  org_id            UUID PK FK  -- só contratantes
  balance           INT
  updated_at        TIMESTAMPTZ

CreditTransaction (ledger append-only)
  id                UUID PK
  org_id            UUID FK
  delta             INT  -- +N para compra, -N para consumo
  reason            ENUM (purchase | scan_consumption | refund | adjustment)
  related_scan_id   UUID FK  -- nullable
  stripe_event_id   TEXT     -- para purchases
  occurred_at       TIMESTAMPTZ

Subscription (Continuous Feedback por repo)
  id                UUID PK
  org_id            UUID FK
  repository_id     UUID FK
  plan              ENUM (continuous_feedback)
  started_at        TIMESTAMPTZ
  renewal_at        TIMESTAMPTZ
  stripe_subscription_id TEXT
  status            ENUM (active | past_due | canceled)
```

### Audit Trail Context (hash-chain)

```
AuditEvent (◆ append-only, immutable via DB triggers)
  id              UUID PK
  occurred_at     TIMESTAMPTZ
  actor_type      ENUM (user | system | github)
  actor_id        TEXT
  event_type      TEXT
  entity_type     TEXT
  entity_id       UUID
  payload         JSONB
  prev_hash       CHAR(64)
  curr_hash       CHAR(64)  -- SHA256(prev_hash || canonical_json(row_sem_hashes))
  sequence        BIGSERIAL UNIQUE
```

Detalhes da implementação em `07-security-compliance.md`.

---

## 3. Convenções e invariantes

### Imutabilidade de versão

`Project.canon_version_id` e `Scan.canon_version_id` são **cópias imutáveis**. Atualizar Canon de um Project é criar nova versão (audit event), não UPDATE direto.

### Lifecycle de snippet

`Violation.snippet` é coluna separada. Job diário (`snippet-expirator`) roda:
```sql
UPDATE violations SET snippet = NULL 
WHERE snippet IS NOT NULL 
  AND created_at < NOW() - INTERVAL '90 days';
```

### Cache de inferência LLM

- Chave de cache = `Violation.fingerprint` + `inference_signature_hash`
- Se mesmo fingerprint + mesma signature já tem resultado: reusa (zero LLM call)
- Quando `inference_signature` muda (model snapshot, prompts, canon), cache invalida automaticamente porque hash da chave muda

---

## 4. Fluxo Completo: Milestone Audit

```
[Usuário]
   │  "Solicitar Auditoria Oficial" (dashboard ou /lintty-audit no PR)
   ▼
[API Cloud Run]
   │
   ├─ 1. Validar:
   │     ├── wallet do contratante tem crédito?
   │     ├── projeto está active?
   │     ├── canon_version pinada existe?
   │     └── rate-limit (ex: max 5 milestones/hora/projeto)
   │
   ├─ 2. Reservar crédito:
   │     CreditTransaction(delta=-N, reason=scan_consumption, status=pending)
   │
   ├─ 3. Criar Scan(status=queued, type=milestone, commit_sha=X, 
   │        rescan_index=count_milestones_for_same_commit)
   │
   ├─ 4. Publicar evento: Pub/Sub topic "scan.requested"
   │
   ▼
[Pub/Sub] ──── push ────►
   │
   ▼
[Scan Worker - Cloud Run Job]
   │  Sandbox efêmero: VPC isolada, egress whitelist
   │
   ├─ 5. Token GitHub App com escopo mínimo (repo X, commit X)
   ├─ 6. Shallow clone --depth=1 em /tmp isolado
   ├─ 7. Contar LoC; se exceder tier contratado:
   │     opção (a) abortar com aviso
   │     opção (b) confirmar consumo de créditos adicionais
   │
   ├─ 8. BUILD GATE
   │     ├── dotnet restore (com NuGet proxy + secret de feed privado se houver)
   │     └── dotnet build --no-incremental
   │     ↓ FALHA → status=failed_compile
   │              ESTORNA crédito (CreditTransaction status=refund)
   │              notifica agência+contratante por email
   │              FIM.
   │
   ├─ 9. ROSLYN
   │     ├── MSBuildLocator.RegisterDefaults()
   │     ├── MSBuildWorkspace + WorkspaceFailed vigilante
   │     ├── Filtra código gerado
   │     ├── Layer tagging (lintty.yml > convention; FAIL-FAST se nenhum)
   │     ├── Constrói grafo: types, assemblies, namespaces, refs
   │     ├── Análise paralela por DAG de projetos
   │     └── Identifica bounded contexts
   │
   ├─10. DETERMINISTIC RULE PASS
   │     ├── LNTY-001 (Domain Isolation, dois passes)
   │     ├── LNTY-002 (Persistence Contamination, type+SQL literal)
   │     ├── LNTY-003 (Forbidden Instantiation)
   │     ├── LNTY-006 (Naming blacklist)
   │     ├── LNTY-007 (Cycles, dois grafos)
   │     ├── LNTY-008 (Ports at Boundaries)
   │     └── Identifica snippets cinza para LLM (LNTY-004, LNTY-005)
   │
   ├─11. LLM PASS (seletivo)
   │     Para cada candidato:
   │     ├── Cache lookup via fingerprint+signature
   │     │     ├── HIT  → usa cached, próximo
   │     │     └── MISS → continua
   │     ├── Verifica budget de tokens (8K hard cap = LNTY-009)
   │     ├── Chamada A (juiz) com tool_use forçado, temp=0
   │     ├── Se confidence<0.85 OU severidade Crítica/Alta:
   │     │     └── Chamada B (advogado de defesa)
   │     ├── Outcome:
   │     │     ├── Concordam VIOLATION → registra
   │     │     ├── Concordam NO_VIOLATION → ignora
   │     │     └── Discordam → INCONCLUSIVE (ambos reasonings → PDF Considerations)
   │     └── Persiste no cache
   │
   ├─12. SUPRESSÕES
   │     ├── Parser lê comentários @lintty-ignore
   │     ├── Para cada Critical com supressão → INVÁLIDA (hard lock)
   │     ├── Para Alta/Média → justificativa >=30 chars + autor git válido?
   │     └── Conta total: se >10% das Alta/Média → score=F automático
   │
   ├─13. SCORE
   │     ├── 100 - Σ(peso_severidade × count_aberta)
   │     ├── Arredonda em passos de 5
   │     ├── Qualquer Crítica aberta → grade=F
   │     └── Mapeia para A-F
   │
   ├─14. PERSISTÊNCIA
   │     ├── Violations, Suppressions, graph_metrics → PostgreSQL
   │     ├── Código-fonte DESTRUÍDO (sandbox teardown total)
   │     └── AuditEvent hash-chained (event_type=scan.completed)
   │
   ├─15. Publicar "scan.completed" → Pub/Sub
   │
   ▼
[Report Generator - Cloud Run Job]
   │
   ├─16. Gera PDF/A
   │     ├── Sumário executivo (score, grade, rescan_index)
   │     ├── Versão do Canon
   │     ├── inference_signature
   │     ├── Lista de violações
   │     ├── Sumário de Exceções (todas as supressões)
   │     ├── Grafo visual de dependências
   │     └── Rodapé técnico
   ├─17. Assina PAdES-B-LT com cert Lintty
   ├─18. Anexa timestamp RFC 3161 de TSA
   ├─19. Upload em GCS (ACL restrito), gera shareable URL
   ├─20. AuditEvent (event_type=report.signed)
   └─21. Publica "report.generated"
   │
   ▼
[Notifier - Cloud Run Service]
   │
   ├─22. Atualiza GitHub Check/Status no commit (link do laudo)
   ├─23. Envia email contratante + agência (link + sumário)
   ├─24. Atualiza painel do contratante (novo ponto na tendência)
   │
   ▼
[Billing - Cloud Run Service]
   │
   └─25. Confirma crédito: CreditTransaction status=confirmed
         (ou estorno se scan failed)
```

---

## 5. Fluxo PR Scan (Continuous Feedback)

Subconjunto do Milestone:

- **Trigger:** webhook GitHub em `pull_request` (opened, synchronize)
- **Pula passos:** 2 (não reserva crédito), 14 (persistência mínima), 16-21 (sem PDF), 25 (sem cobrança)
- **Atualiza:** GitHub Check Status com grade provisória, painel com "scan provisório"
- **Não atualiza:** tendência oficial (milestones), audit chain (não é evento crítico)
- **Pré-requisito:** repo tem `Subscription.status = active`. Sem subscription, scan PR retorna 402 com CTA.

---

## 6. State Machine do Scan

```
queued
   │
   ▼
cloning ───────────────────► failed_system (clone error)
   │
   ▼
building ──────────────────► failed_compile (build failed → estorno)
   │                                       └─► FIM (notifica)
   ▼
extracting ────────────────► failed_system (workspace load error → estorno)
   │
   ▼
analyzing
   │
   ├─ deterministic rules
   │
   ├─ llm calls ──────────► failed_llm_outage (após retry 30min → estorno)
   │
   ▼
scoring
   │
   ▼
(milestone only) reporting ─► failed_system (PDF/sign error → estorno)
   │
   ▼
completed (status final)
```

Cada transição emite AuditEvent. Estornos são `CreditTransaction(status=refund)` ligado ao `scan_id`.

---

## 7. Onboarding flow (alto nível)

1. Contratante visita `lintty.com`, clica "Get Started".
2. Login via GitHub OAuth.
3. Cria Organization (tipo: contratante).
4. Instala GitHub App na organização do GitHub do contratante (escopo: org-level).
5. Cria primeiro Project, escolhe Canon version (default v1.0.0), seleciona Repository.
6. Edita toggles do Canon (opcional, default: tudo on exceto LNTY-005).
7. Sistema gera PR automático no repo com `lintty.yml` inicial.
8. Convida agência por email. Agência recebe link, faz login com GitHub OAuth, é vinculada ao Projeto como External Contributor.
9. Compra créditos Pay-per-Scan via Stripe (ou ativa Subscription Continuous Feedback).
10. Pronto para scans.

V1+ inclui SSO corporativo (Google Workspace, Azure AD).
