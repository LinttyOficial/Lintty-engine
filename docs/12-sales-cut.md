# 12 — Sales Cut: a versão para vender a ideia

> **Pivô em 2026-04-27 — Zero-IA, Zero-Custo.** O Sales Cut deixou de incluir a camada LLM (LNTY-004 e o "advogado de defesa"). Demo, primeiro piloto e materiais de venda rodam **somente com motor Roslyn type-aware**. Argumento de venda fica mais forte: 100% determinístico, zero alucinação, zero custo de inferência, mesmo input → mesmo PDF bit-a-bit. A IA volta como roadmap **V1+**, sob ZDR contratualizado, após sinal de "go" comercial.
>
> **Propósito do documento:** documentar o **escopo mínimo viável** para validar comercialmente a tese do Lintty antes de investir os 6 meses do Production MVP completo.
>
> **Princípio operacional:** validar antes de construir. Se ninguém quer comprar, o blueprint completo é over-engineering. Se um piloto disser "eu pagaria por isso", aí sim faz sentido reativar IA, GCP e SOC 2.

---

## 1. Dois públicos, um cut

O Sales Cut serve **dois cenários** levemente diferentes:

| Cenário | Foco | O que enfatizar |
|---------|------|-----------------|
| **Pitch a investidor** | Tamanho do mercado, tese, defensibilidade técnica | Demo curta + slides + roadmap + ask |
| **Demo a primeiro piloto** | "Isso resolve minha dor real?" | Demo extensa + PDF real + conversa sobre primeira entrega |

A boa notícia: o **mesmo conjunto de artefatos serve aos dois**. Só muda a ênfase na conversa.

---

## 2. O que TEM que funcionar de verdade (Tier 1)

Quatro coisas. Sem elas, não tem demo crível.

### 2.1. Motor Roslyn standalone (Sprint 0 — DONE em 2026-04-27)

CLI que roda no laptop e produz JSON factual.

- Passa em **Saint** (score A, 0 violações).
- Passa em **Sinner** (score F com violação por regra ativa, 3 hard locks).
- Passa em **Ninja #1 — Constant-Folded SQL** (a peça que diferencia "linter sério" de "regex envernizado").

**Regras ativas no Sales Cut:** LNTY-001, 002, 003, 006, 007, 008, 009 (7 regras determinísticas Roslyn).
**Regras desativadas no Sales Cut:** LNTY-004 e LNTY-005 (LLM-required → V1+).

**Por que é essencial:** prova matematicamente que a tecnologia funciona. Em 5 minutos de demo, o comprador entende.

### 2.2. PDF de laudo gerado pelo motor (Sprint 1 — substitui o slot da IA)

PDF gerado pelo próprio motor, a partir do JSON factual, via **QuestPDF** (.NET, MIT). Determinístico bit-a-bit (fontes embedded, sem dependência externa). Substitui o "PDF mock em Word/Figma" da versão anterior do Sales Cut — agora o PDF é artefato vivo do motor, não imagem estática.

**Conteúdo:**
- Capa com score grande (ex: "F — selo não emitido").
- Sumário executivo.
- Lista de violações com snippets de código por linha.
- Sumário de Exceções na primeira página (autor git + justificativa).
- Grafo de dependências (alto nível).
- Rodapé técnico com `canon_version`, `run_id`, `hash_pdf` (sha256 do próprio binário). **Sem `inference_signature`** — não há LLM no pipeline.

**Restrições do Sales Cut:**
- **Sem assinatura digital real** (PAdES + TSA é V1+). Slide diz: "PDF de produção será assinado digitalmente com timestamp de TSA confiável."
- **Sem hash-chain imutável** em produção (V1+). Hash do PDF aparece no rodapé como evidência de integridade local.

**Esforço:** 1-2 semanas (Sprint 1). Spec em `docs/adr/0003-pdf-reporter.md`.

