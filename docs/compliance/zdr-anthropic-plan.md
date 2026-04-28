# ZDR Anthropic Enterprise — Plano de Contratação

> **STATUS: V1+ ROADMAP — DESATIVADO NO SALES CUT (pivô 2026-04-27).**
> Sales Cut roda Zero-IA, Zero-Custo (`docs/12-sales-cut.md` reformulado). ZDR Anthropic só vira pré-requisito quando reativarmos LNTY-004/005 na fase Production MVP, após sinal de "go" comercial. Documento preservado como playbook para esse momento.

> **Status original:** ABERTO — execução semana 1 do Sales Cut.
> **Data início:** 2026-04-27
> **Data alvo de assinatura:** 2026-05-25 (4 semanas — limite superior do lead time típico)
> **Owner:** security-compliance (este agente) + product-owner (decisão comercial)
> **Documentos relacionados:** `docs/04-llm-ops.md`, `docs/07-security-compliance.md` §1 e §12, `docs/12-sales-cut.md` §4.3 e §7

---

## 1. Por que ZDR é não-negociável

Zero Data Retention (ZDR) com Anthropic Enterprise é **pré-requisito contratual** para que o Lintty rode o motor LLM contra **código fonte real de cliente**. Três razões, sem cerimônia:

### 1.1. Modelo de ameaça (`docs/07-security-compliance.md` §1)

A tabela de atores hostis lista explicitamente o **Provider LLM** como ameaça relevante: "pode persistir código analisado para treinamento". A mitigação obrigatória é "Enterprise Agreement com Zero Data Retention obrigatório". Sem ZDR contratualizado, este vetor permanece aberto e fere o princípio de **análise efêmera**.

### 1.2. Análise efêmera (`docs/07-security-compliance.md` §2 — Ephemeral by design)

O contrato implícito do Lintty com o cliente é: "o seu código fonte não persiste além do tempo de scan, em lugar nenhum da cadeia". Se a Anthropic retém o payload de inferência por 30 dias (default Tier 1) ou usa para treinamento, esse contrato está quebrado **antes mesmo de o sandbox derrubar o filesystem**. O DPA com o cliente (ver `dpa-template.md` §5) faz declaração expressa nesse sentido — sem ZDR no upstream, essa cláusula vira mentira.

### 1.3. LGPD e exposição (`docs/07-security-compliance.md` §8)

Código fonte do cliente pode conter dados pessoais embutidos (comentários, strings literais, nomes de variáveis com PII). Sub-Operador que retém esse conteúdo expande o escopo de tratamento sem base legal. ZDR contratual fecha esse flanco.

### 1.4. Posicionamento dos demais agents

- O `llm-ops` (`docs/04-llm-ops.md`) trata ZDR como controle padrão da camada de inferência.
- O `product-owner` precisa do ZDR assinado antes de ofertar piloto com código real (ver §6).
- A regra deste agent é clara: **"Apenas com fixtures sintéticas. Código real exige ZDR contratualizado — sem isso, ferimos análise efêmera e abrimos exposição LGPD."**

---

## 2. Cronograma cronometrado a partir de 2026-04-27

Lead time típico Anthropic Enterprise: **2-4 semanas** entre contato comercial e workspace provisionado com ZDR addendum assinado. Cronograma trabalha com a janela superior (4 semanas) para criar buffer.

### Semana 1 — 2026-04-27 a 2026-05-03 — Contato comercial e abertura

| Dia | Ação | Owner |
|-----|------|-------|
| Seg 27/04 | Submeter form Anthropic Sales (`anthropic.com/contact-sales`) com flag explícita "Enterprise tier + ZDR addendum required for B2B SaaS handling third-party source code under LGPD" | product-owner |
| Seg 27/04 | Em paralelo: email direto a `sales@anthropic.com` com mesmo conteúdo, pedindo SE designado | product-owner |
| Ter-Qua 28-29/04 | Resposta esperada de SE (Sales Engineer). Agendar call de descoberta | product-owner |
| Qui-Sex 30/04-01/05 | Call de descoberta com Anthropic. Agenda obrigatória da call: (1) confirmar ZDR está dentro do Enterprise tier, (2) volume estimado de tokens/mês, (3) modelos cobertos (Claude Opus + Sonnet), (4) lead time real, (5) lista de docs jurídicos a trocar | product-owner + security-compliance |

**Volume estimado a comunicar à Anthropic** (rascunho — ajustar com llm-ops se necessário):
- Mês 1-3: <1M tokens/mês (testes + Saint/Sinner/Ninja).
- Mês 4-6: 5-20M tokens/mês (1-3 pilotos pagantes).
- Mês 7-12: 50-200M tokens/mês (5-15 contratos).

### Semana 1-2 — 2026-04-27 a 2026-05-10 — Troca de documentação

