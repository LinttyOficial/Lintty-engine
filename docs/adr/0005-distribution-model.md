# ADR 0005 — Distribution Model: Self-Service CLI as Default

- **Status:** Proposed
- **Date:** 2026-04-28
- **Author:** software-architect
- **Audience:** backend-dev-dotnet (release pipeline), security-compliance (postura LGPD), tech-writer-sales (pitch deck + talk track + lawyer briefing), product-owner (modelo comercial)
- **Sprint:** Sales Cut Tier 1 (`docs/12-sales-cut.md`), em paralelo aos artefatos de venda — não exige novo Sprint de engenharia.
- **Pré-requisitos atendidos:** ADR 0001 (motor é CLI standalone, sem dependência de servidor — `dotnet publish -r <rid> --self-contained` já produz binário único) + ADR 0003 (PDF determinístico bit-a-bit — torna verificação independente possível).
- **Substitui:** o pressuposto implícito anterior de que o operador receberia o código do cliente para rodar o scan (modelo "concierge"). Não invalida ADRs 0001–0004; estende-os no eixo de **distribuição**.

## 1. Contexto

Até 2026-04-28, o fluxo de uso assumido pelos artefatos de venda era:

> Contratante (ex: rede de varejo) compartilha o código do Milestone com o Lintty (operador), que clona em laptop próprio, roda `lintty-engine analyze`, descarta o clone, e devolve o PDF assinado.

Esse modelo — daqui em diante chamado de **Modo B (Concierge)** — foi assumido implicitamente em `docs/sales/customer-flow-example.md` (rascunho), `docs/compliance/dpa-template.md` e `docs/compliance/lawyer-briefing.md` §5.

Em 2026-04-28, conversa com o founder levantou o ponto de que **o cliente compartilhar código fonte com o operador é a parte mais frágil do fluxo** — tanto perante a equipe de segurança do contratante quanto sob LGPD. A proposta levantada: **o cliente roda o CLI no equipamento dele**. O motor já é CLI standalone (decisão do ADR 0001), o PDF já é bit-a-bit determinístico (decisão do ADR 0003), portanto a parte técnica está resolvida — falta apenas distribuição confiável e adequar a narrativa de venda + jurídica.

Esta ADR registra a decisão de inverter o default e formaliza os três modos que vão coexistir.

## 2. Decisão

### 2.1 Default novo

A partir de 2026-04-28, o **modo de uso default do Lintty** é:

> **Modo A — Self-Service CLI.** O cliente baixa o binário oficial do GitHub Releases, valida `sha256` + assinatura, instala em equipamento próprio (laptop, servidor, CI runner) e roda contra a solution dele. **O código fonte do cliente nunca sai do equipamento dele.** O Lintty, como organização, nunca recebe nem processa o código.

### 2.2 Os três modos que coexistem

| Modo | Quando se aplica | Postura jurídica do Lintty |
|------|------------------|----------------------------|
| **A — Self-Service CLI** (default) | Cliente baixa binário, roda local, recebe PDF. Nenhum dado do cliente toca o Lintty. | **Fornecedor de software licenciado** — não há tratamento de dados pelo Lintty. Vínculo é EULA + MSA comercial. |
| **B — Concierge** (fallback raro) | Cliente prefere que operador rode o scan (PoC inicial, cliente sem capacidade técnica de validar binário, scan único de prova). Operador clona via PAT de 24h, roda, descarta. | **Operador de tratamento (LGPD art. 5º VII)** — DPA obrigatório, retenção justificada, análise efêmera, descarte imediato. |
| **C — VS Code Extension** (V1+) | Agência (não contratante) usa durante o desenvolvimento para feedback contínuo no PR. Wrapper TypeScript do CLI, roda local. Não emite PDF oficial — apenas advisory. | **Fornecedor de software licenciado** (mesma postura do Modo A). |

**Modo A é a venda principal.** Modo B existe como cortesia operacional, mas é precificado igual ou levemente premium e não é apresentado como caminho default.

### 2.3 Argumento de venda atualizado

A defensibilidade do Lintty (slide 9 do pitch deck) passa a ter **três pernas reforçando a mesma narrativa de auditabilidade radical**:

1. **100% determinístico** — mesmo input → mesmo PDF, byte por byte. Já decidido no ADR 0003.
2. **Zero IA no V0** — sem alucinação, sem custo de inferência. Já decidido no pivô Zero-IA de 2026-04-27.
3. **100% local — código nunca sai do equipamento do cliente** (NOVO, decidido aqui).

As três pernas juntas formam o argumento "auditabilidade por construção", não por confiança no operador.

## 3. Alternativas consideradas

### 3.1 Manter Modo B como default

Mantém o operador no caminho crítico de cada scan. **Descartado por três razões:**
- **Trust ceiling baixo:** equipe de segurança do contratante (varejo brasileiro de grande porte, ICP atual) trata "compartilhar código fonte com terceiro" como decisão de comitê de risco. O lead time pra fechar piloto inflaciona.
- **Postura LGPD pesada:** Lintty como Operador exige DPA obrigatório, sub-operadores listados, notificação de incidente em 24h, retenção justificada. Custo jurídico inicial estimado em R$5–15k (briefing do advogado, `manual-actions.md` §🔴) é em parte função dessa postura.
- **Escalabilidade:** cada scan exige operador disponível. N clientes em paralelo viram fila.

### 3.2 SaaS com upload

Cliente envia código via web/API, Lintty processa em GCP, devolve PDF. **Descartado por dois motivos:**
- **Fere a perna 3 do argumento de venda** ("100% local"). Inconsistente com o pivô Zero-IA: se o pitch é "auditabilidade radical", não pode receber upload.
- **Reabre toda a postura de Operador LGPD** + adiciona infra GCP + abre modelo de ameaça (uploads concorrentes, isolation entre tenants, retenção). Era o V1+ original; deliberadamente fora do Sales Cut.

### 3.3 GitHub App / GitLab App instalado na org do cliente

Lintty App instalado na org do contratante; webhook dispara scan em runner gerenciado pelo Lintty. **Descartado para o Sales Cut, mantido como roadmap V1+:**
- Apesar de melhor UX que Modo B, ainda exige o Lintty receber o código (mesmo que em runner efêmero), reabrindo a postura de Operador LGPD.
- Lead time de implementação de GitHub App auth + runner orchestration é incompatível com a janela do Sales Cut (4–7 semanas).

### 3.4 Servidor de licença SaaS no V0

CLI faz call HTTPS antes de gerar o PDF; servidor decreta `scan_id`, registra hash do output. **Descartado para o Sales Cut, mantido como V1+:**
- Adiciona infra de servidor que o Sales Cut deliberadamente evita (Zero-Custo).
- No piloto inicial (varejo brasileiro, contratualmente comprometido), **honor system + relatório mensal de scans é suficiente** — o cliente não tem incentivo pra fraudar; o laudo serve a ele para apertar a agência.

## 4. Consequências

### 4.1 Positivas

- **Postura jurídica simplifica.** Lintty deixa de ser Operador LGPD no fluxo default. EULA + MSA comercial substituem o DPA como eixo contratual. Estimativa: **redução de 30–50% no custo+tempo da consulta jurídica inicial** (briefing do advogado vai virar "redigir EULA + MSA" em vez de "redigir DPA + revisar Política").
- **Pitch ganha terceira perna defensável.** "Você nunca me manda código. Verificável." é argumento mais forte que "confio que você descartou".
- **Operação destrava.** N clientes podem rodar em paralelo sem fila do operador. Founder ganha capacidade de focar em vendas em vez de scans manuais.
- **Argumento técnico já existente vira venda.** Determinismo bit-a-bit (Sprint 1) já estava entregue, mas servia só para "Lintty consegue provar que não alterou o PDF". Agora também serve para "agência consegue verificar o laudo do contratante baixando o mesmo binário". Mesmo investimento, dois usos.

### 4.2 Negativas / custos