**Por que é essencial:** **é o artefato vendável**. Cliente compra a ideia de receber este documento ao final de cada Milestone.

### 2.3. Pitch deck

10-15 slides. Estrutura em §6.

**Esforço:** 1 semana incluindo iterações.

### 2.4. Três fixtures públicos para demo ao vivo

`saint-csharp`, `sinner-csharp` e `ninja-01-csharp` em repos públicos no GitHub que você possa clonar **na frente do comprador** e rodar:

```bash
git clone https://github.com/lintty-demo/the-saint
./lintty-engine analyze --solution the-saint/Saint.sln --pdf laudo-saint.pdf
# Score: 100/A — laudo-saint.pdf gerado ✓

git clone https://github.com/lintty-demo/the-sinner
./lintty-engine analyze --solution the-sinner/Sinner.sln --pdf laudo-sinner.pdf
# Score: 0/F (10 violações, 3 hard locks) — laudo-sinner.pdf gerado ✓

git clone https://github.com/lintty-demo/the-ninja-01
./lintty-engine analyze --solution the-ninja-01/Ninja01.sln --pdf laudo-ninja.pdf
# Score: 25/F (3 violações LNTY-002 via constant folding) ✓
```

Drama visual: ver a saída brutal em tempo real **e abrir o PDF na hora** é convincente.

**Esforço:** já incluído no Sprint 0 (fixtures prontos, falta só publicar nos repos `lintty-demo/*`).

---

## 3. O que ajuda mas não bloqueia (Tier 2)

Adicione **se você tem 1-2 semanas extras**.

### 3.1. Mockup do dashboard em Figma

Telas estáticas mostrando o painel executivo do contratante. Não precisa estar implementado — só desenhado.

**Quando vale a pena:** sempre. É barato e ajuda a comunicar a UX final. **Esforço:** 3-5 dias.

### 3.2. Landing page polida + publicada

Uma página HTML estática em `lintty.com`. Já existe em `landing/index.html`; falta domínio + hospedagem (Cloudflare Pages recomendado).

- Headline forte
- Como funciona (3 passos)
- Saint vs Sinner como exemplo visual
- "Solicite uma demo" → Tally/Formspree

**Esforço:** 1-2 dias para configurar host + domínio. **Quando vale a pena:** sempre que houver outbound.

### 3.3. Screencast de backup da demo

Vídeo de ~5 min rodando os 3 fixtures. Backup caso o motor falhe ao vivo — troca para vídeo sem perder ritmo.

**Esforço:** 1 dia. **Quando vale a pena:** sempre antes da primeira reunião com prospect frio.

---

## 4. O que sai do escopo nesta fase

Tudo abaixo é **parte do Blueprint v1.0 mas não é necessário para vender a ideia.** Vira slide, talking point, ou simplesmente espera.

### 4.1. Camada LLM (NOVA — pivô 2026-04-27)

| Item | Tratamento |
|------|-----------|
| **LNTY-004 (advogado de defesa LLM)** | **V1+.** Spec preservada em `docs/futuro/llm-ops.md` e `docs/futuro/adr-0002-llm-sprint-1.md`. Vira slide de roadmap. |
| **LNTY-005 (Anemic Domain LLM)** | V1+. Mesma razão. |
| **Anthropic Enterprise + ZDR** | **V1+.** `docs/futuro/compliance-zdr-anthropic-plan.md` preservado como playbook. Sem LLM no Sales Cut, ZDR não é pré-requisito. |
| **Orquestrador Python** | V1+. Diretório `orchestrator/` mantido como stub. |

### 4.2. Infraestrutura

| Item | Tratamento na fase de venda |
|------|------------------------------|
| GCP completo (Terraform, VPC, Cloud Run, Cloud SQL) | Roda tudo no laptop |
| GitHub App registrado | Demo via clone manual em repo público |
| Pub/Sub, Audit hash-chain | Vira slide ("Como garantimos imutabilidade") |
| SOC 2 Type I | Slide ("Roadmap: SOC 2 Type I no mês 12") |
| Multi-region, DR drills | Slide |
| CI/CD GitHub Actions com WIF | Compila localmente |

