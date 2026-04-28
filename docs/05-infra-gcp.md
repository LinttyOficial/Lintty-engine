# 05 — Infraestrutura GCP (LOCKED)

A infra do Lintty é desenhada em torno de quatro princípios:
1. **Custo idle próximo de zero.** Serverless por default; idle não paga.
2. **Isolamento total do código auditado.** Sandbox descartável, sem persistência de fonte.
3. **Auditabilidade end-to-end.** Tudo que move estado crítico vira AuditEvent imutável.
4. **Compliance desde o dia 1.** SOC 2 Type I no mês 12 não é meta retórica — controles iniciais já apontam pra lá.

---

## 1. Organização GCP

Estrutura hierárquica via **Cloud Organization + Folders**:

```
Organization: lintty.com
├── Folder: shared-services
│   ├── Project: lintty-bootstrap     (Terraform state, org policies)
│   ├── Project: lintty-artifacts     (Artifact Registry, imagens Docker, NuGet proxy)
│   ├── Project: lintty-secrets       (Secret Manager centralizado, rotação)
│   └── Project: lintty-security      (Security Command Center, audit logs agregados)
├── Folder: environments
│   ├── Project: lintty-dev
│   ├── Project: lintty-staging
│   └── Project: lintty-prod
└── Folder: customer-scoped (V1+)
    └── Project: lintty-prod-tenant-<X>  (single-tenant para clientes regulados)
```

### Org Policies hard-set (constraints)

Política por design, não confiando em IAM individual:

| Política | Valor | Razão |
|----------|-------|-------|
| `compute.requireShieldedVm` | `true` | Boot integrity + vTPM |
| `storage.uniformBucketLevelAccess` | `true` | Sem ACLs legados, só IAM |
| `iam.allowedPolicyMemberDomains` | `["lintty.com"]` | Bloqueia external accounts não-aprovados |
| `sql.restrictPublicIp` | `true` | Cloud SQL nunca tem IP público |
| `compute.restrictLoadBalancerCreationForTypes` | `INTERNAL` | Nada exposto direto |
| `iam.disableServiceAccountKeyCreation` | `true` | Força Workload Identity, sem chaves estáticas |

### Billing

Conta de billing única (`lintty-billing`), com:
- Budgets por projeto: alertas em 50%, 80%, 100%
- Currency: USD
- Tax setup: configurado via Stripe + Cloud Billing tax (B2B)

---

## 2. Rede

### VPC por ambiente

Cada projeto de ambiente (dev/staging/prod) tem **uma VPC privada**, sem IPs públicos em compute ou dados.

```
VPC lintty-prod-vpc (us-east1)
├── Subnet control-plane     10.10.0.0/24    Cloud Run services (Edge + Control)
├── Subnet data-plane        10.10.1.0/24    Cloud Run Jobs (sandbox de scan)
├── Subnet databases         10.10.2.0/24    Cloud SQL via PSC
└── Subnet connectors        10.10.3.0/28    Serverless VPC Access Connectors
```

### Egress strategy — o ponto mais sensível

A sandbox precisa chamar **apenas**:

1. `api.github.com` + clones (`github.com`, `*.githubusercontent.com`)
2. `api.anthropic.com` (LLM)
3. Artifact Registry interno (imagens base, pacotes NuGet via proxy)
4. Cloud Logging / Monitoring / Trace (Private Google Access)

### Implementação

- **Cloud NAT** dedicado, **sem default route** para internet
- **Firewall egress rules** com whitelist de CIDRs:
  - GitHub: ranges públicos via `api.github.com/meta`. **CronJob semanal** `lintty-egress-refresh` puxa a lista e atualiza firewall via Terraform
  - Anthropic: endpoint estático
  - Google APIs: via Private Google Access (não sai pra internet pública)
- **NuGet:** Artifact Registry no modo `remote repository` proxia `nuget.org`. Sandbox aponta apenas para AR interno. **Zero egress externo para `nuget.org`** (cache + audit trail + Container Analysis grátis)
- **Logs de toda conexão negada** vão para Security Command Center → alerta (evidência de tentativa de exfiltração)

---

## 3. Compute

### Decisão crítica: sandbox isolation

| Opção | Isolamento | Custo idle | Veredito MVP |
|-------|-----------|------------|--------------|
| **Cloud Run Jobs (gen2)** | gVisor (user-space kernel) | Zero | ✅ **Recomendado** |
| GKE Autopilot + gVisor RuntimeClass | gVisor | ~$75/mês | Overkill MVP |
| Firecracker microVM custom | microVM | Alto | Só para banco-tier em V2+ |