- **Distribuição confiável vira pré-requisito.** Sem release pipeline (GitHub Actions: tag → build self-contained binaries para Win/Linux/macOS → publica no Releases com `sha256` + assinatura GPG), Modo A não vai ao ar. Estimativa: 4–8 horas de trabalho do `backend-dev-dotnet`.
- **Cobrança fica em honor system no V0.** Sem servidor de licença, contagem de scans depende de relatório mensal do cliente + hash dos PDFs como evidência. Funciona com primeiro piloto contratualmente comprometido; **não funciona em escala**. Token de scan emitido por email entra como Modo A.1 se algum prospect exigir controle. Servidor de licença em V1+.
- **Como a agência (Tessera) confia no laudo emitido pelo contratante (Atrium).** Em tese, a Atrium poderia rodar binário modificado e emitir PDF falso. **Defesa:** Tessera baixa o mesmo binário oficial, clona o mesmo commit, roda — o PDF tem que bater byte por byte (determinismo bit-a-bit). Esta defesa precisa ser explicitada no `customer-flow-example.md` para virar argumento de venda.
- **Modo B continua existindo, com toda a complexidade jurídica.** Não dá para deletar — alguns prospects vão preferir concierge. Significa que `dpa-template.md` continua relevante, mas vira anexo do Modo B, não default.

### 4.3 Reversibilidade

Esta decisão é **leve de reverter**. Modo A é puramente editorial + release pipeline; o motor não muda. Se em 6 meses os primeiros pilotos sinalizarem que preferem Modo B (improvável dado o ICP de varejo brasileiro grande, mas possível), basta:

- Repromover Modo B a default nos artefatos de venda.
- Ativar/recontratar DPA como artefato principal.
- Manter Modo A como cortesia self-service.

Sem custo de código, sem dívida técnica acumulada, sem migração de cliente.

## 5. Plano de implementação

### 5.1 Engenharia (`backend-dev-dotnet`, 4–8h)

- **Release pipeline.** `.github/workflows/release.yml` que dispara em tag `v*`:
  - `dotnet publish -r win-x64 --self-contained -p:PublishSingleFile=true -p:PublishTrimmed=true`
  - Idem para `linux-x64`, `osx-arm64`, `osx-x64`.
  - `sha256sum` de cada binário em `SHA256SUMS`.
  - Assinatura GPG do `SHA256SUMS` em `SHA256SUMS.asc` (chave do operador, no Sales Cut).
  - Upload tudo no GitHub Release.
- **README do repo público com instruções de instalação:** `wget` do binário, `sha256sum -c SHA256SUMS`, `gpg --verify SHA256SUMS.asc`, `chmod +x`, primeiro `lintty-engine analyze`.
- **Sem mudança no motor.** Zero código novo no `engine/`.

### 5.2 Editorial (`tech-writer-sales` + `security-compliance`, 1–2 dias somados)

Os arquivos abaixo viram consequência editorial do ADR. Ordem sugerida:

| Arquivo | Mudança principal |
|---|---|
| `docs/sales/customer-flow-example.md` (criar) | Reescrever a Parte B do exemplo Atrium↔Tessera começando pelo Modo A. Modo B vira fallback com 1 parágrafo. |
| `docs/sales/pitch-deck.md` Slide 9 | Adicionar pilar "**100% local — código nunca sai do laptop do cliente**" entre "100% determinístico" e "Análise type-aware". |
| `docs/sales/talk-track.md` | Reescrever respostas hipotéticas: "Como vocês recebem o código?" → "Vocês não nos mandam código. Você baixa o binário, valida o hash, roda na máquina de vocês." |
| `docs/compliance/lawyer-briefing.md` | §3.1 reescrita (Lintty é fornecedor de software, não Operador). §12 nova: "EULA + MSA substituem o DPA neste modelo?" — pergunta explícita ao advogado. |
| `docs/compliance/dpa-template.md` | Banner no topo: "aplicável apenas no Modo Concierge (B), raro. Modo default é Modo A — ver ADR 0005." |
| `docs/compliance/privacy-policy.md` | §4 (Compartilhamento) ganha frase: "No uso normal do produto (Modo A), código fonte do cliente não é compartilhado conosco — o software roda no equipamento do cliente." |
| `docs/manual-actions.md` | Nova tarefa 🟡: "Configurar GitHub Releases pipeline com binários assinados." Reduz prioridade de "Repos públicos" (não é mais drama de demo apenas — vira distribuição). |
| `docs/12-sales-cut.md` §2 e §5 | Pipeline atualizado: "Code (laptop do cliente) → Roslyn → JSON → PDF (laptop do cliente)". |
| `docs/01-product-vision.md` | Mercado-alvo ganha menção ao modelo Self-Service CLI como vetor de adoção. |

### 5.3 Quem faz o quê