### 4.3. Billing e onboarding

| Item | Tratamento |
|------|-----------|
| Stripe integrado | Negocia piloto com handshake; emite NF-e manualmente |
| Pricing real definido em moeda | **Sob consulta no piloto.** Sem créditos LLM (não há LLM), modelo simplifica para R$ por scan baseado em LoC — calibrar com primeiro prospect |
| Onboarding self-service | Você instala/configura pessoalmente |
| Email templates | Gmail mesmo |
| NF-e/NFS-e provider | Resolve quando virar receita recorrente |

### 4.4. Compliance e segurança

| Item | Tratamento |
|------|-----------|
| DPA template revisado por advogado | **Faça mesmo assim.** Custa pouco e é leve hoje (~1 semana com advogado). Necessário porque ainda processamos código fonte do cliente, mesmo sem LLM |
| Política de Privacidade + Termos | Versão básica é necessária se houver landing page |
| Subprocessors page | Slide; vira página real quando primeiro cliente assinar contrato |
| Pentest, bug bounty | V1+, slide |
| Anthropic Enterprise + ZDR | **V1+.** Sem LLM no Sales Cut, ZDR sai da linha crítica |
| PAdES qualified certificate | V1+. PDF gerado pelo motor (sem assinatura) dá conta. Slide diz: "PDF de produção será assinado digitalmente com timestamp de TSA RFC 3161" |

### 4.5. Produto

| Item | Tratamento |
|------|-----------|
| Dashboard web funcional | Mockup Figma + CLI demo |
| Painel de tendências | Slide com mock |
| Continuous Feedback (PR scans subscription) | Roadmap |
| Visualização de grafo de dependências | Slide com imagem de exemplo (gerada pelo motor é V1) |
| Suporte a Java/Spring, frontend, mobile | Roadmap V1+ |
| Custom rules pelo cliente | Roadmap V2+ |

---

## 5. Roteiro da demo (12 minutos)

Demo encurtada de 15 → 12 min com a saída do bloco LLM. Em formato de talk-track para venda. Adapte ao tempo disponível.

### 0:00–1:00 — Hook e problema

> "Quando uma empresa enterprise terceiriza desenvolvimento, ela só descobre se a arquitetura foi bem feita quando o sistema falha em produção. Code reviews manuais cobrem 5%, demoram dias, e revisores não conseguem ver o todo. O resultado é que muitos contratos são pagos sem que ninguém tenha validado o que importa: a estrutura do que foi construído."

### 1:00–2:30 — O que é Lintty (positioning)

> "Lintty é um árbitro técnico automatizado para entregas de software .NET. Ele recebe o código, roda análise arquitetural type-aware com o compilador Roslyn, e emite um laudo PDF que vale como evidência de aceite — sem litígio, sem mediação humana, sem espaço para discordância subjetiva."
>
> Diferencial: "Não somos escrow financeiro nem advogado. Somos o oráculo técnico determinístico. **Mesmo input, mesmo PDF, bit a bit. Zero alucinação.** Verdade arquitetural — não opinião."

### 2:30–8:30 — Demo ao vivo

```
$ git clone https://github.com/lintty-demo/the-saint
$ lintty-engine analyze --solution the-saint/Saint.sln --pdf laudo-saint.pdf

→ Output JSON: 0 violações, score 100, grade A
→ laudo-saint.pdf (abrir na hora)
```

> "Esse é um código bem-feito. Lintty não inventa problemas onde não tem. Repare no PDF: capa A, sumário executivo limpo, página de exceções vazia."

