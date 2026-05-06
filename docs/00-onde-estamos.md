# 00 — Onde estamos (TL;DR)

> Documento de orientação rápida. Lê em 3 minutos. Para detalhes, segue os apontadores.

## A frase única

Lintty é um **árbitro técnico de arquitetura .NET**. O cliente entrega um `.sln`, a gente devolve um **PDF de laudo determinístico** com nota A–F e a lista de violações ao Canon (Hexagonal/DDD).

**100% determinístico. Zero IA. Zero alucinação. Mesmo input → mesmo PDF, byte por byte.**

## O que existe hoje (2026-04-30)

| Peça | Status | Onde mora |
|------|--------|-----------|
| Motor Roslyn (7 regras Canon) | ✅ Funcionando | `engine/src/Lintty.Engine.Core` |
| CLI standalone | ✅ Funcionando | `engine/src/Lintty.Engine.Cli` |
| Reporter PDF (QuestPDF, determinístico) | ✅ Funcionando | `engine/src/Lintty.Engine.Reporter` |
| Fixtures Saint / Sinner / Ninja-01 | ✅ Validados | `fixtures/the-*` |
| Landing + dashboard (Next.js, static export) | ✅ Migrado de `landing/*.html` para `frontend/` (Next 15 + TS + Tailwind 3) | `frontend/` |
| Web Inspector — backend (API + worker) | ✅ Esqueleto pronto, 26/26 testes verdes (gate de determinismo cruzado incluso) | `engine/src/Lintty.WebInspector/` |
| Web Inspector — frontend `/inspect` (4 estados + polling 2s) | ✅ Implementado em `frontend/app/inspect/`, consome `/api/jobs` | `frontend/app/inspect/` |
| Deploy Cloudflare Pages, Dockerfile, PAT-passthrough | ❌ Fora do esqueleto V0, próximos tickets | spec em `13-web-inspector.md` §12 |
| Distribuição oficial do CLI (release binário) | ❌ Não publicada | plano em `14-cli-distribution.md` |

## Os dois caminhos de uso (V0)

A gente vende uma única promessa, com **duas formas de consumi-la**:

### Caminho 1 — CLI local (Self-Service)

> Default conforme [ADR 0005](adr/0005-distribution-model.md). Código fonte **nunca sai do equipamento do cliente**.

```bash
# Cliente baixa o binário do GitHub Releases, valida sha256, e roda:
lintty-engine analyze --solution MeuProjeto.sln --pdf laudo.pdf
```

Sai um PDF assinado-por-hash do conteúdo (SHA-256 do JSON) no rodapé. Sem upload, sem login, sem cloud.

### Caminho 2 — Web Inspector (GitHub URL)

> Para repos públicos ou com PAT temporário. Conveniência: cliente só cola URL e baixa PDF.

```
1. Cliente acessa lintty.com/inspect
2. Cola "https://github.com/cliente/projeto" (commit/branch opcional)
3. Backend clona shallow (--depth=1), roda o mesmo CLI, devolve PDF
4. Clone é descartado depois do scan; nada persiste
```

Mesmo motor, mesmo Canon, mesmo PDF. Spec completa em [`13-web-inspector.md`](13-web-inspector.md).

## O que NÃO faz parte do V0

Tudo abaixo está **preservado em [`futuro/`](futuro/)** como playbook V1+. **Não construir antes de sinal comercial:**

- Camada LLM (LNTY-004 / 005, "advogado de defesa") — `futuro/llm-ops.md`
- Infraestrutura GCP completa (Cloud Run, Pub/Sub, Cloud SQL, hash-chain) — `futuro/infra-gcp.md`
- Multi-tenancy, billing wallet, Stripe — `futuro/data-and-flow.md`
- PAdES + TSA, SOC 2, audit chain imutável — `futuro/security-compliance.md`
- GitHub App registrado, dashboard self-service, painel de tendências — `futuro/product-vision-blueprint.md`

## Próximas 8–12 semanas

Detalhe em [`15-roadmap-curto.md`](15-roadmap-curto.md). Em uma frase: **publicar release oficial do CLI + montar o Web Inspector mínimo + colocar a landing no ar com link funcional para os dois caminhos.**

## Onde ir agora

- **Quero entender o produto:** [`01-product-vision.md`](01-product-vision.md)
- **Quero ver as 7 regras ativas:** [`02-canon-v1.md`](02-canon-v1.md)
- **Vou mexer no motor / CLI:** [`03-motor-cli.md`](03-motor-cli.md)
- **Vou montar o Web Inspector:** [`13-web-inspector.md`](13-web-inspector.md)
- **Vou empacotar / distribuir o CLI:** [`14-cli-distribution.md`](14-cli-distribution.md)
- **Quero o roadmap real (próximas semanas):** [`15-roadmap-curto.md`](15-roadmap-curto.md)
- **Vou fazer demo / pitch:** [`12-sales-cut.md`](12-sales-cut.md)