**Cloud Run Jobs gen2** com gVisor + egress whitelist + zero execução de código + sandbox descartável em minutos é isolamento mais que suficiente para o threat model atual.

### Serviços em produção

| Componente | Recurso | Configuração MVP |
|------------|---------|------------------|
| Dashboard SPA | Cloud Run Service (regional, autoscale 0–10) | min-instances=1 em prod |
| API REST | Cloud Run Service | min-instances=1 |
| Webhook GitHub | Cloud Run Service (dedicado, autoscale 0–20) | min-instances=2 (GitHub tem retry curto) |
| Scan Orchestrator | Cloud Run Service (state machine) | min-instances=1 |
| Análise (sandbox) | Cloud Run Job | sob demanda; 4 vCPU / 16 GB; timeout 45min; retry=0 |
| Report Generator | Cloud Run Job | 2 vCPU / 4 GB; timeout 5min |
| Egress refresh | Cloud Run Job (scheduled) | semanal |
| Audit chain verify | Cloud Run Job (scheduled) | nightly |
| Audit chain export | Cloud Run Job (scheduled) | weekly |

---

## 4. Camada de Dados

### PostgreSQL (Cloud SQL)

| Item | Valor |
|------|-------|
| Versão | PostgreSQL 15 |
| Tier inicial | `db-custom-2-3840` (2 vCPU / 3.75 GB RAM) |
| HA | Regional (failover automático zonal) |
| Encryption | CMEK via Cloud KMS, rotação anual |
| IP | Privado apenas, via Private Service Connect |
| PITR | 7 dias no MVP → 30 dias no V1 |
| Backups | Diários automáticos + export semanal para GCS cross-region |
| Auth | IAM Auth via Cloud SQL Auth Proxy (sem senhas em código) |
| Read replica | Postergado para V1 |

**Trigger de upscale:**
- `pg_stat_database.blks_hit_ratio < 98%` por 1h, OU
- CPU sustentada >70% por 1h
- Bump para `db-custom-2-7680` (7.5 GB RAM)

### GCS Buckets

| Bucket | Conteúdo | Retenção | Configuração |
|--------|----------|----------|--------------|
| `lintty-reports-<env>` | PDFs assinados | Indefinida (ou per-contract) | Uniform ACL, CMEK |
| `lintty-snippets-<env>` | Snippets de violação | Lifecycle 90d → delete | Uniform ACL, CMEK |
| `lintty-audit-archive-<env>` | Export do audit chain | 7 anos | **Bucket Lock** (imutável) |
| `lintty-backups-<env>` | DB backups exportados | 30d | Cross-region (DR) |

Bucket Lock no audit-archive é importante: torna o conteúdo imutável **mesmo com IAM root**. Diretor de engenharia consegue dormir.

### Redis (Memorystore)

| Item | Valor |
|------|-------|
| Tier MVP | Basic, 1 GB |
| Tier V1 | Standard HA, 2 GB |
| Uso | Rate limiting, locks distribuídos (scan lock por repo), cache de metadata GitHub, JWT blocklist |

---

## 5. Mensageria — Pub/Sub

Tópicos por **domain event**, não por serviço:

| Tópico | Publishers | Subscribers |
|--------|-----------|-------------|
| `scan.requested` | API, Webhook, Orchestrator | Scan Worker |
| `scan.completed` | Scan Worker | Report Generator, Notifier, Billing, Analytics |
| `scan.failed` | Scan Worker | Notifier, Billing (refund logic) |
| `report.generated` | Report Generator | Notifier (envia email com link) |
| `audit.event` | Múltiplos | Audit Hash-Chain Writer (única consumidora) |
| `canon.changed` | Canon Service | Cache invalidator |

Cada subscription tem **DLQ** após 5 retries + alerta. Mensagens são idempotentes via `event_id` único.

---

## 6. Secrets

### Estrutura

Secret Manager centralizado em `lintty-secrets` (folder `shared-services`):

| Secret | Escopo | Rotação |
|--------|--------|---------|
| `pades-signing-cert` | Global | Anual (CA-dependent) |
| `pades-signing-key` | Global | Anual |
| `github-app-privatekey` | Global | 90 dias |
| `anthropic-api-key` | Global | 60 dias |
| `stripe-webhook-secret` | Global | 180 dias |
| `stripe-api-key` | Global | 180 dias |
| `nuget-feed-<tenant-id>` | Por org | Controlado pelo cliente |
| `jwt-signing-key` | Global | 30 dias |

### Acesso

- **IAM fine-grained por service account.** Cada Cloud Run service só vê secrets que precisa.
- **Sem env vars persistentes** com secrets. Leitura é runtime via Secret Manager API.
- **Rotação automática** via Cloud Scheduler + Cloud Function que rotaciona key e atualiza referências.