```
$ git clone https://github.com/lintty-demo/the-sinner
$ lintty-engine analyze --solution the-sinner/Sinner.sln --pdf laudo-sinner.pdf

→ Output: 10 violações, 3 hard locks Críticas, grade F
→ laudo-sinner.pdf (abrir na hora)
```

> "Aqui o código tem problemas estruturais. Repare na regra LNTY-002 — SQL no domínio. E aqui na LNTY-007 — ciclo entre projetos. Tudo evidenciado no PDF com arquivo, linha, e snippet do código violador."

```
$ git clone https://github.com/lintty-demo/the-ninja-01
$ lintty-engine analyze --solution the-ninja-01/Ninja01.sln --pdf laudo-ninja.pdf

→ Output: 3 violações LNTY-002 detectadas via constant folding
```

> "Esse é o caso interessante. O código tenta esconder SQL via concatenação de constantes — `'SE' + 'LECT'`. Linters tradicionais com regex passam isso como limpo. Lintty detecta porque usa o compilador Roslyn de verdade, com análise type-aware. **Tudo determinístico. Sem IA. Sem alucinação. Mesma entrada → mesmo PDF, sempre.**"

### 8:30–10:00 — Mostrar o PDF de perto

> "Esse é o artefato vendável. Capa com score, lista de violações com snippets, sumário de exceções na primeira página com email do dev e justificativa, grafo de dependências, hash sha256 do próprio PDF no rodapé como integridade local."
>
> "Em produção, esse PDF é assinado digitalmente com PAdES e timestamp de TSA — peso jurídico real. No Sales Cut, sem assinatura ainda; o conteúdo já é o conteúdo final."
>
> "É isso que o cliente recebe ao final de cada Milestone de entrega. É isso que vai junto do contrato como prova técnica de aceite."

### 10:00–11:00 — Modelo de negócio e roadmap

> "Cobrança por scan de Milestone. Tier por tamanho de código — calibração final com o primeiro piloto, não escondemos o número."
>
> "Roadmap: V1 adiciona dashboard self-service, PDF assinado digitalmente, audit hash-chain imutável, e a camada LLM opcional para regras semânticas como 'lógica de negócio dentro do repositório' — sob ZDR contratual com Anthropic. Hoje, no Sales Cut, **rodamos 100% determinístico** — escolha técnica, não falta."

### 11:00–12:00 — Defensibilidade e ask

> "Defensibilidade: canon opinionado, determinismo bit-a-bit, fixtures públicos verificáveis, motor Roslyn type-aware (não regex). Roadmap inclui PDF assinado e LLM opcional como complemento, não como dependência."

Para investidor: "Buscamos $X para 12-18 meses até validar 50 contratos."
Para piloto: "Topa rodar uma entrega real com a gente? A gente calibra a primeira auditoria e devolve se não agregar."

---

## 6. Estrutura do pitch deck

15 slides, ~30 segundos por slide:

| # | Slide | Conteúdo |
|---|-------|----------|
| 1 | **Title** | Lintty — Arquitetura como evidência. Founder + contato |
| 2 | **The Problem** | Enterprise terceiriza, mas não consegue auditar entrega arquitetural |
| 3 | **Today's Approach** | Code reviews manuais, 5% de cobertura, dias por revisão, miss the forest for the trees |
| 4 | **Lintty Solution** | Motor automatizado type-aware + laudo PDF + 0 mediação humana |
| 5 | **How It Works (Pipeline)** | **Code → Roslyn → JSON → PDF.** 4 caixas. Determinístico end-to-end |
| 6 | **Live Demo Frame** | "Vou mostrar agora" (transição para terminal) |
| 7 | **The Laudo** | Imagem da capa do PDF gerado pelo motor (Sinner F) |
| 8 | **Why Now** | Hexagonal/DDD maduro + Roslyn type-aware viável + outsourcing crescente em LATAM |
| 9 | **Defensibility** | Opinionated canon, **determinismo bit-a-bit (zero alucinação)**, ephemeral analysis, audit hash-chain (V1+), signed PDF (V1+) |
| 10 | **Business Model** | Pay-per-Scan tiered. Quem paga: contratante. Subscription opcional |
| 11 | **Pricing** | Tier por LoC; pricing calibrado com piloto (slide diz "sob consulta no Sales Cut") |
| 12 | **Roadmap** | Sales Cut (hoje, motor + PDF) → V1 (6m, PDF assinado + dashboard + LLM advogado de defesa sob ZDR) → Escala (12-24m, multi-stack, SOC 2) |
| 13 | **Team** | Founder + perfil da primeira contratação |
| 14 | **Traction / Pipeline** | Pilotos em conversa, lista de prospects (se houver) |
| 15 | **Ask** | Valor para investidor; primeira auditoria para piloto |

