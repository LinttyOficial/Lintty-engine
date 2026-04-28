# Lintty — Blueprints v1.0

**Status:** Design conceitual completo. Todos os 4 eixos travados. Implementação ainda não iniciada.

Lintty é uma plataforma SaaS B2B que atua como **árbitro técnico de arquitetura de software** entre empresas contratantes (enterprise) e agências de desenvolvimento terceirizadas. O produto emite laudos arquiteturais com peso probatório, baseados em análise estática profunda (Roslyn) combinada com inferência semântica seletiva via IA (Anthropic Claude).

Lintty **não é** escrow financeiro, **não é** árbitro jurídico e **não tem** equipe de mediação humana. É um oráculo técnico cujo laudo é usado pelas partes envolvidas sob responsabilidade própria.

---

## Índice da documentação

| # | Documento | Conteúdo |
|---|-----------|----------|
| 01 | [Visão e Modelo de Produto](01-product-vision.md) | Posicionamento, mercado, modelo de receita, atores, mecânica de disputa |
| 02 | [Canon v1.0](02-canon-v1.md) | As 9 regras travadas, governança via `lintty.yml`, score, supressões |
| 03 | [Motor Roslyn](03-motor-roslyn.md) | Pipeline de análise, algoritmos por regra, semantic slicing, contrato de saída |
| 04 | [LLM Ops](04-llm-ops.md) | Provedor, prompts, auto-consistência, guardrails, reprodutibilidade |
| 05 | [Infraestrutura GCP](05-infra-gcp.md) | Organização, rede, compute, dados, mensageria, CI/CD, observabilidade |
| 06 | [Modelo de Dados e Fluxo](06-data-and-flow.md) | Entidades, multi-tenancy, fluxo Milestone end-to-end, fluxo PR |
| 07 | [Segurança e Compliance](07-security-compliance.md) | Modelo de ameaça, sandbox, audit hash-chain, SOC 2, LGPD |
| 08 | [Plano de Execução](08-execution-plan.md) | Pre-flight checklist, sprints, primeira contratação |
| 09 | [Golden Tests](09-golden-tests.md) | Estratégia de teste, casos Saint/Sinner/Ninja, progressão |
| 10 | [Roadmap e Gaps](10-roadmap-gaps.md) | O que está especificado, o que falta, o que está adiado |
| 11 | [Glossário](11-glossary.md) | Termos, acrônimos e conceitos centrais |
| 12 | [Sales Cut](12-sales-cut.md) | **Versão enxuta para validar a tese antes do MVP completo** (4-7 semanas vs 6 meses) |

---

## Status dos eixos de design

| Eixo | Status | Documento principal |
|------|--------|---------------------|
| Canon v1.0 (regras) | ✅ LOCKED | `02-canon-v1.md` |
| Motor Roslyn (extração determinística) | ✅ LOCKED | `03-motor-roslyn.md` |
| Infraestrutura GCP (cofre) | ✅ LOCKED | `05-infra-gcp.md` |
| LLM Ops (cérebro semântico) | ✅ LOCKED | `04-llm-ops.md` |
| Dashboard UX | 🟡 ADIADO (após Sprint 2) | — |
| Onboarding/Billing UX | 🟡 ADIADO (Sprint 3+) | — |
| API pública | 🔴 v1+ | — |

---

## Princípios fundadores

1. **Verdade arquitetural, não opinião.** Determinismo onde possível; IA apenas para zonas cinza, com guardrails e reprodutibilidade.
2. **Operação enxuta.** Sem mediação humana. Disputas são desenhadas para fora via hard locks, caps de supressão e transparência total no PDF.
3. **Opinionado por design.** Canon prescritivo (Hexagonal/DDD), com toggles. Sem custom rules no MVP.
4. **Custo idle próximo de zero.** Serverless (Cloud Run), Pub/Sub, GCS — escala com uso, não com infra.
5. **Compliance desde o dia 1.** SOC 2 Type I no mês 12 é meta; controles iniciais já apontam para lá.

---

## Como navegar

- **Estou validando a tese (pre-revenue):** comece por **`12-sales-cut.md`** — versão enxuta de 4-7 semanas para vender a ideia antes de construir o MVP completo.
- **Quero entender o produto rapidamente:** `01-product-vision.md`.
- **Quero ver as regras técnicas em detalhe:** `02-canon-v1.md`.
- **Vou implementar o motor:** `03-motor-roslyn.md` + `09-golden-tests.md`.
- **Vou desenhar prompts/IA:** `04-llm-ops.md`.
- **Vou subir a infra:** `05-infra-gcp.md` + `07-security-compliance.md`.
- **Quero o roadmap completo e o que falta:** `08-execution-plan.md` + `10-roadmap-gaps.md`.

> **Recomendação prática:** se você ainda não tem 1 piloto pagante ou term sheet, **siga o `12-sales-cut.md`**. O Blueprint completo (docs 01-11) é a visão de longo prazo — não construa antes de validar.
