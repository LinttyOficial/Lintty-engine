# 08 — Plano de Execução

Após travar os 4 eixos de design, a fase de implementação roda em duas trilhas paralelas: **Pre-flight** (lead time externo) e **Sprints de engenharia** (vertical slice end-to-end).

---

## 1. Pre-flight Checklist

Itens com **lead time de terceiros**. Se não começarem hoje, o motor fica pronto e não tem onde rodar nem como cobrar.

### Críticos (semanas 1–4)

| Item | Owner | Lead time | Impacto se atrasar |
|------|-------|-----------|-------------------|
| **Anthropic Enterprise + ZDR** | Founder | 2–4 semanas | Sem isso, o produto é risco SOC 2; impede demos para enterprise |
| **PAdES qualified certificate (e-CNPJ A3 ou HSM)** | Founder + advogado | 2–6 semanas (CA-dependent) | Sem isso, PDF não é assinável com peso probatório |
| **TSA (Timestamp Authority) integration** | Founder | 1 semana após cert | DigiCert ou FreeTSA. Necessário para PAdES-B-LT |
| **GitHub App registration** | Engenheiro | 1–2 dias | Define escopo de permissões: `metadata:read`, `pull_requests:write`, `contents:read`. App público ou interno (decisão GTM) |
| **Stripe Tax setup** | Founder + contador | 1–2 semanas | B2B com VAT/NIF + retenções na fonte. Necessário desde primeiro cliente |

### Operacionais (semanas 1–6)

| Item | Owner | Notas |
|------|-------|-------|
| **GCP Organization bootstrap** | Engenheiro | Folders, projetos, billing, Workload Identity |
| **Domínio + DNS** | Founder | `lintty.com` ou similar. Cloud DNS + email corporativo |
| **Razão social + CNPJ** | Founder + contador | Brasil-specific |
| **Conta bancária PJ** | Founder | Stripe payout |
| **Política de Privacidade + Termos + DPA** | Advogado | LGPD-compliant; DPA template para clientes |
| **Subprocessors page** | Engenheiro | `lintty.com/subprocessors`; lista atualizada |
| **Email provider** | Engenheiro | SendGrid, Postmark ou Resend |
| **Status page** | Engenheiro | Atlassian Statuspage ou similar (V1+) |

### Pre-flight de segurança/compliance (mês 1–6)

| Item | Notas |
|------|-------|
| Insurance E&O / Cyber | Lead time 4–8 semanas |
| Auditor SOC 2 selecionado | Lead time 4 semanas (cotação) |
| Bug bounty (V1) | HackerOne onboarding 2 semanas |

---

## 2. Roadmap de Implementação — Sprints

Cada sprint entrega uma camada vertical. Não construir tudo em paralelo: First Smoke completo end-to-end antes de polir camada por camada.

### Sprint 0 — Motor Standalone (Semanas 1–3)

**Objetivo:** binário standalone que prova que a análise determinística funciona.

**Módulo:** `Lintty.Engine.Roslyn` (CLI Tool)

**Entregável:** binário .NET que:
```bash
lintty-engine analyze --solution path/to/x.sln --canon-config path/to/lintty.yml
# stdout: factual JSON conforme schema do motor
```

**Dependências de Pre-flight:** nenhuma — desenvolvimento 100% local.

**Critério binário de sucesso:**
- Passa Golden Test Suite localmente para as 5 regras determinísticas (LNTY-001/002/003/006/007/008/009).
- Inclui:
  - **The Saint:** Score 100 / A
  - **The Sinner:** Score F (cada regra dispara uma violação)
  - **Ninja #1 ("Constant-Folded SQL"):** Score F com 2 violações LNTY-002 detectadas via constant folding

**Riscos:**
- Tempo subestimado para entender bem MSBuildWorkspace e WorkspaceFailed
- Layer tagging convention pode ter casos de borda imprevistos

**Mitigação:**
- Spike de 2 dias na primeira semana só carregando solutions reais e logando o que MSBuild reporta
- Layer tagging fail-fast resolve casos de borda — não tenta adivinhar

---

### Sprint 1 — Cérebro Semântico (Semanas 4–5)

**Objetivo:** integrar LLM e provar que o pipeline factual → IA → veredito funciona.

**Módulos:**
- `Lintty.Orchestrator` (chama o motor + envia candidatos para LLM)
- `Lintty.LLM.AnthropicAdapter` (implementa `ISemanticInferenceProvider`)
- `Lintty.LLM.PromptBuilder` (monta prompts cache-aware)