---

## 7. Esforço e timeline

### Versão Mínima (Tier 1) — 4-5 semanas solo

| Semana | Trabalho | Status |
|--------|----------|--------|
| 1-2 | **Sprint 0:** motor lendo solution, layer tagging, LNTY-001/002/003/006/007/008/009 + Saint, Sinner, Ninja #1 passando | **DONE 2026-04-27** |
| 3 | **Sprint 1 (NOVO):** PDF reporter via QuestPDF — projeto `Lintty.Engine.Reporter`, layout do laudo, fontes embedded, hash sha256 no rodapé | **PENDING** |
| 4 | Pitch deck v1 + repos públicos `lintty-demo/the-*` + screencast de backup | PENDING |
| 5 | Iterações no deck + landing publicada (Cloudflare Pages) + form Tally/Formspree | PENDING |

**Output:** demo + deck + PDF gerado pelo motor + 3 fixtures públicos. **Pronto para falar com investidor ou piloto técnico.**

### Versão Estendida (Tier 1 + 2) — 6-7 semanas

Acrescenta:
- Semana 6: Mockup completo do dashboard em Figma
- Semana 7: Landing polida com OG image, copy iteração 2

### Em paralelo (não na linha crítica)

- DPA template com advogado (~1 semana, ~R$5-15k) — material para a primeira reunião está em `docs/compliance/lawyer-briefing.md`.
- Identidade legal LGPD para a Política de Privacidade — em **Modo Projeto** (Operador pessoa natural), com Política preparada para republicação ao constituir PJ.

**Saiu da linha crítica:**
- Anthropic Enterprise + ZDR (V1+).
- Constituição da PJ — gatilhada pelo primeiro contrato com cliente, não antes (princípio de proporcionalidade).

---

## 8. Critérios para sair desta fase e ir para Production MVP

Não passe para os Sprints 2-6 do blueprint sem **pelo menos um** destes sinais:

### Sinais de "go"

✅ **Pelo menos 1 piloto pagante assinou** (mesmo simbólico — $5k é sinal real).

✅ **Investidor sério (term sheet) aceitou tese** baseado nos artefatos do Tier 1.

✅ **3+ prospects qualificados em pipeline avançado** (call de descoberta feita, dor confirmada, interesse explícito em piloto).

### Sinais de "not yet, iterate"

⚠️ Conversas iniciais positivas mas ninguém compromete.

⚠️ "Adoraria ter, me liga em 6 meses".

⚠️ Comprador entende o problema mas não vê dor suficiente para gastar.

→ **Solução:** itera o pitch, ajusta o customer profile, possivelmente ajusta a tese. Não constrói mais produto.

### Sinais de "no, pivot"

❌ Múltiplas conversas (10+) sem ninguém ficar entusiasmado com o problema.

❌ Compradores acham a solução cara demais para o valor percebido.

❌ "Já temos isso resolvido com X" (concorrente, ferramenta interna, processo).

→ **Solução:** repensa a tese. Talvez não seja Lintty como concebido. **Não constrói MVP no escuro.**

---

## 9. Anti-patterns nesta fase