| Item | Para a Anthropic | Da Anthropic |
|------|------------------|--------------|
| CNPJ + razão social Lintty | enviar | — |
| Due diligence questionnaire (security posture, controles, data flow) | preencher e enviar | template recebido |
| Volume forecast estruturado (tokens/mês por trimestre, ano 1) | enviar | — |
| Use case description (B2B SaaS, código fonte de cliente, análise efêmera, ZDR mandatório) | enviar | — |
| MSA (Master Services Agreement) | — | receber |
| ZDR Addendum (ou DPA equivalente) | — | receber |
| SOC 2 Type II report da Anthropic | — | requisitar |
| Subprocessadores Anthropic (lista) | — | requisitar para alimentar nossa subprocessors page futura |

### Semana 2-3 — 2026-05-04 a 2026-05-17 — Revisão jurídica

| Dia | Ação | Owner |
|-----|------|-------|
| Seg 04/05 | Encaminhar MSA + ZDR Addendum ao advogado especialista LGPD (mesmo profissional que está revisando o DPA template — ver `dpa-template.md`). Pedir leitura **focada em** quatro pontos críticos: | security-compliance |
| | (1) ZDR é cláusula **contratual** (não config opcional do dashboard) | |
| | (2) Retenção zero aplica a **todos os payloads** — prompt + completion + logs internos da Anthropic | |
| | (3) Cláusula de **non-training** explícita (código de cliente nunca alimenta treinamento de modelos Anthropic) | |
| | (4) Notificação de incidente: SLA compatível com nosso DPA com cliente (ver `dpa-template.md` §7 — 24h) | |
| Qui 14/05 | Receber parecer jurídico. Consolidar pontos de redlines com Anthropic | security-compliance |
| Sex 15/05 | Submeter redlines à Anthropic | product-owner |

### Semana 3-4 — 2026-05-18 a 2026-05-25 — Assinatura e provisionamento

| Dia | Ação | Owner |
|-----|------|-------|
| Seg-Ter 18-19/05 | Negociação final de redlines com Anthropic Legal | product-owner + security-compliance |
| Qua 20/05 | Assinatura eletrônica MSA + ZDR Addendum | product-owner (signatário legal) |
| Qui-Sex 21-22/05 | Provisionamento de workspace Enterprise. Confirmar ZDR está flagado no console | security-compliance + backend-dev-cloud |
| Seg 25/05 | **Data alvo final.** Workspace operacional com ZDR ativo. Chave API gerada com tagging por ambiente (dev/staging/prod) | security-compliance |

---

## 3. O que é PERMITIDO antes do ZDR estar assinado

A trava é sobre **código real de cliente**, não sobre o motor LLM em geral. Permitido enquanto ZDR não está ativo:

- Rodar LLM (Anthropic API tier padrão, qualquer plano) contra **fixtures sintéticas** do repo `lintty-fixtures`:
  - Saint (`the-saint`)
  - Sinner (`the-sinner`)
  - Ninja #1 — Constant-Folded SQL
  - Ninjas #2-N quando existirem
- Sprint 1 do motor (`docs/12-sales-cut.md` §3.1) com LLM integrada usando **apenas** os fixtures acima.
- Demos ao vivo para investidor ou piloto técnico usando os fixtures públicos.
- Desenvolvimento de prompts, few-shot, calibragem do "advogado de defesa" — tudo contra fixtures.

## 4. O que é PROIBIDO antes do ZDR estar assinado

- **Rodar LLM contra qualquer linha de código fornecida por cliente real**, mesmo que para "demonstrar viabilidade técnica" em piloto. Sem exceção.
- **Aceitar piloto com upload de código real** prometendo "ZDR está em andamento". Se o cliente upar antes da assinatura, a inferência é com **LLM mockada** ou **apenas Sprint 0 (motor Roslyn standalone, sem chamada de API)**.
- Qualquer logging de conteúdo de prompt/completion contendo código de cliente em sistemas internos do Lintty (também válido após ZDR — pseudo-anonimização sempre).

## 5. Riscos e mitigações

| Risco | Probabilidade | Impacto | Mitigação |
|-------|---------------|---------|-----------|
| Lead time excede 4 semanas (Anthropic Legal travado) | Média | Alto se piloto exigir código real em 5-6 semanas | Começar **2026-04-27** (semana 1 do Sales Cut). Buffer de 2 semanas até o início típico de piloto pagante |
| Anthropic recusa termos do ZDR Addendum (cláusulas de auditoria, SLA de incidente) | Baixa | Médio | Fallback A: piloto com **LLM mockada** (resposta determinística, sem chamada externa) — perde "advogado de defesa" mas mantém Sprint 0; Fallback B: piloto **só com Sprint 0** (motor Roslyn puro, sem IA) |
| Custo Enterprise incompatível com runway pré-receita | Média | Médio | Negociar **startup credit** com Anthropic (programa Anthropic for Startups). Pedir explicitamente na call de descoberta |
| ZDR exige volume mínimo contratual que ainda não temos | Média | Médio | Negociar tier Enterprise com **commit baixo** baseado no forecast de §2. Se Anthropic insistir em commit alto, escalar ao product-owner — pode ser bloqueador comercial |
| Mudança regulatória LGPD/ANPD que exija ZDR também para fixtures sintéticas | Muito baixa | Baixo | Não é threat model atual. Revisitar em revisão anual |
| Provider alternativo (OpenAI Enterprise, Vertex AI Anthropic via GCP) é necessário | Baixa | Médio | Vertex AI hosted Claude no GCP é Plano B — ZDR é nativo da relação GCP-Lintty (CMEK + project isolation). Investigar como fallback caso negociação Anthropic falhe |