---

## 7. CI/CD

### Plataforma: GitHub Actions

Decisão: **GitHub Actions** (não Cloud Build). Razões:
- Lintty vive no GitHub (App, golden tests, PR feedback no produto). Coesão maior.
- Workload Identity Federation elimina chaves GCP armazenadas no GitHub.
- Cloud Build vira redundante.

### Pipeline

```
PR → Main (em qualquer service do Lintty):
  1. Lint + format check
  2. Unit tests (per-package)
  3. Build artifact Docker
  4. GOLDEN TEST SUITE (gate obrigatório no motor Roslyn)
  5. Security scan (Trivy + Semgrep)
  6. Push para Artifact Registry (tag = commit SHA)
  7. Terraform plan

Merge em main:
  8. Deploy automático → dev
  9. Smoke tests
 10. Deploy automático → staging
 11. Integration tests (e2e com PR sintético)
 12. Manual approval gate
 13. Deploy → prod (canary 10% → 50% → 100% via Cloud Run traffic split)
 14. Post-deploy health checks (5min)
 15. Rollback automático se SLO violado
```

### Releases

- SemVer + changelog automático (commit conventional)
- Tag no repo = release no Cloud Run

---

## 8. Observabilidade

| Camada | Ferramenta | Retenção |
|--------|-----------|----------|
| Logs | Cloud Logging (JSON estruturado) | 30d prod / 7d dev |
| Métricas | Cloud Monitoring | 6 meses |
| Traces | Cloud Trace + OpenTelemetry | 30d |
| Erros | Error Reporting | Auto-group |
| Alertas | Cloud Monitoring → PagerDuty (Opsgenie no MVP) | — |

### SLOs iniciais

| SLO | Target |
|-----|--------|
| Scan completion rate | ≥99% (em 30d, excluindo falhas legítimas) |
| Scan p95 duration | <15min para tier ≤200k LoC |
| API availability | ≥99.5% |
| Webhook processing latency p95 | <10s |
| LLM API success rate | ≥98% |

### Alertas P0 (acordam alguém)

- Error rate >5% em 5min
- DLQ com mensagens não processadas
- LLM API >2% de falha
- Cloud SQL CPU >80% sustentado
- **Audit chain break detected** (verificação noturna)
- **Audit sequence gap detected** (DBA hostil)
- Golden Suite produção com flip de veredito

---

## 9. Audit Hash-Chain — implementação concreta

Pilar central da credibilidade do produto.

### Tabela `audit_events`

```sql
CREATE TABLE audit_events (
  id              UUID PRIMARY KEY,
  occurred_at     TIMESTAMPTZ NOT NULL,
  actor_type      TEXT NOT NULL,        -- user | system | github
  actor_id        TEXT,
  event_type      TEXT NOT NULL,        -- scan.requested, config.changed, ...
  entity_type     TEXT NOT NULL,
  entity_id       UUID NOT NULL,
  payload         JSONB NOT NULL,
  prev_hash       CHAR(64) NOT NULL,    -- SHA256 hex do hash anterior
  curr_hash       CHAR(64) NOT NULL,    -- SHA256(prev_hash || canonical_json(row))
  sequence        BIGSERIAL NOT NULL UNIQUE
);

-- Trigger proibindo UPDATE/DELETE — append-only forçado em DB-level
CREATE TRIGGER audit_no_update BEFORE UPDATE ON audit_events
  FOR EACH ROW EXECUTE FUNCTION raise_immutable_violation();
CREATE TRIGGER audit_no_delete BEFORE DELETE ON audit_events
  FOR EACH ROW EXECUTE FUNCTION raise_immutable_violation();

-- INSERT via stored procedure que calcula curr_hash atomicamente
```

### Eventos canônicos (events que viram `audit_events`)

Tudo que move estado crítico:
- `scan.requested`, `scan.completed`, `scan.failed`
- `canon.toggle.changed`
- `suppression.applied`
- `credit.consumed`, `credit.refunded`, `credit.purchased`
- `report.signed`
- `user.invited`, `role.changed`
- `github_installation.added`, `github_installation.removed`

### Jobs de verificação

#### Nightly: `audit-chain-verify`

```sql
-- Verificação 1: integridade da chain
SELECT COUNT(*) FROM audit_events a1
JOIN audit_events a2 ON a2.sequence = a1.sequence + 1
WHERE a2.prev_hash != a1.curr_hash;
-- > 0 = chain quebrada → PagerDuty crítico

-- Verificação 2: continuidade da sequência (sem gaps)
SELECT s 
FROM generate_series(1, (SELECT MAX(sequence) FROM audit_events)) s
LEFT JOIN audit_events ON sequence = s
WHERE audit_events.id IS NULL;
-- > 0 linhas = evidência de DELETE direto via DBA hostil → PagerDuty crítico
```

