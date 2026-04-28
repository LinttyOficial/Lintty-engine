# Lintty — Pitch Deck (15 slides)

> Versão: Tier 1 Sales Cut, semana 3-4 (2026-04-27 — pivô Zero-IA).
> Formato: 1 H2 por slide. ~30 segundos por slide narrado.
> Público: investidor early stage e/ou primeiro piloto técnico.
> Placeholders marcados em `[CAIXA ALTA]` precisam de decisão do product-owner ou de imagem real.
>
> **Pivô em 2026-04-27:** o V0 do produto roda **100% determinístico** (motor Roslyn type-aware + PDF gerado pelo motor via QuestPDF). A camada LLM ("advogado de defesa" para LNTY-004) move-se para o **roadmap V1+**, sob ZDR contratualizado. O argumento de venda fica mais forte: zero alucinação, zero custo de inferência, mesmo input → mesmo PDF, bit a bit.

---

## Slide 1 — Lintty

- Arquitetura como evidência.
- Auditoria automatizada para entregas de software .NET.
- Founder: [INSERIR NOME DO FOUNDER]
- Contato: contato@lintty.com
- Data: abril 2026

> Speaker note: "Lintty é um árbitro técnico para entregas de software. Hoje em 15 minutos eu mostro o que ele faz, em quem vendemos, e por que agora."

> Visual: [INSERIR LOGO LINTTY GRANDE NO CENTRO + TAGLINE EM SUBTITLE. FUNDO LIMPO, BRANCO OU CINZA-ESCURO. SEM STOCK PHOTO.]

---

## Slide 2 — O Problema

- Empresas enterprise terceirizam desenvolvimento .NET para agências externas.
- Não conseguem auditar a qualidade arquitetural do que recebem.
- Falhas estruturais só aparecem em produção, sob carga, meses depois.
- Contratos são pagos sem validação técnica formal.

> Speaker note: "O comprador enterprise paga centenas de milhares de reais em entregas e não tem como saber se o que recebeu vai escalar. Descobre quando já é tarde."

> Visual: [INSERIR ÍCONE/ILUSTRAÇÃO DE 'CONTRATO ASSINADO' AO LADO DE 'SISTEMA QUEBRANDO'. SEM STOCK PHOTO GENÉRICA — PREFERIR DIAGRAMA SIMPLES OU PRINT REAL DE INCIDENTE.]

---

## Slide 3 — Como o Mercado Resolve Hoje

- Code review manual: cobre ~5% do código entregue.
- Cada revisão consome dias de engenheiros sêniores.
- Revisor lê arquivo a arquivo — perde a visão arquitetural do conjunto.
- Linters tradicionais (regex) detectam estilo, não estrutura.

> Speaker note: "A alternativa atual é code review humano, que é caro, lento e parcial. Linters de mercado ajudam em estilo, mas não enxergam camadas, dependências, ou regra de negócio vazando para infra."

> Visual: [INSERIR TABELA COMPARATIVA SIMPLES: COVERAGE % | TEMPO | CUSTO. TRÊS COLUNAS: CODE REVIEW MANUAL | LINTER TRADICIONAL | (COLUNA VAZIA — SE PREENCHE NO SLIDE 4).]

---

## Slide 4 — Lintty: a Solução

- Motor automatizado de análise arquitetural type-aware (compilador Roslyn, não regex).
- Laudo PDF determinístico como evidência de aceite (assinatura digital em V1+).
- Zero mediação humana; canon de regras explícito e auditável.
- Resultado em minutos, não dias. Mesmo input → mesmo PDF, bit a bit.

> Speaker note: "Lintty automatiza essa auditoria. Recebe o código, roda análise arquitetural profunda com o compilador Roslyn, e emite um laudo PDF que vale como evidência de aceite — não opinião, evidência. 100% determinístico, zero alucinação."

> Visual: [INSERIR MESMO LAYOUT DO SLIDE 3, AGORA COM A 3ª COLUNA PREENCHIDA — 'LINTTY: 100% COBERTURA / MINUTOS / R$ POR SCAN POR LOC'. DESTAQUE VISUAL NA COLUNA NOVA.]