Coisas que dão vontade de fazer mas vão custar tempo sem retornar valor:

- **❌ Reativar a camada LLM antes do primeiro sinal de "go".** O pivô Zero-IA é deliberado. Adiciona-se IA quando houver piloto pagante pedindo, não antes.
- **❌ Implementar GCP infra completa antes do Sprint 1 (PDF reporter) estar passando.** Ordem invertida.
- **❌ Implementar PAdES + TSA agora.** Projetos sérios; PDF Sales Cut com hash sha256 no rodapé dá pro recado.
- **❌ Construir dashboard web funcional para a demo.** Figma estático + terminal CLI vendem igualmente bem em 99% dos casos.
- **❌ Implementar Stripe integrado para piloto pagante.** Cobrança por TED + nota fiscal manual no piloto.
- **❌ Escrever Política de Privacidade exaustiva antes do primeiro cliente.** Versão básica para landing basta. DPA real com advogado quando o piloto pedir.
- **❌ Tentar vender para múltiplas verticais ao mesmo tempo.** Foque em 1 — geralmente o que sua rede já alcança.
- **❌ Promessa de "powered by AI" no deck.** AI saiu do produto V0 — promete Zero-IA, entrega Zero-IA. Quando voltar, é roadmap.

---

## 10. Riscos desta fase (e mitigação)

| Risco | Mitigação |
|-------|-----------|
| Demo cair na frente do comprador | Ensaie. Tenha screencast gravado de backup. Se vai falhar, falha melhor antes |
| Comprador querer ver o código real ali | Tenha repos `lintty-demo/the-*` já em GitHub público acessíveis |
| Comprador perguntar coisa técnica fora do roteiro | É bom sinal. Tenha o blueprint v1.0 (os 11 outros docs) para responder por email após a reunião |
| Comprador perguntar "cadê a IA?" | Resposta direta: "Saiu deliberadamente do V0. Hoje rodamos 100% determinístico — argumento de venda, não falta. IA volta em V1 como complemento opcional, sob ZDR contratual." |
| PDF gerado pelo motor sair diferente entre runs | Determinismo é exigência: fontes embedded, sem timestamps no conteúdo (só no rodapé), seed fixo na geração. Spec em ADR 0003. |
| Ninguém quer comprar | Sinal mais valioso possível. Pivote ou ajuste antes de queimar 6 meses construindo |
| Piloto quer rodar com código próprio antes de assinar | Aceita — sem LLM no pipeline, código fonte é processado **localmente no laptop** e descartado. Sem exposição a third-party (Anthropic). DPA simples cobre |

---

## 11. Resumo executivo

| | Tier 1 (Essencial) | Tier 2 (Útil) | Production MVP (depois) |
|-|---|---|---|
| **Duração** | 4-5 semanas | +2 semanas | +5-6 meses |
| **Custo operacional** | ~$0 fora salário (Zero-IA, sem cloud) | + designer freelancer | Engenheiro pleno + infra GCP + Anthropic Enterprise |
| **O que tem** | Motor + 7 regras Roslyn + PDF QuestPDF + 3 fixtures + deck | + dashboard mockup + landing polida | Tudo do Blueprint v1.0 (incluindo LLM, ZDR, hash-chain, PAdES) |
| **Prova qual coisa** | "A tecnologia é viável e determinística" | "A UX final é sólida" | "O produto está pronto para comprar" |
| **Vende para** | Investidor early stage, piloto técnico | Piloto sofisticado | Cliente self-service |

**A regra de ouro desta fase:** se uma feature do Blueprint não muda a probabilidade de fechar o piloto ou o investimento, não faça agora.

**Princípio do pivô (2026-04-27):** o argumento "100% determinístico, zero alucinação, zero custo de inferência" é mais forte para CTO de banco do que "temos IA". A IA volta como roadmap quando houver alguém pagando para acelerar — não antes.
