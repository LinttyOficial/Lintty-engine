# Lintty — Talk Track de Demo (12 minutos)

> Versão: Tier 1 Sales Cut, semana 3-4 (2026-04-27 — pivô Zero-IA).
> Duas versões na mesma página; mesmos timeboxes, ênfases distintas.
> Acompanha `pitch-deck.md` (15 slides) e o **PDF do Sinner gerado pelo motor** (Sprint 1, QuestPDF).
> Princípio: voz curta, dados quando possível, "arquitetura como evidência" — não opinião.

> **Pivô em 2026-04-27:** demo encurtou de 15 → 12 min com a saída do bloco LLM. O argumento de venda mudou de "AI advogado de defesa" para "**determinismo bit-a-bit, zero alucinação, zero custo de inferência**". A IA volta como roadmap V1 quando aparecer sinal de "go".

---

## Setup antes da reunião (não conta no relógio)

- Repos `the-saint`, `the-sinner`, `the-ninja-01` clonados localmente em pastas limpas.
- `lintty-engine` compilado e no `PATH`. Flag `--pdf` operacional (Sprint 1).
- Terminal em fonte grande (>= 18pt). Tela cheia, sem notificações.
- Screencast de backup gravado, salvo localmente, pronto para fallback.
- **3 PDFs pré-gerados** (Saint, Sinner, Ninja) abertos em janelas separadas — fallback se a geração ao vivo travar.

---

## Versão A — Investidor

> Ênfase: tamanho do mercado, defensibilidade técnica, roadmap, ask financeiro.
> Demo é prova de viabilidade técnica, não imersão de produto.

### 0:00–1:00 — Hook e problema

> "Empresas enterprise gastam centenas de milhares por ano terceirizando desenvolvimento .NET. Não conseguem validar arquiteturalmente o que recebem. Code review manual cobre 5%. Falhas estruturais só aparecem em produção, meses depois — e aí o contrato já foi pago. Esse é o gap."

(Slide 2, Slide 3)

### 1:00–2:30 — Positioning

> "Lintty é um árbitro técnico automatizado para entregas .NET. Recebe o código, roda análise arquitetural type-aware com Roslyn, e emite um laudo PDF como evidência de aceite. **Tudo determinístico: mesmo input, mesmo PDF, bit a bit.** Zero alucinação, zero custo de inferência. Não somos escrow, não somos advogado, não há mediação humana. Somos o oráculo técnico — verdade arquitetural, não opinião."

(Slide 4, Slide 5)

### 2:30–7:30 — Demo ao vivo (curta, prova de viabilidade)

```
$ lintty-engine analyze --solution the-saint/Saint.sln --pdf laudo-saint.pdf
→ 0 violações, score 100, grade A. PDF gerado.
```

> "Código bem-feito. Lintty não inventa problemas onde não tem. PDF: capa A, exceções vazia."

```
$ lintty-engine analyze --solution the-sinner/Sinner.sln --pdf laudo-sinner.pdf
→ 10 violações, 3 hard locks Críticas, grade F. PDF gerado.
```

> "Aqui o código tem problemas estruturais reais. SQL no domínio, ciclo de dependência entre camadas. Hard lock significa selo não emitido — não tem como suprimir. Repare no PDF: cada violação tem arquivo, linha, snippet do código violador."

```
$ lintty-engine analyze --solution the-ninja-01/Ninja01.sln --pdf laudo-ninja.pdf
→ 3 violações LNTY-002 detectadas via constant folding.
```

> "Esse é o ponto técnico que diferencia. O código tenta esconder SQL via concatenação de constantes — `'SE' + 'LECT'`. Linter de regex passa como limpo. Lintty pega porque usa o compilador Roslyn de verdade, com análise type-aware. Sem isso, o produto seria fraude."

(Slide 6)

### 7:30–8:30 — O PDF gerado

> "Esse é o artefato vendável. Não é tela de dashboard, não é integração. É um PDF que vai junto do contrato como prova técnica de aceite. Hoje rodamos sem assinatura digital — em V1 acrescentamos PAdES-B-LT mais TSA RFC 3161, hash registrado em audit chain. Peso probatório real. Hoje o conteúdo já é o conteúdo final; falta só o selo cripto."