**Entregável:** ainda CLI. Aceita um scan completo end-to-end (motor + LLM):
```bash
lintty-orchestrator run --solution path/x.sln --canon-config path/lintty.yml
# stdout: laudo completo com violations + suppressions + score, mas SEM PDF (Sprint 3)
```

**Dependências de Pre-flight:** Anthropic Enterprise Agreement + ZDR ativo. API key disponível.

**Critério binário de sucesso:**
- Roda Ninja #1 e Saint sem chamar LLM (todas regras determinísticas)
- Roda The Sinner com 1 violação LNTY-004 detectada via LLM
- O "advogado de defesa" é acionado e ambas as chamadas são logadas
- `inference_signature` é preenchida corretamente em cada Violation LLM

**Marco vendável:** primeiro screenshot/demo do veredito completo com motor + LLM trabalhando juntos.

---

### Sprint 2 — Fortaleza GCP (Semanas 6–8)

**Objetivo:** tirar o motor do laptop. Tudo passa a rodar em ambiente isolado, escalável, auditável.

**Módulos:**
- Terraform: VPC, Cloud SQL, Cloud Run Service (orchestrator), Cloud Run Job (analysis worker), Pub/Sub, GCS, Secret Manager
- `Lintty.WebhookReceiver` (Cloud Run Service que recebe webhook GitHub)
- `Lintty.ScanWorker` (containerizado, roda como Cloud Run Job)

**Entregável:** PR no GitHub dispara um scan na infra GCP, com output em Cloud Logging.

**Dependências de Pre-flight:**
- GitHub App registrado e instalado em repo de teste
- GCP org bootstrapped
- Anthropic API key em Secret Manager
- Artifact Registry com imagem do worker

**Critério binário de sucesso:**
- Push em branch de PR no repo `lintty-golden-tests/the-sinner-csharp` dispara webhook
- Webhook receiver enfileira job no Pub/Sub
- Cloud Run Job pega, roda Build Gate, Roslyn, LLM (cached do Sprint 1)
- Resultado é persistido no PostgreSQL
- Cloud Logging mostra trace completo do scan
- Sandbox é destruída ao fim (verificar via `sandbox_integrity` no JSON)
- Egress logs do Security Command Center: zero tentativa fora da whitelist

---

### Sprint 3 — Artefato da Verdade (Semanas 9–10)

**Objetivo:** o resultado vira PDF assinado entregável ao cliente.

**Módulos:**
- `Lintty.Reporter` (Cloud Run Job para gerar PDF)
- `Lintty.PadesSigner` (assinatura PAdES-B-LT)
- `Lintty.AuditChain` (writer para `audit_events` + verify nightly + export weekly)
- `Lintty.Notifier` (Cloud Run Service: GitHub Status, email)

**Entregável:** Milestone solicitado via API/dashboard simples → PDF entregue por email com link compartilhável.

**Dependências de Pre-flight:**
- PAdES certificate procurado e instalado em Secret Manager
- TSA integration configurada
- Email provider configurado (SendGrid)

**Critério binário de sucesso:**
- Botão "Solicitar Auditoria Oficial" no dashboard mínimo
- Scan completo roda, PDF é gerado
- Email recebido com link compartilhável
- PDF aberto em verificador independente (Adobe Acrobat) mostra:
  - Assinatura digital válida
  - Timestamp confiável
  - Conteúdo: score, violações, supressões, inference_signature, rescan_index
- Audit chain tem entrada com `event_type=report.signed` e `curr_hash` válido

---

## 3. Sprints subsequentes (alta prioridade pós-MVP)

### Sprint 4 — Onboarding e Billing (Semanas 11–12)

- Fluxo completo de signup → instalação GitHub App → criação de Project
- Stripe checkout para créditos
- Wallet + ledger funcional
- Convite de Agência por email
- Dashboard "real" (substituí o mínimo do Sprint 3)

### Sprint 5 — Continuous Feedback (Semanas 13–14)

- Subscription mensal por repositório
- PR scans advisory funcionando
- GitHub Check Status com grade provisória

### Sprint 6 — Endurecimento e Compliance (Semanas 15–18)

- Audit chain verify + export jobs em produção
- Drift monitoring para LLM
- Golden Suite running daily contra produção
- Pentest interno (eng + security consultant)
- Documentação SOC 2 (políticas, evidências)
- Hardening de IAM + access reviews configurados

---

## 4. Primeira Contratação Estratégica

### Perfil ideal

**Senior .NET Engineer com viés de Platform/SRE.**