- **Founder:** ratifica este ADR (status Proposed → Accepted) e decide se chave GPG do operador é suficiente no Sales Cut ou se já entra HSM/Cloud KMS.
- **`backend-dev-dotnet`:** release pipeline (5.1).
- **`tech-writer-sales`:** editoriais 1, 2, 3, 7, 8, 9 (5.2).
- **`security-compliance`:** editoriais 4, 5, 6 (5.2). Atualiza briefing do advogado **antes** do agendamento da consulta — a §12 nova reduz custo da consulta.

### 5.4 Sequência

1. Founder ratifica ADR (status → Accepted).
2. `tech-writer-sales` + `security-compliance` rodam editoriais em paralelo (5.2).
3. `backend-dev-dotnet` configura release pipeline (5.1) — pode rodar em paralelo a 2.
4. Primeiro release `v0.1.0` no GitHub Releases. Esse release é o entregável que destrava Modo A na prática.
5. Founder agenda advogado com briefing atualizado.

## 6. Métricas de sucesso

A decisão é considerada validada quando:

- **Pelo menos 1 prospect aceita rodar o binário no equipamento dele** sem objeção significativa de segurança/compliance.
- **OU** advogado confirma que EULA + MSA são suficientes no fluxo Modo A (DPA fica opcional/Modo B).
- **OU** Tessera (agência) consegue verificar independentemente um laudo emitido por Atrium (contratante) baixando o mesmo binário e confirmando hash do PDF idêntico.

Qualquer um dos três sinais é suficiente. Os três juntos confirmam que o pivô vai bem.

A decisão é considerada errada se:

- **3+ prospects sucessivos** rejeitam baixar binário externo por política interna ("não instalamos software de fornecedor sem auditoria de fornecedor de 3 meses") — sinal de que o Modo A não cabe no ICP atual e Modo B precisa voltar como default.
- **Advogado** orienta que EULA + MSA são insuficientes e DPA continua obrigatório mesmo no Modo A (improvável dada a doutrina LGPD sobre software licenciado, mas possível).

## 7. Decisões deixadas para depois

- **Servidor de licença online** (V1+): chamada HTTPS do CLI antes de gerar PDF, servidor emite `scan_id`, registra hash do output, retorna assinatura PAdES. Substitui honor system. Roadmap pós-validação.
- **HSM / Cloud KMS para assinatura do binário** (V1): GPG do operador vira insuficiente quando há mais de 1 dev na org. Migrar para chave gerenciada.
- **Notarization Apple + assinatura Authenticode Microsoft** (V1+): exigências de OS para evitar "binário não confiável" no SmartScreen / Gatekeeper. Custo: certificados US$ 100–500/ano cada.
- **Telemetria opcional opt-in** (V2+): contagem de scans, versão usada, distribuição de grades. Útil para roadmap de regras, exige opt-in explícito sob LGPD.

---

## Apêndice A — Consequência jurídica em 1 parágrafo

No Modo A, o ciclo de vida de um dado pessoal eventualmente presente no código analisado (ex: nome de desenvolvedor em comentário, email em string de log) é integralmente contido no equipamento do cliente. O Lintty, como organização, **não acessa, não recebe, não armazena nem transmite** esse dado. A relação entre Lintty e cliente é, portanto, juridicamente análoga à relação entre Microsoft e quem usa Word offline: licenciamento de software, regida por EULA. O DPA, instrumento de tratamento por Operador (LGPD art. 5º VII), só se aplica ao Modo B. Esta diferença é o eixo da §12 nova do `lawyer-briefing.md` e a justificativa para a redução estimada de 30–50% no custo da consulta jurídica inicial.

## Apêndice B — Por que três modos e não dois

A tentação seria fundir Modo A e Modo C ("é tudo software local, não importa se CLI ou IDE plugin"). **Não é a mesma coisa:**

- **Modo A** emite PDF oficial (com hash sha256, assinado em V1+) — é o **artefato de aceite contratual**.
- **Modo C** emite advisory diagnostic inline na IDE — é **feedback de desenvolvimento**, não vira PDF, não é registro contratual.

A separação garante que ninguém confunda "rodei na minha IDE e passou" com "tenho laudo oficial". O laudo oficial só vem do `lintty-engine analyze --pdf` (Modo A) ou do scan do operador (Modo B).