(Slide 7 + PDF do Sinner aberto)

### 8:30–10:00 — Modelo de negócio + roadmap

> "Quem paga é o contratante. Cobrança por scan de Milestone, tier por LoC. Pricing sob consulta no Sales Cut — calibramos com primeiro piloto, não inventamos número antes de ouvir a dor. Subscription mensal de feedback contínuo entra em V1 como gancho de retenção."

> "Roadmap: hoje, motor mais PDF determinístico — 100% tipado. V1 em 6 meses pós-sinal: PDF assinado, audit hash-chain, dashboard self-service, GCP serverless, e a camada LLM como complemento opcional para regras semânticas como 'lógica de negócio dentro do repositório' — sob ZDR Anthropic. **A IA fora do V0 é escolha técnica, não falta.** Defensibilidade ganha argumento: zero alucinação por design."

(Slide 9, Slide 10, Slide 11, Slide 12)

### 10:00–12:00 — Ask

> "Estamos buscando `[VALOR — CALIBRAR COM PRODUCT-OWNER]` para `[N MESES]`. Uso: completar o motor (assinatura PAdES, audit chain), GCP serverless, primeira contratação, go-to-market. Marco de saída do round: `[X PILOTOS PAGANTES]` ou `[$Y ARR]`."

(Slide 15, ask para investidor)

**Pergunta única de fechamento:**

> "Faz sentido para a tese do fundo?"

---

## Versão B — Piloto

> Ênfase: dor real do comprador, demo extensa, conversa sobre primeira entrega.
> Demo é imersão; o investidor-pitch vira plano de fundo.

### 0:00–1:00 — Hook e problema

> "Vocês já passaram pela situação de receber uma entrega de uma agência terceirizada e descobrir só meses depois que o sistema não escala? Que tem regra de negócio em Stored Procedure? Que o time interno não consegue dar manutenção? É exatamente esse problema que a gente resolve. Hoje quero entender se isso ressoa com a dor de vocês — e se ressoar, mostrar como funciona na prática."

(Slide 2, Slide 3)

### 1:00–2:00 — Positioning

> "Lintty é um árbitro técnico automatizado para entregas .NET. O código entra, em minutos sai um laudo PDF dizendo se a arquitetura está sólida ou não. Vocês usam esse laudo como evidência formal de aceite — sem precisar de revisor sênior interno gastando dias em cada milestone, sem ficar refém da boa vontade da agência. **Tudo type-aware, tudo determinístico — mesmo input, mesmo PDF, bit a bit.** Não somos parte do contrato de vocês, somos a prova técnica que o contrato pede."

(Slide 4, Slide 5)

### 2:00–8:00 — Demo ao vivo (extensa, com pausa para perguntas)

```
$ lintty-engine analyze --solution the-saint/Saint.sln --pdf laudo-saint.pdf
→ 0 violações, score 100, grade A. PDF gerado.
```

> "Esse é o cenário em que tudo está bem. Lintty não cria ruído quando não tem problema. Esse é importante porque a confiança do laudo depende de não ter falso positivo."

```
$ lintty-engine analyze --solution the-sinner/Sinner.sln --pdf laudo-sinner.pdf
→ 10 violações, 3 hard locks Críticas, grade F. PDF gerado.
```

> "Aqui é o caso real de entrega problemática. Olha a LNTY-002: SQL inline na camada de domínio. Olha a LNTY-007: ciclo de dependência entre Application e Infrastructure. Esses dois são hard locks — não tem como a agência mandar comentário pedindo para ignorar. O selo não é emitido enquanto não corrigir."

> *(pausa para pergunta: "Vocês já viram esse padrão em entregas que receberam?")*

```
$ lintty-engine analyze --solution the-ninja-01/Ninja01.sln --pdf laudo-ninja.pdf
→ 3 violações LNTY-002 detectadas via constant folding.
```