Por que **não** "dev fullstack":
- O fosso defensivo do Lintty é o motor (Roslyn) + a infra (GCP, sandbox, audit chain). Dashboard é commodity técnica.
- Um único engenheiro forte na intersecção de .NET profundo + GCP/infra moderna consegue carregar Sprint 0–3 sozinho com você.
- Frontend (React/Next.js) entra na 2ª contratação, depois que produto está provado.

### Skills concretas obrigatórias

- C# senior (≥5 anos)
- Roslyn / MSBuildWorkspace experience (mesmo que mínima)
- GCP em produção (Cloud Run, Cloud SQL, Pub/Sub) **OU** AWS equivalente forte
- Terraform
- Docker + Linux containers
- CI/CD (GitHub Actions ideal)
- Familiaridade com OAuth2/OIDC

### Skills "nice to have"

- Já trabalhou com analyzers Roslyn customizados
- Já lidou com prompt engineering / API LLM
- Já passou por SOC 2 audit
- Conhece DDD/Hexagonal Architecture profundamente (afeta julgamento de regras)

### O que ele NÃO precisa

- React/frontend
- ML/data science
- Mobile

---

## 5. Estrutura de equipe ao longo do tempo

| Mês | Headcount | Composição |
|-----|-----------|------------|
| 1–3 | 1 (founder) + 1 (eng) | Founder técnico + Senior .NET/Platform |
| 4–6 | 3 | + 1 Frontend (React/TypeScript) ou + 1 SRE específico |
| 7–12 | 4–5 | + 1 GTM/sales + 1 Product Designer (se UX virar gargalo) |
| 13–24 | 8–12 | + 2 engenheiros (escala motor para Java) + Customer Success + Compliance/Legal interno |

---

## 6. Riscos do plano e como acompanhar

### Risco 1: Sprint 0 estourar (motor mais complexo que parece)

**Sinal de alerta:** semana 2 sem passar nem o Saint.

**Mitigação:**
- Time-box de 3 semanas firme. Se não passou, Sprint 1 começa com motor incompleto e bug-fix entra em paralelo.
- Aceitar regras 001/002/007 como mínimo viável; 006 e 008 podem entrar no Sprint 1 se necessário.

### Risco 2: Anthropic ZDR demora além de 4 semanas

**Sinal de alerta:** Sprint 1 começa sem API key disponível.

**Mitigação:**
- Sprint 1 pode rodar com API key padrão (sem ZDR) usando dados sintéticos do Golden Suite.
- Não usar dados de cliente real até ZDR estar contratual.

### Risco 3: PAdES certificate atrasa

**Sinal de alerta:** Sprint 3 começa sem certificado.

**Mitigação:**
- Sprint 3 pode entregar PDF não-assinado primeiro (assinatura entra hotfix).
- Cliente piloto pode aceitar PDF não-assinado em fase beta com SLA explícito de signing futuro.

### Risco 4: Custo de LLM explode em scan grande

**Sinal de alerta:** primeiro scan real >$30 em LLM.

**Mitigação:**
- Tier de LoC já desenhado; budget por scan já implementado.
- Cache hit rate vai subir após primeiros 30d em produção.
- Pior caso: tunar few-shots para mais economia, ou aumentar threshold de pré-filtro Roslyn.

---

## 7. KPIs do MVP

| Métrica | Target final do MVP (mês 6) |
|---------|----------------------------|
| Primeiros clientes piloto | 3–5 contratantes ativos |
| Scans Milestone executados | 30+ |
| Score precision (vs golden) | 100% no Saint, 100% no Sinner, 95%+ no Ninja |
| LLM cost per scan (50k LoC) | ≤$2 |
| Cache hit rate (LLM) | ≥40% (rumando para 60% em V1) |
| Scan p95 duration (50k LoC) | ≤10 min |
| Compile failure rate | ≤15% (entregas reais quebradas) |
| NPS de contratantes | ≥30 |
| NPS de agências | ≥0 (esperado: agências são neutras/negativas inicialmente) |

---

## 8. Definition of Done para o MVP

O MVP está **pronto para vender** quando:

- [ ] Os 4 sprints técnicos (0–3) estão completos
- [ ] 3 clientes piloto rodaram pelo menos 1 Milestone real cada
- [ ] Golden Suite roda daily sem flip de veredito
- [ ] Pre-flight 100%: ZDR ativo, PAdES funcionando, Stripe operacional, DPA assinada por todos pilotos
- [ ] Política de Privacidade + Termos + Subprocessors page no ar
- [ ] Audit chain verify nightly rodando sem alertas
- [ ] DR drill executado uma vez (mesmo em ambiente staging)
- [ ] Documentação interna de runbook (incident response, deploy, rollback)
- [ ] Pricing público confirmado e Stripe configurado para conversão automática
