# Lintty — Documentação (V0)

**Status:** V0 em construção. Motor + PDF prontos. Falta release oficial do CLI e Web Inspector. Plano em [`15-roadmap-curto.md`](15-roadmap-curto.md).

Lintty é um **árbitro técnico de arquitetura .NET**. Recebe um `.sln`, aplica o Canon (Hexagonal/DDD), devolve um **PDF de laudo determinístico** com nota A–F.

**Promessa central:** 100% determinístico, zero IA, zero alucinação, mesmo input → mesmo PDF byte por byte.

> **Importante:** o V0 atual é deliberadamente enxuto. A versão completa do blueprint (com LLM, GCP, multi-tenant, billing, SOC 2, PAdES, audit hash-chain) está preservada em [`futuro/`](futuro/) como roadmap aspiracional — **não é referência operacional.**

---

## Por onde começar

| Você quer... | Vá para |
|--------------|---------|
| Visão geral em 3 minutos | [`00-onde-estamos.md`](00-onde-estamos.md) |
| Entender o produto (V0 real) | [`01-product-vision.md`](01-product-vision.md) |
| Ver as 7 regras ativas e como pontuamos | [`02-canon-v1.md`](02-canon-v1.md) |
| Mexer no motor / CLI | [`03-motor-cli.md`](03-motor-cli.md) |
| Construir o **Web Inspector** (GitHub URL → PDF) | [`13-web-inspector.md`](13-web-inspector.md) |
| Empacotar e distribuir o CLI | [`14-cli-distribution.md`](14-cli-distribution.md) |
| Roadmap real das próximas 8–12 semanas | [`15-roadmap-curto.md`](15-roadmap-curto.md) |
| Pitch / venda / talk-track | [`12-sales-cut.md`](12-sales-cut.md) + `sales/` |
| ADRs (decisões arquiteturais) | [`adr/`](adr/) |
| Glossário de termos | [`11-glossary.md`](11-glossary.md) |
| Estratégia de testes (golden suite) | [`09-golden-tests.md`](09-golden-tests.md) |
| Compliance / DPA / advogado | [`compliance/`](compliance/) |
| Identidade visual / PDFs institucionais | [`brand/`](brand/) |
| Lista de TODOs humanos pendentes | [`manual-actions.md`](manual-actions.md) |

---

## Índice

### V0 (referência operacional)

| # | Documento | Conteúdo |
|---|-----------|----------|
| 00 | [Onde estamos (TL;DR)](00-onde-estamos.md) | Status atual, 2 caminhos de uso, próximos passos |
| 01 | [Visão e Modelo (V0)](01-product-vision.md) | Posicionamento real, dois caminhos, mecânica de disputa |
| 02 | [Canon v1.0](02-canon-v1.md) | As 7 regras ativas, score, supressões, hard locks |
| 03 | [Motor CLI](03-motor-cli.md) | Pipeline, contrato CLI, determinismo, layer tagging |
| 09 | [Golden Tests](09-golden-tests.md) | Saint, Sinner, Ninja — gate de qualidade |
| 11 | [Glossário](11-glossary.md) | Termos e acrônimos |
| 12 | [Sales Cut](12-sales-cut.md) | Roteiro de demo, deck, materiais de venda |
| 13 | [Web Inspector](13-web-inspector.md) | Spec do fluxo "GitHub URL → PDF" |
| 14 | [Distribuição CLI](14-cli-distribution.md) | Empacotamento, GitHub Releases, sha256 |
| 15 | [Roadmap Curto](15-roadmap-curto.md) | Próximas 8–12 semanas, sprint a sprint |

### ADRs (decisões travadas)

| ADR | Decisão |
|-----|---------|
| [0001](adr/0001-motor-skeleton.md) | Esqueleto do motor, contrato JSON, exit codes |
| [0003](adr/0003-pdf-reporter.md) | PDF Reporter via QuestPDF, hash_content, fontes embedded |
| [0004](adr/0004-brand-pdf-template.md) | Template white-label de PDF institucional |
| [0005](adr/0005-distribution-model.md) | CLI Self-Service como default; Concierge como fallback |

> ADR 0002 (LLM Sprint 1) está em [`futuro/`](futuro/) — fora de escopo no V0.

### Subpastas

- [`sales/`](sales/) — pitch deck, talk track, landing copy, mock do laudo
- [`compliance/`](compliance/) — DPA template, briefing para advogado, política de privacidade
- [`brand/`](brand/) — identidade visual e padrão de PDF institucional
- [`adr/`](adr/) — Architecture Decision Records ativos

---

## `futuro/` — planejamento V1+ (não construir ainda)

Tudo em [`futuro/`](futuro/) é **roadmap aspiracional preservado**, não referência operacional. Não construir antes de sinal comercial claro (piloto pagante, term sheet, ou 3+ prospects qualificados).

| Arquivo | Era... |
|---------|--------|
| [`futuro/product-vision-blueprint.md`](futuro/product-vision-blueprint.md) | `01-product-vision.md` original (versão completa com LLM, dashboard, billing) |
| [`futuro/motor-roslyn-blueprint.md`](futuro/motor-roslyn-blueprint.md) | `03-motor-roslyn.md` original (com Cloud Run sandbox + semantic slicing LLM) |
| [`futuro/llm-ops.md`](futuro/llm-ops.md) | LNTY-004 "advogado de defesa", prompts, ZDR Anthropic |
| [`futuro/infra-gcp.md`](futuro/infra-gcp.md) | GCP completo: VPC, Cloud Run, Cloud SQL, Pub/Sub |
| [`futuro/data-and-flow.md`](futuro/data-and-flow.md) | Multi-tenancy, billing wallet, fluxo Milestone end-to-end |
| [`futuro/security-compliance.md`](futuro/security-compliance.md) | SOC 2, audit hash-chain, modelo de ameaça, sandbox |
| [`futuro/execution-plan.md`](futuro/execution-plan.md) | Plano de execução de 6 meses com Sprints 0–6 completos |
| [`futuro/roadmap-gaps.md`](futuro/roadmap-gaps.md) | Roadmap de longo prazo + gaps especificados |
| [`futuro/adr-0002-llm-sprint-1.md`](futuro/adr-0002-llm-sprint-1.md) | ADR de integração LLM no Sprint 1 |
| [`futuro/compliance-zdr-anthropic-plan.md`](futuro/compliance-zdr-anthropic-plan.md) | Playbook de Zero Data Retention com Anthropic |
| [`futuro/llm/`](futuro/llm/) | Few-shots, prompts, mock verdicts |

---

## Princípios fundadores (V0)

1. **Verdade arquitetural, não opinião.** Determinismo onde possível; IA fica em V1+ para zonas cinza, com guardrails.
2. **Operação enxuta.** Sem mediação humana. Disputas resolvidas via hard locks, caps de supressão e transparência total no PDF.
3. **Opinionado por design.** Canon prescritivo (Hexagonal/DDD), com toggles. Sem custom rules no V0.
4. **Local-first.** Caminho default é o cliente rodar o CLI no equipamento dele. Web Inspector é conveniência adicional.
5. **Custo idle próximo de zero.** Sem cloud no V0; Web Inspector cabe em 1 VM ou 1 Cloud Run service.

---

## Recomendação prática

Se você ainda **não tem um piloto pagante ou term sheet**, fica nos docs do V0. O Blueprint completo (em `futuro/`) é a visão de longo prazo — **não construir antes de validar.**