> "Esse caso é o que diferencia Lintty de ferramenta de regex. O código tenta esconder SQL concatenando strings — `'SE' + 'LECT'`. Um SonarQube ou um grep passam direto. Lintty pega porque o motor é Roslyn type-aware: vê o que o compilador vê, não o que o texto parece dizer. Isso importa porque agência ruim aprende rápido a passar pelo linter superficial."

(Slide 6)

### 8:00–9:30 — O PDF aberto

> "Esse é o documento que vocês recebem ao final de cada Milestone de entrega. Capa com score, lista de violações com arquivo e linha, sumário de exceções com email do dev que pediu supressão e justificativa dele — tudo na primeira página, não escondido. Rodapé com hash sha256 do próprio PDF como integridade local. Em V1 entra assinatura digital com PAdES e timestamp de TSA — peso jurídico nacional. Esse PDF vai junto do termo de aceite contratual com a agência."

> *(abre PDF do Sinner e percorre página por página: capa → sumário → violações → exceções → grafo → rodapé)*

(Slide 7 + PDF do Sinner aberto)

### 9:30–10:30 — Modelo de negócio + por que sem IA agora

> "Cobrança é por scan de Milestone. A solution de vocês tem `[ESTIMAR LOC NA CONVERSA]` linhas, então cai no tier de `[X×]`. Pricing fica calibrado com vocês — não vou inventar número antes de ouvir a dor. Quem paga é vocês; em V1, a agência ganha subscription opcional para feedback contínuo em PR durante o desenvolvimento."

> "Sobre a parte de 'por que sem IA': hoje rodamos 100% determinístico. Mesmo código, mesmo PDF, sempre. Isso é deliberado — para CTO de banco, 'zero alucinação' vende mais do que 'temos IA'. Em V1 acrescentamos uma camada LLM como **complemento opcional** para detectar coisas semânticas como 'lógica de negócio dentro de repository', sob ZDR contratual com Anthropic — código de vocês não persiste em LLM, garantia contratual. Mas isso é roadmap, não V0."

(Slide 9, Slide 10, Slide 11, Slide 12)

### 10:30–12:00 — Ask

> "Proposta concreta: vocês escolhem uma entrega real que a agência de vocês está fazendo agora. A gente roda o primeiro Milestone Audit por `[VALOR — CALIBRAR COM PRODUCT-OWNER]`. Se o laudo agregar valor — se vocês olharem e disserem 'isso me dá tração com a agência' — a gente continua para o próximo Milestone. Se não agregar, devolvemos integralmente."

(Slide 15, ask para piloto)

**Pergunta única de fechamento:**

> "Qual entrega de vocês a gente roda primeiro?"

---

## Notas de execução

- Versão Investidor é mais discursiva nos blocos 7:30-12:00 (defensibilidade + ask). Demo é mais curta (intencional).
- Versão Piloto inverte: demo mais longa, com pausa para validação de dor; ask é específico da operação do prospect.
- Em ambas, NÃO citar concorrente direto (SonarQube, CodeClimate) sem ser provocado. Se vier pergunta, responder com nuance — Lintty é arquitetural type-aware, não estilístico/regex.
- Em ambas, NÃO prometer custom rules, mediação humana, escrow, free trial.
- **Em ambas, NÃO prometer "powered by AI" no V0.** A IA é roadmap V1+ — argumentar zero alucinação é a venda.
- Pergunta única de fechamento é literal: uma pergunta. Silêncio depois é normal e desejável.

## Resposta a "cadê a IA?"

Pergunta vai aparecer. Resposta direta:

> "Saiu deliberadamente do V0. Hoje rodamos 100% determinístico — Roslyn type-aware, score canon, PDF reproduzível bit-a-bit. Para validar a tese, isso é mais forte: zero alucinação, zero custo de inferência, auditável por construção. Em V1, voltamos a ligar uma camada LLM como **complemento** para regras semânticas (ex: 'lógica de negócio em repository'), sob ZDR contratual com Anthropic. Mas a integridade do laudo não depende da IA — ela acrescenta, não sustenta."

Se o prospect insiste em querer IA hoje:

> "Justo. A gente prefere fechar contigo no V0 sem IA e ativar a camada LLM no primeiro Milestone do Production MVP, junto com PDF assinado e audit chain. Faz sentido?"