---

## Slide 5 — How It Works (Pipeline)

- **Code:** solution .NET entregue pela agência.
- **Roslyn:** análise estática type-aware (compilador, não regex). 7 regras determinísticas no Sales Cut.
- **JSON:** output factual com violações, score A-F, layer summary, exceções.
- **PDF:** laudo gerado pelo próprio motor via QuestPDF — determinístico bit-a-bit.

> Speaker note: "Quatro etapas. Código entra por um lado, laudo PDF sai pelo outro. Tudo type-aware, tudo determinístico. Mesma entrada, mesmo PDF — sempre. Em V1 voltamos a ligar a camada LLM como complemento opcional para regras semânticas, mas o V0 está deliberadamente sem IA: zero alucinação é um argumento de venda, não uma falta."

> Visual: [INSERIR DIAGRAMA DO PIPELINE: **QUATRO** CAIXAS HORIZONTAIS — CODE → ROSLYN → JSON → PDF. SETAS LIMPAS ENTRE ELAS. CORES NEUTRAS. SEM CAIXA "IA" — É O DIFERENCIAL DO PIVÔ.]

---

## Slide 6 — Live Demo Frame

- Próximos 5 minutos: motor rodando ao vivo.
- Fixture 1: the-saint (código bem feito).
- Fixture 2: the-sinner (código com 9 violações reais).
- Fixture 3: the-ninja-01 (SQL escondido via constant folding).

> Speaker note: "Vou parar de falar e mostrar. Três repos públicos, motor rodando no laptop, output em tempo real."

> Visual: [INSERIR SLIDE EM TELA CHEIA COM PROMPT DE TERMINAL ESTILIZADO E A FRASE 'LIVE DEMO'. SEM TEXTO DE BULLET — É UMA TRANSIÇÃO VISUAL PARA O TERMINAL REAL.]

---

## Slide 7 — O Laudo

- Capa com score visível (A-F) e selo emitido ou não.
- Sumário executivo, lista de violações, evidências por linha.
- Sumário de exceções na primeira página, com email do autor git.
- Rodapé técnico com `canon_version`, `run_id`, `hash_pdf` (sha256 do binário) — reprodutibilidade verificável.

> Speaker note: "Esse é o artefato vendável. Não é tela de dashboard. É um PDF que vai junto do contrato como prova técnica de aceite. Hoje o motor gera; em V1 ele é assinado digitalmente com PAdES e timestamp de TSA RFC 3161 — peso jurídico real."

> Visual: [INSERIR FOTO DA CAPA DO PDF GERADO PELO MOTOR (RODAR Sprint 1 SOBRE O SINNER E EXPORTAR EM ALTA RESOLUÇÃO). DESTAQUE VISUAL NO 'F — SELO NÃO EMITIDO'. ESTE SLIDE É IMAGEM, NÃO TEXTO.]

---

## Slide 8 — Why Now

- Hexagonal/DDD/Clean Architecture amadureceram como padrão de mercado .NET.
- Roslyn como compilador-API maduro habilita análise type-aware em segundos no laptop — não precisa de cluster.
- Outsourcing de software .NET cresce em LATAM (BR, MX, AR) com clientes US/EU.
- Pressão regulatória (LGPD, BCB, SUSEP) eleva exigência sobre fornecedores.

> Speaker note: "Há cinco anos isso não daria pé. Roslyn não tinha esse grau de exposição de Symbol API, hexagonal era nicho. Hoje os fatores convergem — e o que entregamos hoje é determinístico, sem depender de IA. IA volta como complemento em V1."

> Visual: [INSERIR 4 ÍCONES OU CARDS HORIZONTAIS — UM POR FATOR. SEM STOCK PHOTO. PODE SER PEQUENO GRÁFICO DE TENDÊNCIA POR FATOR.]

---

## Slide 9 — Defensibilidade

