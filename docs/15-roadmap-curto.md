# 15 — Roadmap Curto (próximas 8–12 semanas)

> Substitui o "Plano de Execução de 6 meses" antigo (preservado em [`futuro/execution-plan.md`](futuro/execution-plan.md)) e o "Roadmap & Gaps" longo ([`futuro/roadmap-gaps.md`](futuro/roadmap-gaps.md)) por um plano operacional de **8–12 semanas**, alinhado ao escopo real do V0.

## Princípio

A prioridade é simples: **do "motor pronto + landing parada" para "alguém consegue usar o produto e nos pagar"**.

Tudo abaixo é construído sob estas restrições:
- **Solo (1 pessoa)**, sem contratação no horizonte do roadmap.
- Custo operacional **< US$ 50/mês** até o primeiro piloto pagante.
- **Zero IA, zero GCP** completo. Uma VM ou Cloud Run single-service basta para o Web Inspector.
- Cada item do roadmap é descartável: se não move agulha de fechar piloto/investidor, sai.

## Estado de partida (semana 0 — 2026-04-30)

✅ **Pronto:**
- Motor Roslyn determinístico passando em Saint / Sinner / Ninja-01.
- 7 regras Canon ativas (LNTY-001/002/003/006/007/008/009).
- PDF reporter via QuestPDF, gate de determinismo verde.
- White-label brand PDF template.
- Landing estática em `landing/` (não publicada).
- Política de Privacidade rascunhada.

❌ **Não pronto:**
- Release oficial do CLI (sem binários publicados).
- Web Inspector (não construído).
- Domínio `lintty.com` (não comprado / não apontado).
- Repos públicos `lintty-demo/the-*` (fixtures locais, não publicados).
- DPA revisado por advogado.
- Pitch deck final em formato de slides.

## Sprint 1 (semanas 1–2): publicar o que já existe

**Objetivo:** sair do limbo "tudo pronto mas nada acessível".

| Tarefa | Saída | Doc de referência |
|--------|-------|-------------------|
| Comprar `lintty.com` + apontar Cloudflare Pages | Domínio resolve | — |
| Publicar `landing/` no Cloudflare Pages | `lintty.com` no ar | `landing/README.md` |
| Criar org GitHub `lintty/` (ou usar a existente) | Organização pública criada | — |
| Publicar fixtures como repos públicos `lintty-demo/the-saint`, `lintty-demo/the-sinner`, `lintty-demo/the-ninja-01` | 3 repos públicos cloneáveis | `fixtures/the-*` |
| Workflow `release.yml` no repo `lintty/lintty-engine` (push de tag → 4 binários + sha256) | CI funciona, primeira release `v0.1.0` no ar | `14-cli-distribution.md` |
| README do repo `lintty-engine` + página `lintty.com/cli` | Cliente consegue baixar e validar | `14-cli-distribution.md` §7 |

**Critério de "feito":** abro um navegador anônimo, vou em `lintty.com`, clico "baixar CLI", baixo o `.exe`, valido sha256, rodo contra um clone de `the-sinner`, abro o PDF. Tudo em < 5 min sem documentação prévia.

## Sprint 2 (semanas 3–5): Web Inspector mínimo

**Objetivo:** segundo caminho de uso funcionando para os 3 fixtures.

Spec completa em [`13-web-inspector.md`](13-web-inspector.md).

| Tarefa | Saída |
|--------|-------|
| Esqueleto ASP.NET Core minimal API + SQLite + página de form | Stub local rodando |
| Worker subprocess: clone shallow + invocar CLI + cleanup | Job local end-to-end com fixture |
| Validações de URL, tamanho, rate-limit por IP | Erros amigáveis |
| Frontend simples (HTML+JS, sem framework) com polling de status | UX completa |
| Deploy em VM ou Cloud Run + subdomain `lintty.com/inspect` | URL pública |
| Testes manuais com os 3 fixtures via URL pública | Os 3 fixtures retornam PDF correto |
| **Gate de determinismo cruzado**: PDF do Web Inspector é byte-idêntico ao do CLI local | Bug-zero antes do go-live |

**Critério de "feito":** abro a `lintty.com/inspect`, colo `https://github.com/lintty-demo/the-sinner`, em < 2 min recebo o `laudo.pdf` com score F. PDF é idêntico ao gerado localmente pelo CLI.

## Sprint 3 (semanas 6–7): material de venda + outbound

**Objetivo:** ter o que mostrar em call de prospect e em pitch a investidor.

