# 07 — Segurança e Compliance

## 1. Modelo de Ameaça

### Quem queremos proteger contra

| Ator | Capacidade | Mitigação principal |
|------|-----------|---------------------|
| **Agência hostil** | Embute código malicioso ou manipula scan para passar | Sandbox gVisor + egress whitelist + sem execução de código + hash chain |
| **NuGet package malicioso** | Tenta exfiltração via dependência transitiva no `dotnet restore` | Artifact Registry como proxy + Container Analysis + egress whitelist |
| **DBA hostil interno** | Apaga linhas de audit no PostgreSQL | Hash chain + sequence gap check + bucket-locked archive em GCS |
| **Atacante externo** | Tenta acessar laudos ou código da sandbox | CMEK + IAM least-privilege + private networking + Cloud Armor + WAF |
| **Cliente do cliente** | Tenta acessar dados de outro tenant | Multi-tenancy enforced em DB level + middleware de autorização |
| **Provider LLM** | Pode persistir código analisado para treinamento | Enterprise Agreement com Zero Data Retention obrigatório |
| **Lintty insider** | Tenta acessar código do cliente | Ephemeral sandbox + zero persistence + audit logs com hash chain |

### O que NÃO defendemos no MVP

- Quantum-safe cryptography (não é threat model atual)
- Air-gapped deployment (V2+ para clientes regulados)
- Forensic-level user behavior analytics (V1+ se escala demandar)

---

## 2. Princípios de Segurança

### Ephemeral by design

O **código fonte do cliente nunca persiste** além do tempo de scan. Sandbox é destruída em minutos. Apenas:
- Metadata do grafo (agregado, não código)
- Métricas
- Snippets de violação (TTL 90 dias)
- PDFs assinados (retenção indefinida)

### Zero trust internal

- Cada Cloud Run service tem service account com IAM mínimo necessário
- Cloud SQL: IAM Auth obrigatório, sem senhas em código
- Sem chaves estáticas: Workload Identity Federation para todo CI/CD
- Service-to-service: mTLS via Cloud Run internal traffic

### Defesa em profundidade

Camadas:
1. **Network:** VPC privada, egress whitelisted, Cloud Armor no edge
2. **Identity:** IAM least-privilege, MFA obrigatório, quarterly review
3. **Application:** auth + authz em cada endpoint, rate limiting, input validation
4. **Data:** CMEK em tudo, snippets com lifecycle, audit chain
5. **Audit:** hash chain + sequence gap + bucket lock + TSA timestamp

---

## 3. Sandbox de Análise

### Isolation

Cloud Run Jobs gen2 com gVisor runtime. Cada scan = container novo. Características:
- **gVisor:** kernel user-space; user code não toca host kernel diretamente
- **Filesystem:** `/tmp` ephemeral, sem persistência cross-execution
- **Memória:** isolada e limitada (16GB no MVP)
- **CPU:** quota controlada
- **Rede:** Cloud NAT com firewall egress restrito

### Egress whitelist

Apenas:
| Destino | Porta | Razão |
|---------|-------|-------|
| `api.github.com` | 443 | Token + clone |
| `*.githubusercontent.com` | 443 | Clone de repos |
| `github.com` | 443 | Clone de repos |
| `api.anthropic.com` | 443 | LLM inference |
| Artifact Registry interno | — | Imagens + NuGet proxy |
| Cloud Logging/Monitoring/Trace | — | Via Private Google Access |

Tudo o mais é **bloqueado**. Tentativas viram log de Security Command Center.

### Não-execução

A análise estática **nunca executa o código do cliente**. Apenas:
- Lê arquivos
- Compila com `dotnet build` (compilador, não execução do binário gerado)
- Faz parse via Roslyn

Binário compilado é descartado junto com a sandbox.

### Teardown

```
Após scan:
  1. AuditEvent emitido
  2. Sandbox container terminado
  3. Cloud Run gen2 garante teardown completo de filesystem temporário
  4. Memória liberada
  5. Token GitHub revogado (já era short-lived)
  6. Secrets em memória zerados explicitamente antes do exit
```

`sandbox_integrity.source_destroyed_at` é registrado e impresso no laudo como evidência.

---

## 4. Gestão de Secrets

### Onde vivem