- **Canon opinionado:** agências aprendem a entregar "no padrão Lintty". Network effect via padrão.
- **Determinismo bit-a-bit:** mesmo input → mesmo PDF, sempre. Zero alucinação, zero custo de inferência. Argumento de venda direto para CTO de banco que rejeita "magia de IA".
- **Type-aware Roslyn:** detecta SQL via constant folding (Ninja #1) — linter regex não alcança. Diferencial técnico verificável em demo.
- **Análise efêmera:** código processado localmente no Sales Cut; em V1 sandbox Cloud Run derruba após scan.
- **Roadmap V1+:** PDF assinado (PAdES-B-LT + TSA RFC 3161), audit hash-chain imutável, LLM advogado de defesa sob ZDR — todos como complemento, não como dependência.

> Speaker note: "Cinco pilares. O canon cria network effect. O determinismo é nosso pilar mais forte hoje: 'mesma entrada, mesmo PDF, bit a bit' — auditável, reproduzível, sem IA. Em V1 acrescentamos PDF assinado e LLM opcional, mas o produto V0 é defensável sozinho."

> Visual: [INSERIR 5 ÍCONES VERTICAIS OU EM GRADE 2x3 (1 VAZIA). CADA UM COM TÍTULO CURTO. FUNDO ESCURO PARA DAR PESO. ÍCONE DE 'EQUALS' PARA DETERMINISMO; ÍCONE DE 'CHAIN' PARA AUDIT-CHAIN (ROADMAP).]

---

## Slide 10 — Modelo de Negócio

- Quem paga: o contratante (empresa que terceiriza).
- Pay-per-Scan: 1 scan = 1 auditoria oficial de Milestone com PDF. Cobrança por LoC (tier no slide 11).
- Continuous Feedback: assinatura mensal por repositório, scans ilimitados em PR (V1+).
- Sem split entre contratante/agência. Sem escrow. Sem mediação humana.

> Speaker note: "Operação enxuta. Um pagador, um produto. Cobrança direta por scan, com tier por tamanho de código — sem pacotes pré-pagos, sem créditos, sem complexidade. A agência ganha subscription opcional de feedback contínuo em V1; quem assina o contrato é o contratante."

> Visual: [INSERIR DIAGRAMA SIMPLES DE FLUXO: CONTRATANTE → $ → LINTTY → LAUDO → CONTRATANTE. AGÊNCIA RECEBE FEEDBACK NO PR EM RAMO LATERAL.]

---

## Slide 11 — Pricing

- **Modelo:** R$ por scan de Milestone, tier por tamanho de código (LoC da solution).
- **Sales Cut:** pricing **sob consulta** — calibramos com o primeiro piloto, não escondemos depois.
- **Tiers de LoC** (estrutura, valor calibrado com piloto): ≤50k = 1×, 50–200k = 2×, 200–500k = 4×, >500k cotação.
- **Continuous Feedback Subscription** (opcional, V1+): assinatura mensal por repositório, scans ilimitados em PR.
- Cobrança via TED + NF-e manual no piloto. Stripe entra em V1.

> Speaker note: "Sem créditos pré-pagos no V0. Cobramos por scan, com tier transparente por tamanho. O valor exato calibra com vocês — não vou inventar número aqui sem ter ouvido a dor de vocês primeiro. Subscription mensal de feedback contínuo é roadmap V1, opcional."

> Visual: [INSERIR TABELA DE TIERS DE LOC COMO IMAGEM LIMPA. SEM TABELA DE PACOTES (REMOVIDA NO PIVÔ ZERO-IA). SEM 'A PARTIR DE'. NA BASE DO SLIDE: 'Pricing calibrado com piloto. Sem créditos pré-pagos.']

---

## Slide 12 — Roadmap

- **Sales Cut (hoje):** motor Roslyn type-aware (7 regras) + PDF gerado pelo motor (QuestPDF) + 3 fixtures públicos. **100% determinístico.**
- **V1 (mês 0–6 após sinal de "go"):** PDF assinado digitalmente (PAdES-B-LT + TSA RFC 3161) + audit hash-chain imutável + dashboard self-service + GCP serverless + **LLM advogado de defesa (LNTY-004) sob ZDR Anthropic**.
- **V2 (mês 6–12):** Stripe + SOC 2 Type I + multi-stack inicial (Java/Spring).
- **Escala (mês 12–24):** tier enterprise + data residency BR/EU + custom rules + LNTY-005 e expansão semântica.

> Speaker note: "Sales Cut hoje é determinístico end-to-end. V1 acrescenta o que dá peso jurídico ao PDF e a IA como complemento opcional, sob ZDR contratual. Cada fase desbloqueada por sinal de mercado, não por calendário. A IA aparece em V1 — deliberadamente fora do V0 para que o produto se sustente sem ela."

> Visual: [INSERIR TIMELINE HORIZONTAL DE 4 BLOCOS COM FASE NA BASE. SALES CUT EM DESTAQUE COMO 'HOJE'. MARCO POR FASE COMO ÍCONE. IA NO BLOCO V1, NÃO ANTES.]

---

## Slide 13 — Time

- Founder: `[INSERIR NOME + 1 LINHA DE BACKGROUND TÉCNICO]`
- Próxima contratação: engenheiro pleno .NET com afinidade por DDD/hexagonal.
- Advisors: `[LISTAR SE HOUVER, OU REMOVER ESTE BULLET]`
- Operação solo até primeiro piloto pagante; contrata após sinal de mercado.

> Speaker note: "Hoje sou eu. A primeira contratação acontece após o primeiro piloto pagante, não antes — operação enxuta é parte da tese."

> Visual: [INSERIR FOTO DO FOUNDER À ESQUERDA + BIO CURTA À DIREITA. SE HOUVER ADVISORS, LISTAR EM LINHA INFERIOR.]

---

## Slide 14 — Tração e Pipeline

- Pilotos em conversa: `[N PROSPECTS QUALIFICADOS]`.
- **Vertical-foco no MVP: varejo brasileiro de grande porte.** Adjacências reativas (financeiro, seguros) atendidas se chegarem.
- 3 fixtures públicos no GitHub (the-saint, the-sinner, the-ninja-01) acessíveis para clone ao vivo durante a demo.
- Demo CLI funcional + PDF gerado pelo motor (QuestPDF, determinístico) + landing page no ar.

> Speaker note: "Estamos em conversa avançada com [N] prospects no varejo brasileiro. Os artefatos do Sales Cut estão prontos para mais reuniões. O sinal de go-to é primeiro piloto pagante ou term sheet — qualquer um destes vira o gatilho para Production MVP."

> Visual: [INSERIR LISTA DISCRETA DE TIPOS DE PROSPECT (NÃO LOGOS NOMINAIS — ATÉ FECHAR CONTRATO, NUNCA CITAR PROSPECT POR NOME). EX: 'VAREJISTA NACIONAL TIER 1', 'MARKETPLACE BRASILEIRO', 'REDE DE E-COMMERCE'.]

---

## Slide 15 — Ask

### Ask para investidor

- Captação: `[VALOR EM USD/BRL — CALIBRAR COM PRODUCT-OWNER]` em `[N MESES — TIPICAMENTE 12-18]`.
- Uso: motor + IA + infra GCP + 1ª contratação + go-to-market para fechar 50 contratos.
- Marco de saída do round: `[X PILOTOS PAGANTES OU $Y EM ARR]`.

### Ask para piloto

- Topa rodar uma entrega real conosco?
- Cobramos `[VALOR DO PRIMEIRO MILESTONE — CALIBRAR COM PRODUCT-OWNER]` pela primeira auditoria oficial.
- Se não agregar valor mensurável, devolvemos integralmente.

> Speaker note (investidor): "Estamos buscando [valor] para [N] meses. O alvo é [X] pilotos pagantes até [marco]."

> Speaker note (piloto): "Topa fazer uma entrega real conosco? Primeiro milestone por [valor]. Se não agregar, devolvemos."

> Visual: [INSERIR SLIDE LIMPO COM AS DUAS VERSÕES LADO A LADO OU EM ABAS. CTA CLARO NA BASE: EMAIL + LINKEDIN DO FOUNDER.]