#### Weekly: `audit-chain-export`

Copia eventos da semana para `lintty-audit-archive-<env>` em GCS, bucket com **Bucket Lock** de 7 anos. Backup imutável fora do PostgreSQL.

#### Monthly: `audit-tsa-timestamp`

Hash do último `curr_hash` do mês é submetido a TSA RFC 3161 (DigiCert ou FreeTSA). Carimbo de tempo é salvo como evidência adicional ("a chain existia neste estado em T").

---

## 10. Disaster Recovery

### Targets MVP

| Métrica | Valor | Justificativa |
|---------|-------|---------------|
| RPO (Recovery Point Objective) | 1 hora | Cloud SQL PITR |
| RTO (Recovery Time Objective) | 4 horas | Failover regional manual |

Multi-region ativo (RTO <5min) dobra custo de Cloud SQL e Memorystore. **Postergado para V1+.**

### Drills

- **Quarterly game-day:** simular perda total de `us-east1` → restaurar em `us-central1` a partir de backups cross-region. Documentar tempo real e ajustar runbook.

### Runbook

Documento separado (`runbook-dr.md` em repo interno) descreve:
1. Critérios para declarar incident
2. Steps para failover
3. Steps para failback
4. Comunicação interna e com clientes (DPA define SLA de notificação)

---

## 11. Compliance roadmap

| Marco | Mês | Entregável |
|-------|-----|------------|
| Políticas internas | 1 | Data retention, access control, incident response, change management |
| DPA template | 1 | Template revisado por advogado, pronto para clientes |
| Subprocessadores público | 1 | Página `lintty.com/subprocessors` |
| BAA Anthropic | 1 | Acordo com ZDR e DPA assinado |
| Acesso quarterly review | 3 | Revisão de IAM bindings, MFA, joins/leaves |
| Pentest externo | 6 | Primeiro pentest (V1) |
| **SOC 2 Type I — gap assessment** | 6 | Avaliação inicial vs. controles |
| **SOC 2 Type I emitido** | 12 | Auditoria completa |
| Bug bounty (HackerOne) | 12 | Programa público |
| SOC 2 Type II + ISO 27001 | 18-24 | Auditoria contínua + ISO |

---

## 12. Estrutura Terraform

```
terraform/
├── bootstrap/                        (uma execução, manual)
│   ├── org-policies/
│   ├── state-backend/                (GCS bucket + state lock)
│   └── iam-initial/
├── modules/
│   ├── vpc/
│   ├── cloudsql/
│   ├── cloudrun-service/
│   ├── cloudrun-job/
│   ├── gcs-bucket/
│   ├── pubsub-topic/
│   ├── secret/
│   ├── workload-identity/
│   ├── monitoring-dashboard/
│   ├── audit-chain/
│   └── slo-policy/
└── envs/
    ├── dev/    (main.tf compondo módulos)
    ├── staging/
    └── prod/
```

State em GCS com versioning + lock. `tfstate` por ambiente, **nunca compartilhado**.

Apply via GitHub Actions com WIF; approval manual obrigatório para prod.

---

## 13. Cost shape (ordem de grandeza)

| Fase | Idle base/mês | Por scan (50k LoC) |
|------|---------------|---------------------|
| MVP, 10 clientes ativos | ~$800 | ~$2-4 compute + ~$1-2 LLM |
| V1, 50 clientes | ~$2.000 | similar |
| Escala, 500 clientes | ~$10.000 | similar (economia marginal de escala) |

A parte serverless escala linear com uso. O que cresce fixo: Cloud SQL, Memorystore, logs/observability.

### Cost levers principais

- Cache hit rate (LLM)
- Cloud SQL right-sizing
- Logs retention tuning
- NuGet proxy hit rate (Artifact Registry)

---

## 14. Decisões travadas

| Decisão | Valor |
|---------|-------|
| Sandbox MVP | Cloud Run Jobs gen2 (gVisor) |
| Ambientes | Dev + Staging + Prod |
| CI/CD | GitHub Actions com WIF |
| DR target | RPO 1h / RTO 4h |
| Região primária | us-east1 (com CDN para BR) |
| SOC 2 Type I | Mês 12 |
| NuGet | Artifact Registry como remote proxy |
| Audit chain verify | Chain integrity + sequence gap (proteção contra DBA hostil) |
| Cloud SQL inicial | db-custom-2-3840 (right-sized) |