- **Secret Manager** centralizado em projeto `lintty-secrets`
- **Tenant-scoped:** secrets de feed NuGet privado por org cliente
- **Global:** chaves Lintty, certificados, API keys

### Como são acessados

- Cada service account do Cloud Run tem IAM bindings específicos
- **Sem env vars** com secrets: leitura runtime via Secret Manager API
- Em Cloud Run Jobs: secret é injetado como volume mount, lido na inicialização, **memória zerada após uso**

### Rotação

| Secret | Rotação |
|--------|---------|
| `pades-signing-cert` + `key` | Anual (CA-dependent) |
| `github-app-privatekey` | 90 dias |
| `anthropic-api-key` | 60 dias |
| `stripe-secrets` | 180 dias |
| `nuget-feed-<tenant>` | Cliente controla |
| `jwt-signing-key` | 30 dias |

Rotação automática via Cloud Scheduler + Cloud Function que rotaciona key e atualiza referências.

---

## 5. Audit Hash-Chain — implementação completa

### Por que existe

Pilar de credibilidade do Lintty. Garante que **mesmo Lintty insiders não podem alterar histórico** sem deixar evidência matemática.

### Schema

```sql
CREATE TABLE audit_events (
  id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  occurred_at     TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  actor_type      TEXT NOT NULL CHECK (actor_type IN ('user', 'system', 'github')),
  actor_id        TEXT,
  event_type      TEXT NOT NULL,
  entity_type     TEXT NOT NULL,
  entity_id       UUID NOT NULL,
  payload         JSONB NOT NULL,
  prev_hash       CHAR(64) NOT NULL,
  curr_hash       CHAR(64) NOT NULL,
  sequence        BIGSERIAL NOT NULL UNIQUE
);

-- Triggers proibindo UPDATE/DELETE
CREATE OR REPLACE FUNCTION raise_immutable_violation() RETURNS trigger AS $$
BEGIN
  RAISE EXCEPTION 'audit_events is append-only; UPDATE/DELETE not permitted';
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER audit_no_update BEFORE UPDATE ON audit_events
  FOR EACH ROW EXECUTE FUNCTION raise_immutable_violation();

CREATE TRIGGER audit_no_delete BEFORE DELETE ON audit_events
  FOR EACH ROW EXECUTE FUNCTION raise_immutable_violation();

-- Stored procedure para INSERT que calcula curr_hash atomicamente
CREATE OR REPLACE FUNCTION audit_append(
  p_actor_type TEXT,
  p_actor_id TEXT,
  p_event_type TEXT,
  p_entity_type TEXT,
  p_entity_id UUID,
  p_payload JSONB
) RETURNS UUID AS $$
DECLARE
  v_prev_hash CHAR(64);
  v_id UUID;
  v_canonical TEXT;
  v_curr_hash CHAR(64);
BEGIN
  SELECT curr_hash INTO v_prev_hash
  FROM audit_events ORDER BY sequence DESC LIMIT 1;
  
  IF v_prev_hash IS NULL THEN
    v_prev_hash := repeat('0', 64);  -- genesis
  END IF;
  
  v_id := gen_random_uuid();
  v_canonical := jsonb_build_object(
    'id', v_id,
    'actor_type', p_actor_type,
    'actor_id', p_actor_id,
    'event_type', p_event_type,
    'entity_type', p_entity_type,
    'entity_id', p_entity_id,
    'payload', p_payload
  )::TEXT;
  
  v_curr_hash := encode(digest(v_prev_hash || v_canonical, 'sha256'), 'hex');
  
  INSERT INTO audit_events (id, actor_type, actor_id, event_type, 
                            entity_type, entity_id, payload, 
                            prev_hash, curr_hash)
  VALUES (v_id, p_actor_type, p_actor_id, p_event_type, 
          p_entity_type, p_entity_id, p_payload, 
          v_prev_hash, v_curr_hash);
  
  RETURN v_id;
END;
$$ LANGUAGE plpgsql;
```

### Eventos canônicos

Tudo que move estado crítico vira AuditEvent:

| Event Type | Quando |
|------------|--------|
| `scan.requested` | Usuário solicita Milestone |
| `scan.completed` | Scan termina com sucesso |
| `scan.failed` | Scan falha |
| `canon.toggle.changed` | Mudança em ProjectRuleToggle (via PR mergeado) |
| `suppression.applied` | Supressão `@lintty-ignore` validada |
| `credit.consumed` | Crédito Pay-per-Scan debitado |
| `credit.purchased` | Cliente comprou créditos via Stripe |
| `credit.refunded` | Estorno por falha de scan |
| `report.signed` | PDF assinado emitido |
| `user.invited` | Membership criada |
| `user.role_changed` | Role mudou |
| `github_installation.added` | GitHub App instalado |
| `github_installation.removed` | GitHub App removido |

### Verificação Nightly: `audit-chain-verify`

Cloud Run Job scheduled diariamente:

```sql
-- Verificação 1: integridade da chain
WITH chain_check AS (
  SELECT a1.sequence, a1.curr_hash, a2.prev_hash AS next_prev
  FROM audit_events a1
  LEFT JOIN audit_events a2 ON a2.sequence = a1.sequence + 1
)
SELECT COUNT(*) FROM chain_check 
WHERE next_prev IS NOT NULL AND curr_hash != next_prev;

-- > 0 = chain quebrada → PagerDuty CRITICAL

-- Verificação 2: continuidade da sequência (proteção contra DELETE)
WITH expected AS (
  SELECT generate_series(1, (SELECT MAX(sequence) FROM audit_events)) AS seq
)
SELECT COUNT(*) FROM expected e
LEFT JOIN audit_events a ON a.sequence = e.seq
WHERE a.id IS NULL;

-- > 0 = gaps detectados → PagerDuty CRITICAL
-- Significa: alguém DELETOU linhas via DBA hostil ou bug catastrófico
```

### Export Weekly: `audit-chain-export`

Cloud Run Job scheduled semanalmente:
1. Exporta eventos da semana para `lintty-audit-archive-<env>` em GCS
2. Bucket tem **Bucket Lock** de 7 anos — **imutável mesmo com IAM root**
3. Backup imutável independente do PostgreSQL — se DB for comprometido ou perdido, evidência sobrevive

### TSA Monthly: `audit-tsa-timestamp`

Cloud Run Job mensal:
1. Pega `curr_hash` da última linha do mês
2. Submete a TSA RFC 3161 (DigiCert ou FreeTSA)
3. Carimbo de tempo é salvo como evidência adicional

Resultado: a chain pode ser provada como existente em data específica, sem depender só do Lintty.

---

## 6. Multi-tenancy

### Enforcement

Toda query no Postgres passa por middleware que injeta `WHERE org_id = $current_org`. Garantido em duas camadas:

1. **Application:** todo Repository tem método `WithOrgFilter(orgId)` obrigatório.
2. **Database (V1+):** Row-Level Security (RLS) com policies por tabela.

### Onboarding scope

GitHub App é instalado no nível da **Organization** do cliente (não user). Isolation total entre tenants.

---

## 7. Segurança de Aplicação

### Authentication

- **MVP:** GitHub OAuth (login social).
- **V1:** SSO corporativo via Google Workspace, Azure AD (SAML/OIDC).

### Authorization

Modelo de roles:

| Role | Pode |
|------|------|
| `admin` | Gerenciar org, projetos, billing, convidar usuários, mudar canon |
| `member` | Solicitar Milestones, ver laudos, editar canon (gera PR) |
| `viewer` | Ver laudos, score, tendências |

External Contributor (Agência) tem role específica com escopo limitado ao Project.

### API security

- **Rate limiting** via Memorystore Redis (limite por IP + per-user)
- **JWT** com TTL curto (1h), refresh via cookie HttpOnly
- **CORS** estrito (apenas domain Lintty)
- **CSRF** double-submit cookie pattern
- **Input validation** em todo endpoint (FluentValidation no .NET, ou similar)
- **Security headers:** HSTS, CSP, X-Frame-Options, etc.

### Webhook security

Webhook do GitHub é validado por:
1. **HMAC signature** com secret específico por installation
2. **Replay protection** via `delivery_id` em Redis (dedupe TTL 7d)
3. **Rate limit** por installation

---

## 8. Conformidade — LGPD

### Bases legais

- **Execução de contrato:** dados do contratante e da agência são processados para entregar o serviço contratado.
- **Legítimo interesse:** snippets de código violador (90d) são retidos para que a agência possa corrigir; após esse período, expurgados.

### Direitos do titular

