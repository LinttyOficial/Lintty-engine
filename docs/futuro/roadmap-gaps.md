# 10 — Roadmap e Análise de Gaps

Este documento é a **análise honesta do que está e do que NÃO está especificado**. Após reler todos os Blueprints v1.0, identifico o que falta antes de Sprint 0 começar produtivamente, durante o MVP, e o que está conscientemente adiado.

---

## 1. O que está completamente especificado (não há ambiguidade técnica)

✅ **Eixo 1 — Canon v1.0**
- 9 regras com algoritmos de detecção definidos
- Layer tagging (convention + override + fail-fast)
- `lintty.yml` schema
- Score, hard locks, supressões com cap de 10%
- Governança via GitOps
- Versionamento imutável

✅ **Eixo 2 — Motor Roslyn**
- Ambiente de execução, container, recursos
- Pipeline canônico (15 passos)
- Algoritmos por regra (com código C# de referência em `03-motor-roslyn.md`)
- Semantic slicing factual
- Output JSON contract completo (schema v1.0)
- Bootstrap obrigatório (MSBuildLocator + WorkspaceFailed)

✅ **Eixo 3 — Infraestrutura GCP**
- Organização (folders, projects, org policies)
- VPC, egress whitelist, NuGet via Artifact Registry
- Compute (Cloud Run Jobs gen2 + Cloud Run Services)
- Cloud SQL right-sized + lifecycle
- Pub/Sub topics
- Secret Manager + rotação
- CI/CD (GitHub Actions + WIF)
- DR (RPO 1h / RTO 4h)
- Audit hash-chain (schema + jobs)

✅ **Eixo 4 — LLM Ops**
- Provider, modelo, snapshot pinado
- Estrutura de prompt cache-aware
- Output Tool Use forçado + Chain of Thought
- Auto-consistência (juiz + advogado de defesa, binário)
- Thresholds de confidence
- Slice budget
- inference_signature
- Prompt injection defense
- Drift monitoring

✅ **Plano de Execução**
- Pre-flight checklist com lead times
- Sprints 0–3 com critério binário de sucesso
- Primeira contratação definida

✅ **Golden Tests**
- Saint, Sinner, Ninja #1, #2, #3 todos especificados

---

## 2. Gaps Críticos — bloqueiam Sprint 0 ou criam débito imediato

Esses pontos precisam ser definidos **antes ou durante** Sprint 0, senão geram retrabalho ou bug em produção.

### G1. Schema concreto de mensagens Pub/Sub

**Status:** Tópicos nomeados. **Falta:** schema JSON exato de cada mensagem (`scan.requested`, `scan.completed`, etc).

**Impacto:** consumidores e produtores precisam concordar antes de Sprint 2. Bug se diferente.

**Resolução:** documentar cada mensagem com required fields, enums, exemplos de payload válidos.

### G2. Idempotência de eventos

**Status:** Mencionado "event_id único" mas não detalhado.

**Falta:** estratégia clara de idempotência:
- Como webhook do GitHub é deduplicado (delivery_id em Redis com TTL?)
- Como retry de Pub/Sub não cria scan duplicado
- Como recuperação após crash do worker não cobra crédito duas vezes

**Impacto:** sem isso, retries naturais geram cobrança duplicada e múltiplos PDFs.

### G3. Estratégia de versionamento do output JSON

**Status:** Campo `schema_version: "1.0"` definido.

**Falta:** quando subir versão, como Control Plane lida com versões antigas, como migra resultados antigos.

### G4. Specifically: como `git blame` resolve `author_email_git`

**Status:** Output define o campo.

**Falta:** detalhe de implementação. Sandbox tem .git history? Shallow clone funcional para git blame? (Sim com `--no-single-branch`, mas não confirmado no spec.)

**Impacto:** se shallow clone não permite git blame, supressão sem author é inválida → muitas supressões legítimas viram inválidas.

### G5. Layout exato do PDF do laudo

**Status:** Lista de campos obrigatórios definida.

**Falta:** template visual real. Diagrama de páginas, hierarquia de informação, identidade visual (Lintty branding).

**Impacto:** parte vendável do produto. Cliente julga a qualidade do PDF.

**Resolução:** brief de design para a primeira contratação ou freelancer especializado.

### G6. PadES library específica

**Status:** PAdES-B-LT + RFC 3161 timestamp definido como tecnologia.

**Falta:** library .NET concreta (iText, BouncyCastle, Aspose, Syncfusion). Cada uma tem limitações de licença e features.

**Impacto:** algumas dessas libs são pagas. Custo não está no orçamento.

**Resolução:** spike de 2 dias na semana 1 do Sprint 3 avaliando opções.

### G7. Stripe products & prices catálogo

**Status:** Tiering Pay-per-Scan + Subscription mencionados.

**Falta:** valores em USD/BRL definidos, criação dos products no Stripe, mapeamento credit pack → price_id, webhook events relevantes (`invoice.paid`, `subscription.updated`, etc).

**Impacto:** sem isso, billing não funciona em Sprint 4.

### G8. Email templates

**Status:** "Notifier envia email" definido.

**Falta:** templates concretos:
- Convite à agência
- Notificação de scan completed (contratante)
- Notificação de scan completed (agência)
- Compra de créditos confirmada
- Wallet baixa (alerta)
- Falha de scan
- Subscription renovada

### G9. Detalhes de Stripe Tax + emissão fiscal

**Status:** Stripe Tax mencionado.

**Falta:** B2B brasileiro tem peculiaridades (NF-e/NFS-e). Stripe não emite NFS-e diretamente.

**Resolução:** integração com emissor terceirizado (NFE.io, Bling) ou processo manual no MVP. **Conversa com contador.**

### G10. Onboarding flow detalhado

**Status:** Resumo de 10 passos em `06-data-and-flow.md`.

**Falta:**
- Telas e copy de cada passo
- Tratamento de "GitHub App já instalado em outra conta"
- Convite de agência: como agência recebe (email? link?)
- "Repos disponíveis" listing: paginação, busca, filtros
- Estado intermediário "GitHub App instalado mas Project não criado"

**Impacto:** UX crítico para conversão. Decide se cliente piloto adota ou abandona.

---

## 3. Gaps Importantes — precisam resolução durante MVP, não bloqueiam Sprint 0

### G11. Dashboard UX completo

**Status:** Mockup ASCII em conversa antiga. Adiado para após Sprint 2.

**Falta:** design real:
- Painel executivo do contratante (score, tendência, lista de scans, drill-down)
- Visualização de grafo de dependências (D3.js? Cytoscape?)
- Tela de configuração do Canon
- Tela de gestão de Projects/Repositories
- Tela de billing (wallet, transactions, subscriptions)
- Tela de membros e roles
- Visão da Agência (listagem de PRs com grade, drill-down em violações)

### G12. Specific monitoring dashboards

**Status:** SLOs mencionados. Cloud Monitoring listado.

**Falta:** dashboards concretos com queries:
- Operations dashboard (scans/dia, success rate, p95 duration por tier)
- Cost dashboard (custo Cloud Run + Cloud SQL + LLM por tenant)
- Quality dashboard (violation distribution, suppressions rate, golden suite status)
- Security dashboard (egress denials, audit chain status, IAM anomalies)

### G13. PagerDuty/Opsgenie config concreta

**Status:** Alertas listados.

**Falta:** mapping alerta → severity → escalation path → on-call rotation. Quem é acordado às 3am no MVP?

**Resolução:** no MVP, founder é on-call único. Documentar rotation quando equipe crescer.

### G14. PR merging strategy

**Status:** Required Status Check mencionado como configurável pelo cliente.

**Falta:**
- Como Lintty comunica "essa configuração existe" para clientes
- Documentação de "como configurar branch protection" para contratantes
- Tratamento de PR merge antes de scan completar (corrida)

### G15. Webhook retry e dedupe

**Status:** Webhook receiver mencionado.

**Falta:**
- Idempotency key explícita (delivery_id do GitHub)
- Behavior quando webhook chega duplicado
- TTL de dedupe em Redis
- Behavior quando webhook é antigo (commit já scan-ed)

### G16. Specific Anthropic prompt cache configuration

**Status:** "Cache-aware prompt" definido em arquitetura.

**Falta:**
- Cache TTL: 5min default ou 1h paid extension?
- Beta header configuration concreta
- Behavior quando cache é invalidado mid-flight

### G17. Tokenizer da Anthropic

**Status:** "Slice budget = 8K tokens" definido.

**Falta:** library concreta para contar tokens. Anthropic não publica oficialmente; usa-se aproximação via tiktoken-style ou client SDK.

**Risco:** subestimar count → estouro de context window → erro 400 da API.

**Mitigação:** usar `tiktoken` com encoding `cl100k_base` como aproximação, com margem de segurança de 20% (target real = 6K para garantir <8K real).

### G18. Specific cost model — quem paga LLM overhead

**Status:** "Re-cobrança em estouro" definida.

**Falta:**
- Como é apresentado ao cliente que vai re-cobrar
- Limite máximo de re-cobrança (não pode ser ilimitado)
- Caso edge: scan falha após estouro; estorna ou cobra?

### G19. Branch strategy do código Lintty

**Status:** "GitHub Actions com main → dev/staging/prod" mencionado.

**Falta:**
- Trunk-based ou git-flow?
- Hotfix process
- Long-lived branches?
- Padrão de commit messages (Conventional Commits?)

### G20. Code review standards

**Status:** Não mencionado.

**Falta:** standards mínimos do MVP:
- Mandatory review antes de merge
- Quando founder é único reviewer (early stage)
- Test coverage threshold

---

## 4. Gaps de Negócio — não-técnicos mas críticos

### G21. Pricing real (números)

**Status:** Tiers de LoC e estrutura definidos.

**Falta:** valores em moeda. Quanto custa 1 crédito? E o pacote de 10? E a subscription mensal?

**Resolução:** análise de market price + custo de operação (incluindo LLM tokens). Decidir antes de Sprint 4.

### G22. Customer profile do primeiro piloto

**Status:** "Empresas enterprise com .NET outsourcing" — vago.

**Falta:**
- Setor específico (financeiro? seguros? varejo?)
- Tamanho da empresa (Fortune 500? mid-market BR?)
- Buyer persona (CTO? Diretor de Engenharia? Compras de TI?)
- Como será descoberto/abordado (rede? cold outbound? marketing?)

### G23. GTM strategy

**Status:** Não definido.

**Falta:**
- Self-service ou sales-led?
- PLG ou top-down?
- Marketing channels (LinkedIn? content? eventos?)
- Pilot recruitment plan

### G24. Trial / Free tier strategy

**Status:** "Sem trial gratuito no MVP" decidido implicitamente.

**Falta:** se há "demo grátis com Saint/Sinner público" ou similar para buyer ver antes de comprar.

### G25. Legal entity formation

**Status:** Pre-flight lista CNPJ.

**Falta:**
- Tipo societário (LTDA? S/A?)
- Holding fora do Brasil (Delaware C-Corp para fundraising futuro)?
- Trademark "Lintty"
- IP assignment dos founders

### G26. Roadmap de produto pós-MVP claro

**Status:** V1 e Escala mencionados em `01-product-vision.md` e `08-execution-plan.md`.

**Falta:** priorização explícita baseada em feedback do MVP. Pré-MVP, V1 está em sketch.

---

## 5. O que está conscientemente ADIADO (V1+)

Decisões de escopo: **não falta**, é decisão deliberada.

| Item | Quando entra |
|------|--------------|
| Suporte a outras stacks (Java/Spring) | V1 (mês 12+) |
| Suporte a frontend/mobile analysis | V2+ |
| Cenário "repo da agência" (B) | V1 |
| Subscription "Continuous Feedback" sólida | V1 |
| SSO corporativo (Google Workspace, Azure AD) | V1 |
| Suporte a GitLab além de GitHub | V1 |
| API pública read-only | V1 |
| Badge público "Lintty A-grade" | V1 |
| SOC 2 Type I (auditoria emitida) | mês 12 |
| SOC 2 Type II + ISO 27001 | mês 18-24 |
| Multi-region ativo (RTO <5min) | V1+ |
| Self-hosted/single-tenant tier | V2+ (banco-tier customers) |
| Custom rules pelo cliente (DSL) | V2+ |
| LLM secundário (Gemini, GPT, self-hosted) | V1+ |
| Cache incremental do motor (file-level) | V1 |
| Heurística de "interface equivalente" por surface match | V1.1 |
| LNTY-005 com glossário do bounded context | V1.1 |
| LNTY-010 (Prompt Injection formal) | V1.1 |
| LNTY-011 (Reflection-based DI bypass) | V1.1 |
| LNTY-012 (Source Generator infra smuggling) | V1.1 |
| LNTY-013 (Conditional compilation hide) | V1.1 |
| Bug bounty (HackerOne) | V1 |
| Pentest externo anual | V1 |
| Integrações enterprise (SAP Ariba, Coupa, ServiceNow) | Escala |
| Analytics cross-agência | Escala |
| Dashboard mobile-friendly | V1 |

---

## 6. Roadmap consolidado para o MVP

Combinando tudo: Pre-flight + Sprints + Gaps críticos a resolver.

### Mês 1 (Semanas 1–4)

**Pre-flight:**
- Anthropic Enterprise + ZDR (start)
- PAdES certificate procurement (start)
- GitHub App registration
- Stripe + tax setup
- GCP Org bootstrap
- Domínio + DNS
- Razão social/CNPJ

**Sprints:**
- Sprint 0: Motor standalone CLI passando Saint, Sinner e Ninja #1

**Gaps a resolver em paralelo:**
- G1, G2: Pub/Sub schema + idempotência (durante Sprint 0 já ajudam Sprint 2)
- G6: PadES library spike

### Mês 2 (Semanas 5–8)

**Pre-flight:**
- Anthropic ZDR finalizado
- Stripe products/prices catalogados (G7)

**Sprints:**
- Sprint 1: Orchestrator + LLM (semanas 4–5)
- Sprint 2: GCP infra + Cloud Run Jobs (semanas 6–8)

**Gaps a resolver:**
- G3: Output JSON versioning
- G4: git blame na sandbox (validar)
- G15: Webhook idempotency

### Mês 3 (Semanas 9–12)

**Pre-flight:**
- PAdES certificate ativo
- Email provider configurado

**Sprints:**
- Sprint 3: Reporter + Audit chain (semanas 9–10)
- Sprint 4: Onboarding + Billing (semanas 11–12)

**Gaps a resolver:**
- G5: PDF layout
- G8: Email templates
- G10: Onboarding flow completo
- G21: Pricing real definido
- G22: Primeiro piloto identificado

### Mês 4–5 (Semanas 13–18)

**Sprint 5:** Continuous Feedback (PR scans subscription)
**Sprint 6:** Hardening + Compliance prep + Drift monitoring

**Gaps:**
- G11: Dashboard UX
- G12: Monitoring dashboards
- G13: PagerDuty config
- G16, G17, G18: Tunings de LLM
- Primeiros pilotos rodando

### Mês 6: Pré-launch

- Documentação de runbooks
- DR drill em staging
- Pentest interno
- 3+ pilotos com Milestone real rodado
- SOC 2 gap assessment com auditor externo

---

## 7. Decisões abertas que precisam ser tomadas (priorizadas)

Listagem do que precisa de uma resposta concreta, ordenado por urgência:

### Urgente (Mês 1)

1. **Pricing real:** quanto custa o crédito, o pacote, a subscription? (G21)
2. **Customer pilot profile:** vertical, tamanho, persona, abordagem. (G22)
3. **GTM channel:** como abordar o piloto. (G23)
4. **Holding offshore?** Influencia formação legal. (G25)
5. **PadES library:** decisão técnica antes de Sprint 3. (G6)
6. **NF-e/NFS-e provider:** Stripe Tax não basta no Brasil. (G9)

### Importante (Mês 2–3)

7. **PDF layout completo:** brief de design. (G5)
8. **Onboarding flow:** wireframes/copy. (G10)
9. **Email templates.** (G8)
10. **Dashboard MVP:** o que está em Sprint 4 vs adiado. (G11)
11. **Free tier?** Saint público demo? (G24)

### Pode esperar (Mês 4–6)

12. Dashboards de monitoring (G12).
13. PagerDuty config concreto (G13).
14. Branch strategy (G19).
15. Code review standards (G20).
16. Roadmap V1 priorizado (G26).

---

## 8. Risk register pós-blueprint

Riscos identificados em conversa que precisam observação contínua:

| # | Risco | Probabilidade | Impacto | Mitigação ativa |
|---|-------|---------------|---------|-----------------|
| R1 | Custo LLM explode em scan grande | Média | Alto | Tier de LoC, budget hard, cache hit rate ≥60% |
| R2 | Anthropic muda termos / sobe preço | Baixa | Crítico | Abstração ISemanticInferenceProvider |
| R3 | Entregas não compilam (build fail) | Alta | Médio (UX) | Documentar pré-requisitos; dry-run endpoint V1 |
| R4 | NuGet privado não funcional | Alta | Alto | Fluxo via secret, doc clara, fallback "público apenas" no piloto |
| R5 | Vazamento de IP do cliente | Baixa | Existencial | Ephemeral analysis, ZDR, CMEK, audit chain |
| R6 | Falsos positivos de IA geram disputa | Média | Médio | Auto-consistência, confidence thresholds, transparência total |
| R7 | Laudo irreproduzível | Baixa | Crítico | inference_signature pinada, snapshot do modelo |
| R8 | Latência em projetos >1M LoC | Média | Médio | Paralelização DAG, tier dimensionado, cache incremental V1 |
| R9 | LGPD / SOC 2 gap | Média | Alto | Controles desde dia 1, gap assessment mês 6 |
| R10 | Abuso do `@lintty-ignore` | Média | Médio | Cap 10%, hard locks Crit, exception summary |
| R11 | Drift do modelo Anthropic | Média | Crítico | Drift monitoring, daily golden suite, model snapshot pinado |
| R12 | First customer churn por UX ruim | Alta | Alto | Sprint 4 UX, sucesso ativo no piloto |
| R13 | Founder burnout por on-call solo | Alta | Médio | Documentar runbooks, automatizar alertas, segunda contratação no mês 4-6 |

---

## 9. Conclusão

**O Blueprint v1.0 é completo nos 4 eixos técnicos e operacionais.** Permite começar Sprint 0 amanhã com clareza.

**Os 10 gaps críticos (G1–G10) são todos resolvíveis durante Sprint 0–3** sem grande retrabalho, desde que sejam tratados explicitamente.

**Os gaps de negócio (G21–G26) são o real risco subestimado:** pricing, customer pilot, GTM. Sem isso, o produto pode ficar pronto e não vender.

**A próxima conversa estratégica deve ser sobre G21–G24** (pricing, customer profile, GTM), porque essas decisões devem influenciar partes do produto técnico (ex: tipo de telemetria que precisamos, integrações que devemos priorizar, copy do dashboard).

A engenharia tem 6 meses de trabalho claro à frente. O comercial tem 6 meses para construir do zero. Esse é o gap **maior** entre o blueprint e o lançamento real do MVP.
