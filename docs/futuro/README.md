# `futuro/` — planejamento V1+ (não-operacional)

Esta pasta é o **playbook aspiracional do Lintty** — a versão completa do produto, antes do pivô Zero-IA / V0 enxuto. Os documentos foram **preservados intocados** e movidos para cá quando o V0 foi redimensionado em 2026-04-30.

## Por que estão aqui e não na raiz de `docs/`

Operacionalmente, o V0 é deliberadamente menor:

- **Zero IA** (sem LLM, sem ZDR, sem advogado de defesa).
- **Zero cloud completa** (sem GCP, sem Pub/Sub, sem hash-chain externa).
- **Zero multi-tenancy** (sem dashboard, sem billing wallet, sem GitHub App).

Esses docs descrevem **o produto que queremos ser depois de validar a tese**, não o que está em campo. Manter eles na raiz de `docs/` confunde quem chega: a pessoa lê "Cloud Run + Anthropic Enterprise" e tenta operar contra essa referência, quando a realidade é "binário .NET no laptop + uma VM com `git clone`".

## Quando voltar a olhar para cá

Quando **pelo menos um** destes sinais aparecer (critério em [`../15-roadmap-curto.md`](../15-roadmap-curto.md)):

1. Piloto pagante assinado (mesmo simbólico).
2. Term sheet de investidor sério aceitando a tese.
3. 3+ prospects qualificados em pipeline avançado.

Antes disso: **não construir nada do que está aqui**. Princípio do pivô Zero-IA: a IA / GCP / SOC 2 só voltam quando houver alguém pagando para acelerar — não antes.

## O que está aqui

| Arquivo | O que descreve | Era originalmente |
|---------|----------------|-------------------|
| `product-vision-blueprint.md` | Posicionamento completo, multi-tenant, billing, Stripe, GitHub App, dashboard | `docs/01-product-vision.md` |
| `motor-roslyn-blueprint.md` | Motor com Cloud Run sandbox, semantic slicing, integração LLM, NuGet privado via GitHub App secret | `docs/03-motor-roslyn.md` |
| `llm-ops.md` | Prompts, auto-consistência, guardrails, reprodutibilidade, LNTY-004 "advogado de defesa" | `docs/04-llm-ops.md` |
| `infra-gcp.md` | GCP organização, VPC, Cloud Run, Cloud SQL, Artifact Registry, Pub/Sub, observabilidade | `docs/05-infra-gcp.md` |
| `data-and-flow.md` | Modelo de dados multi-tenant, fluxo Milestone end-to-end, fluxo PR | `docs/06-data-and-flow.md` |
| `security-compliance.md` | Modelo de ameaça, sandbox, audit hash-chain, SOC 2 Type I, LGPD avançada | `docs/07-security-compliance.md` |
| `execution-plan.md` | Plano de 6 meses com Sprints 0–6, pre-flight checklist, primeira contratação | `docs/08-execution-plan.md` |
| `roadmap-gaps.md` | Roadmap longo prazo + gaps especificados | `docs/10-roadmap-gaps.md` |
| `adr-0002-llm-sprint-1.md` | ADR de integração LLM no Sprint 1 | `docs/adr/0002-llm-sprint-1.md` |
| `compliance-zdr-anthropic-plan.md` | Playbook completo de Zero Data Retention com Anthropic | `docs/compliance/zdr-anthropic-plan.md` |
| `llm/` | Few-shots, prompts, mock verdicts, inference signature | `docs/llm/` |

## O que **não** veio para cá

Continuam em `docs/` (raiz) porque ainda valem no V0:

- `02-canon-v1.md` — as 9 regras (com 7 ativas no V0).
- `09-golden-tests.md` — Saint/Sinner/Ninja são gate de produto agora.
- `11-glossary.md` — termos seguem válidos.
- `12-sales-cut.md` — é o doc da fase comercial atual, não roadmap futuro.
- `manual-actions.md` — TODOs humanos correntes.
- `compliance/dpa-template.md`, `lawyer-briefing.md`, `privacy-policy.md` — usáveis no V0 (são compliance básico, não SOC 2).
- ADRs 0001, 0003, 0004, 0005 — decisões ativas.
- `brand/`, `sales/`, `landing/` — todos vivos.

## Importante para Claude Code

Quando você for instruído a "olhar a documentação do Lintty" para fazer qualquer trabalho do V0, **não use os docs desta pasta como referência**. Use os docs da raiz de `docs/`. Se precisar, esta pasta serve como contexto histórico ou para entender "para onde a gente está indo eventualmente" — nunca para "o que a gente está construindo agora".