---

## 6. Checklist de provisionamento técnico pós-assinatura

Executar **imediatamente após** assinatura do MSA + ZDR Addendum (alvo: 2026-05-22 a 2026-05-25). Owner: security-compliance + backend-dev-cloud.

### 6.1. Workspace e identidade

- [ ] Workspace ID Anthropic Enterprise registrado em `secrets-inventory.md` (criar se não existir)
- [ ] Confirmação visual no console Anthropic que **ZDR está ON** — screenshot anexado ao registro contratual
- [ ] Confirmar que opção "use my data to improve models" está **OFF** em todos os níveis (workspace, organization, key)
- [ ] Lista de admins do workspace limitada a 2 pessoas (founder + security-compliance designado)
- [ ] MFA obrigatório nas contas admin

### 6.2. API keys com tagging por ambiente

- [ ] Key `lintty-dev` — uso: desenvolvimento local + CI smoke tests. Tag: `env=dev`
- [ ] Key `lintty-staging` — uso: ambiente de staging GCP. Tag: `env=staging`
- [ ] Key `lintty-prod` — uso: ambiente de produção GCP. Tag: `env=prod`
- [ ] Cada key armazenada **exclusivamente** no Cloud Secret Manager do projeto correspondente (`lintty-secrets-dev`, `lintty-secrets-staging`, `lintty-secrets-prod`)
- [ ] Nenhuma key em arquivo `.env`, env var direta no Cloud Run, ou repo Git

### 6.3. Rotation policy

- [ ] Rotação automática a cada **60 dias** (alinhado com `docs/07-security-compliance.md` §4 — tabela de rotação)
- [ ] Cloud Scheduler + Cloud Function que: (a) gera nova key via Anthropic API, (b) atualiza Secret Manager, (c) revoga key antiga após 24h de overlap
- [ ] Alerta no Cloud Monitoring se rotação falhar 2x consecutivas
- [ ] Owner da rotação: backend-dev-cloud (implementação) + security-compliance (auditoria trimestral)

### 6.4. Scoping por ambiente e tagging para reconciliação de custo

- [ ] Cada chamada à Anthropic API inclui header customizado `x-lintty-env` (dev/staging/prod) e `x-lintty-scan-id` (UUID do scan, pseudo-anonimizado)
- [ ] **Sem `customer_id` real no header** — usar hash determinístico (`hmac_sha256(customer_id, salt_per_env)`) para reconciliação
- [ ] Dashboard de custo Anthropic segmentado por env via tags. Reconciliação mensal contra Cloud Billing GCP
- [ ] Alerta de gasto: >$X/dia em prod dispara PagerDuty (X a definir com llm-ops)

### 6.5. Auditoria contínua

- [ ] Job nightly verifica que ZDR continua flagado no workspace (via API se disponível, senão checagem manual mensal documentada)
- [ ] Audit event `llm.inference.requested` registrado no hash-chain (V1+) com `inference_signature` mas **sem o prompt nem a completion**
- [ ] Revisão trimestral: rotação ocorrendo, sem keys vazadas em logs, sem chamadas de fora dos service accounts esperados

### 6.6. Comunicação ao cliente

- [ ] DPA template (`dpa-template.md`) §4 lista Anthropic como Sub-Operador com cláusula ZDR contratualizada — atualizar após assinatura para incluir referência ao número do contrato Anthropic (sem expor termos confidenciais)
- [ ] Privacy Policy (`privacy-policy.md`) já lista Anthropic com ZDR — manter
- [ ] Subprocessors page (adiada para pós-primeiro contrato) lista Anthropic com data de efetivação do ZDR

---

## 7. Critério de pronto

ZDR está "pronto para piloto real" quando **todos** estes itens estão verde:

1. MSA + ZDR Addendum assinados eletronicamente, cópias arquivadas em pasta jurídica do Lintty.
2. Parecer jurídico do advogado LGPD aprovando os termos.
3. Workspace Enterprise provisionado com ZDR confirmado visualmente no console.
4. Keys provisionadas com tagging por ambiente, em Secret Manager, com rotation policy ativa.
5. DPA template e Privacy Policy referenciando o ZDR atualizado.
6. Audit event de teste registrado com sucesso (validação end-to-end do tagging).

Antes disso: **fixtures only**.