- **Acesso:** painel exibe todos os dados pessoais.
- **Correção:** edita perfil no dashboard.
- **Exclusão:** request via support@lintty.com → processo manual no MVP, automatizado V1+.
- **Portabilidade:** export JSON via API (V1+).
- **Revogação de consentimento:** desativa conta; dados são purgados em 30 dias.

### Retenção

| Dado | Retenção |
|------|----------|
| Código fonte | Minutos (durante scan) |
| Snippets de violação | 90 dias |
| Metadata e métricas | Indefinida (parte do contrato — base: tendências) |
| PDFs assinados | Indefinida (base: prova contratual) |
| Audit chain | 7 anos (compliance) |
| Backups DB | 30 dias |

### Subprocessadores

Página pública em `lintty.com/subprocessors` com lista atualizada:
- Google Cloud Platform
- Anthropic (com ZDR contratual)
- Stripe
- GitHub
- TSA provider (DigiCert ou equivalente)
- Email provider (SendGrid ou equivalente)

### DPA (Data Processing Agreement)

Template revisado por advogado, disponível para todos os clientes. Assinatura registrada em `Organization.lgpd_dpa_signed_at`.

---

## 9. Conformidade — SOC 2 Type I (mês 12)

### Trust Service Criteria visados

- **Security:** controles de acesso, criptografia, segregação de duties.
- **Availability:** SLOs documentados, DR drills, monitoring.
- **Confidentiality:** classificação de dados, ephemeral analysis, audit chain.

### Roadmap

| Mês | Marco |
|-----|-------|
| 1 | Políticas internas (data retention, IR, change management, access control) |
| 1 | DPA template, subprocessors page, BAA Anthropic |
| 3 | Quarterly access review begin |
| 6 | Gap assessment com auditor (firma externa) |
| 6 | Pentest externo (V1) |
| 9 | Remediação de findings |
| 12 | **Auditoria SOC 2 Type I emitida** |

### Trabalho operacional contínuo

- Acesso revisado quarterly (todos IAM bindings, MFA enforcement)
- Logs revisados weekly (anomalias, tentativas de exfiltração)
- DR drill quarterly
- Pentest annual a partir do V1
- Bug bounty (HackerOne) a partir do V1

---

## 10. Incident Response

### SLAs de notificação ao cliente

Definidos no DPA:
- **P0 (data breach confirmed):** 24h
- **P1 (security incident, no breach):** 72h
- **P2 (degradação relevante):** 7d

### Runbook

Documento `runbook-ir.md` interno cobre:
1. Triagem inicial
2. Contenção
3. Erradicação
4. Recovery
5. Post-mortem (blameless)
6. Comunicação interna e externa

### War room

- Slack channel dedicado por incident
- Bridge com time técnico + liderança
- Cloud Logging + Monitoring + audit chain como fonte primária

---

## 11. Hardening contínuo

### Reviews periódicos

- **Daily:** triagem de alertas Cloud Monitoring
- **Weekly:** revisão de logs anômalos, status do Golden Suite
- **Monthly:** revisão de IAM bindings expirados, secret rotations
- **Quarterly:** access review completo, DR drill, threat model update
- **Annual:** pentest externo, política review

### Threat intelligence

- Monitoramento de CVEs em dependências (Dependabot, Snyk)
- Container Analysis nas imagens base (Artifact Registry)
- Semgrep + Trivy no CI/CD

### Bug bounty (V1+)

Programa público no HackerOne. Scope: produção do Lintty. Excluído: aplicações third-party.

---

## 12. Resumo das decisões críticas

| Item | Decisão |
|------|---------|
| Sandbox isolation | Cloud Run Jobs gen2 + gVisor + egress whitelist |
| Source code persistence | Zero (ephemeral) |
| Snippet retention | 90 dias com lifecycle policy |
| Audit chain | Hash + sequence gap check + bucket-locked GCS + TSA monthly |
| LLM data retention | Anthropic ZDR contratual obrigatório |
| Encryption | CMEK em DB, GCS, Secret Manager |
| Identity | Workload Identity Federation, sem chaves estáticas |
| Multi-tenancy | App-level + RLS em V1 |
| Compliance target | SOC 2 Type I mês 12, Type II + ISO 27001 mês 18-24 |
| Pentest | V1+ anual |
| Bug bounty | V1+ via HackerOne |