| Tarefa | Saída | Doc de referência |
|--------|-------|-------------------|
| Pitch deck v1 em formato real (Keynote / Slides), 15 slides | PDF + arquivo editável | [`docs/sales/pitch-deck.md`](sales/pitch-deck.md) |
| Talk-track ensaiado (12 min) com fixtures rodando ao vivo | Vídeo de ensaio | [`docs/sales/talk-track.md`](sales/talk-track.md) |
| Screencast de backup (~5 min) | MP4 hospedado | — |
| Página `lintty.com` polida com OG image + seção "Como funciona" + CTAs para CLI e Web Inspector | Landing v1.0 | [`docs/sales/landing-copy.md`](sales/landing-copy.md) |
| Lista de 20 prospects qualificados (varejo BR enterprise, terceiriza dev .NET) + emails de outbound | Sheet + templates prontos | [`docs/manual-actions.md`](manual-actions.md) |

**Critério de "feito":** a página `lintty.com` está em pé, a primeira reunião com prospect está marcada, o pitch deck imprime sem erro de formatação.

## Sprint 4 (semanas 8–10): primeiro piloto e jurídico

**Objetivo:** assinar piloto pagante (mesmo simbólico) ou ter feedback duro de que o produto não está pronto.

| Tarefa | Saída | Doc de referência |
|--------|-------|-------------------|
| Reuniões com prospects, demo, follow-up | 5+ calls feitas, 2+ aprofundadas | — |
| Iterar landing / deck conforme feedback | v1.1, v1.2 | — |
| Revisar DPA com advogado especialista LGPD | DPA enxuto pronto para assinar | [`compliance/lawyer-briefing.md`](compliance/lawyer-briefing.md) |
| Política de Privacidade revisada para mencionar Web Inspector explicitamente | Versão final no ar | [`compliance/privacy-policy.md`](compliance/privacy-policy.md) |
| Calibrar pricing com o primeiro piloto (estrutura por LoC) | Tabela de pricing concreta (privada) | — |

**Critério de "feito":** ou (a) tem piloto pagante assinando, (b) tem term sheet de investidor, (c) tem 3+ prospects qualificados em pipeline avançado, ou (d) está claro que precisa pivotar antes de construir mais.

## Sprint 5+ (semanas 11–12): respiração e calibração

Reservar 2 semanas para:
- Fix de bugs de campo no CLI / Web Inspector reportados pelos primeiros usuários.
- Iterar copy / posicionamento se o feedback do Sprint 4 sugerir.
- Decidir se entra V1 (LLM, dashboard, multi-tenant) ou se itera mais o V0.

## Itens fora do roadmap curto

Tudo abaixo está em [`futuro/`](futuro/) e **não é construído antes de sinal comercial claro**:

- Reativar camada LLM (LNTY-004/005, ZDR Anthropic) → `futuro/llm-ops.md`
- Subir GCP completo (Terraform, VPC, Cloud SQL, Pub/Sub, audit hash-chain) → `futuro/infra-gcp.md`
- Dashboard self-service multi-tenant → `futuro/data-and-flow.md`
- Stripe + wallet de créditos + faturamento automático → `futuro/data-and-flow.md`
- PAdES + TSA, SOC 2 Type I → `futuro/security-compliance.md`
- VS Code Extension → V1+ (mencionado em [ADR 0005](adr/0005-distribution-model.md) §2.2)
- Suporte a Java/Spring, frontend, mobile → V2+

## Riscos do roadmap

| Risco | Mitigação |
|-------|-----------|
| Sprint 2 derrapar (Web Inspector mais complexo do que parece) | Tirar features do escopo (fila distribuída, frontend bonito) — manter só o "caminho feliz". |
| Não conseguir agendar reuniões no Sprint 4 | Começa o outbound já no Sprint 3 (prep paralelo). |
| Cliente pedir feature de V1+ na primeira reunião | Resposta pronta: "Está no roadmap. Vamos validar primeiro a tese com o V0." Não construir sob promessa antes de assinatura. |
| Bug de não-determinismo aparecer no cross-platform release | Investigar antes de qualquer deploy — é o gate da venda. Vários dias se necessário. |
| Solo founder ficar sem fôlego | Sprint 5+ existe exatamente para isso. Não comprometer mais 8 semanas seguidas sem janela de respiração. |

## O que medir

Métricas de tração no V0 (uma planilha, não um dashboard):

| Métrica | Meta no fim das 12 semanas |
|---------|----------------------------|
| Downloads do CLI | 50+ |
| Scans rodados via Web Inspector | 100+ |
| Calls de discovery feitas | 10+ |
| Pilotos pagantes (mesmo simbólico) | ≥ 1 |
| Term sheet ou LOI de investidor | ≥ 1 (sinal alternativo) |

Atingiu pelo menos uma das duas últimas → entra Sprint V1. Não atingiu → calibra ou pivote (mas **não constrói mais V0** no escuro).
