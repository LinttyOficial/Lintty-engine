# QA — Self-Scan do Motor (laudo-engine.pdf, 2026-05-06)

Catálogo de bugs encontrados ao rodar o motor Lintty contra ele mesmo
(`engine/Lintty.Engine.sln`). Esse laudo é o ativo de venda do V0 — qualquer
defeito visível aqui vai aparecer na demo. Documento congela diagnósticos e
fixes propostos para autorização do `software-architect` antes do
`backend-dev-dotnet` implementar.

**Não implementar até autorização.** Cada fix tem âncora no código e plano de
teste; alguns mexem em `RuleCatalog` (sem efeito no JSON, sai do gate de
determinism Reporter), outros em `Engine.cs::BuildReport` (mudam JSON e
exigem regenerar `fixtures/the-*/expected.json`).

---

## Sumário do scan

| Item | Valor reportado | Observado |
|---|---|---|
| Solution | `Lintty.Engine.sln` | OK |
| Score / Grade | 5 / F | Calibração discutível (item C) |
| Violações | 24 | Todas LNTY-009 |
| Hard locks | 0 | OK (motor não tem SQL nem `using` proibido) |
| Layer summary | Domain 1/23/12, demais zerados | **Bug** — 8 dos 9 projetos do `.sln` foram tratados como `Unknown` e somem do laudo |
| Sumário Executivo | "1 projeto, 23 arquivos, 9380 LoC" | **Bug derivado** do anterior |

---

## Bug #1 — Texto do `RuleCatalog["LNTY-009"]` mente sobre o threshold (CRÍTICO)

### Evidência no laudo
Cada uma das 24 violações traz:

> "O slice semântico do método ultrapassa o budget cognitivo de análise (8K
> tokens), o que é proxy estrutural para violação severa de Single
> Responsibility Principle..."

Mas a evidência adicional dos métodos é:
- `loc_count=64;  token_count_estimate=1600;  tokenizer=placeholder_sprint0`
- `loc_count=99;  token_count_estimate=2475;  tokenizer=placeholder_sprint0`
- `loc_count=172; token_count_estimate=4300;  tokenizer=placeholder_sprint0`

Maior caso é 4300 tokens, longe dos 8K prometidos. CTO lê e perde a confiança em 5s.

### Root cause
- `engine/src/Lintty.Engine.Reporter/Model/RuleCatalog.cs:80-86` — `ArchitecturalReasoning` cita "8K tokens" e "budget cognitivo de análise". Esse texto é leftover do plano V1+ LLM (`docs/futuro/llm-ops.md`).
- `engine/src/Lintty.Engine.Core/Analyzers/Lnty009_MethodExceedsAnalyzability.cs:28-29` — o threshold real é `MaxLocPlaceholder = 60` LoC, com `TokenFactor = 25` (placeholder até a Sprint 1 do plano antigo, que não vai mais existir no V0). A violação dispara em ≥ 61 LoC, aproximadamente ≥ 1525 tokens estimados.
- `WhyItMatters` (linha 82) está honesto ("método grande demais para ser entendido em um sentido só de leitura") — só `ArchitecturalReasoning` precisa ser refeito.

### Fix proposto
Reescrever `RuleCatalog.cs` LNTY-009 `ArchitecturalReasoning` para refletir a régua real (LoC), tirar a menção a "8K tokens" e "tokenizer", e citar SRP sem inventar metáforas que o motor não mede:

```
ArchitecturalReasoning:
  "O método excede o teto de 60 linhas executáveis adotado pelo Canon como " +
  "proxy estrutural para violação severa de Single Responsibility Principle. " +
  "Métodos nesse tamanho normalmente concentram múltiplas responsabilidades — " +
  "validação, persistência, regra de negócio e formatação — que pertencem a " +
  "Domain Services dedicados ou a métodos privados extraídos. A refatoração " +
  "em unidades menores precede qualquer outra correção arquitetural, porque " +
  "métodos longos escondem bugs de invariante e impedem code review honesto."
```

Atualizar também o canon (`docs/02-canon-v1.md` §LNTY-009) para deixar de prometer 8K tokens — a régua oficial V0 é `loc_count > 60`. Texto sugerido:

> **Detecção (V0):** método cujo corpo (`Body` ou `ExpressionBody`) tem mais
> de 60 linhas executáveis físicas é reportado. A métrica `token_count_estimate`
> permanece no JSON como informação adicional (loc × 25), mas não é o
> threshold. V1+ pode trocar para tokenizer Anthropic real (8K tokens).

### Impacto em fixtures / determinism
- **`expected.json`**: sem mudança — `RuleCatalog` é display-only no Reporter, não toca o JSON do motor (`Engine.cs::BuildReport` nunca lê o catálogo).
- **`tests/Lintty.Engine.Reporter.Tests/DeterminismTests.cs`**: continua passando — ele compara dois runs do mesmo build sobre o mesmo JSON; trocar a string do catálogo afeta os dois runs igual.
- Risco zero pra `*FixtureTests.cs` (Saint/Sinner/Ninja-01) — eles não inspecionam o RuleCatalog.

---

## Bug #2 — `tokenizer=placeholder_sprint0` vazando como "evidência adicional" (CRÍTICO)

### Evidência no laudo
Cada uma das 24 LNTY-009 mostra:
> "Evidência adicional: loc_count=64; token_count_estimate=1600; **tokenizer=placeholder_sprint0**"

`placeholder_sprint0` é scaffold interno e contradiz o tom do laudo
deterministico que a gente vende ("zero alucinação, motor maduro").

### Root cause
- `engine/src/Lintty.Engine.Core/Analyzers/Lnty009_MethodExceedsAnalyzability.cs:69` —
  `["tokenizer"] = "placeholder_sprint0"`. Isso vai pro JSON
  (`evidence.additional_context.tokenizer`) e o reporter renderiza tudo que
  encontra em `additional_context` (ver `ViolationsList.cs:127-141`).

### Fix proposto
**Opção A (preferida):** trocar o valor por `"loc_v1"` (honesto: a régua é LoC,
não tokenizer). Mantém o campo no JSON pra clientes que dependem do contrato e
aponta a evolução prevista (V1+ vira `"anthropic_v1"`).

**Opção B:** remover o key `tokenizer` do `AdditionalContext` quando estamos
na régua placeholder. Reduz superfície do JSON mas é mudança de schema observável.

Recomendo **Opção A**: zero ruído de schema, comunicação honesta no PDF. Texto
no laudo passa a ser:
> "Evidência adicional: loc_count=64; token_count_estimate=1600; tokenizer=loc_v1"

Alternativa estética (B+): renomear o key inteiro pra `metric=loc_count_v1` e
omitir `token_count_estimate` enquanto não houver tokenizer real. Mais limpo
mas é refactor de contrato — mantém **Opção A** como default.

### Impacto em fixtures / determinism
- **`expected.json` muda em 3 lugares**:
  - `fixtures/the-sinner/expected.json` — `MegaRepository.DoEverything` LNTY-009: `tokenizer=placeholder_sprint0` → `loc_v1`.
  - Saint e Ninja-01 não têm LNTY-009 — sem mudança.
- O `fingerprint` da violação **não muda** (depende de `RuleId + CodeSnippet + CanonVersion`, ver `Engine.cs:331-340`), só o campo de contexto.
- **DeterminismTests do Reporter quebra na primeira execução** porque consome o expected.json: precisa atualizar o fixture *antes* de rodar.
- `SinnerFixtureTests` continua passando (testa contagem, não evidência string).

---

## Bug #3 — Layer summary perde 8 de 9 projetos quando rodam em modo convention (ALTO)

### Evidência no laudo
Layer summary:
- Domain: 1 projeto, 23 arquivos, 12 violações
- Application / Infrastructure / Presentation: 0/0/0

Mas o `.sln` tem 9 projetos: `Lintty.Engine.Core`, `Lintty.Engine.Cli`,
`Lintty.Engine.Reporter`, `Lintty.Docs.Pdf`, `Lintty.WebInspector`,
`Lintty.Engine.Cli.Tests`, `Lintty.Engine.Core.Tests`,
`Lintty.Engine.Reporter.Tests`, `Lintty.WebInspector.Tests`.

E a lista de violações cita **24** entradas em arquivos sob `src/Lintty.Engine.Cli/`,
`src/Lintty.Engine.Reporter/`, `src/Lintty.Docs.Pdf/`, `src/Lintty.WebInspector/` —
e o cabeçalho diz "12 violações em Domain". A tabela mente.

### Root cause (encadeado)

1. **`LayerTagger.cs:59`** retorna `Layer.Unknown` silenciosamente quando o
   nome do projeto não casa com nenhum pattern do `convention_map`. O default
   (`LinttyConfig.DefaultConventionMap`) é:
   - `domain`: `*.Domain`, `*.Domain.*`, `*.Core` ← pega só `Lintty.Engine.Core`
   - `application`: `*.Application`, `*.Application.*`, `*.UseCases`
   - `infrastructure`: `*.Infrastructure`, `*.Infrastructure.*`, `*.Persistence`, `*.Data`
   - `presentation`: `*.Api`, `*.Web`, `*.Controllers`, `*.Presentation`

   `Lintty.Engine.Cli`, `Lintty.Engine.Reporter`, `Lintty.Docs.Pdf`,
   `Lintty.WebInspector` e os 4 `*.Tests` **não casam com nenhum**. Vão pra
   `Layer.Unknown`.

2. **`Engine.cs:163-185`** monta `layerCounts` só com os 4 layer keys
   esperados (`domain/application/infrastructure/presentation`). Linha 170 e
   linha 182 fazem `if (!layerCounts.ContainsKey(key)) continue;` — projetos e
   violações em `Layer.Unknown` (key `"unknown"`) **somem do laudo
   silenciosamente**.

3. **`LayerForViolation` (Engine.cs:307-329)** aplica a mesma lógica para
   atribuir uma violação a um layer baseado no path do arquivo — mas só checa
   `path.StartsWith` contra projetos cuja `Layer != Unknown`. Violações em
   path Unknown caem no `return Layer.Unknown` do final.

4. **Fail-fast violado**: o canon (`docs/02-canon-v1.md` §"Fail-fast")
   determina que projetos não classificados **abortam o scan** com
   `LayerTaggingError`. Não há nenhum lugar em `Engine.AnalyzeAsync` que faça
   essa checagem. Hoje o motor silenciosamente come os Unknowns.

### Fix proposto (3 partes — fica todo num só PR)

**Parte A — Fail-fast em projetos não classificados**, para ficar fiel ao canon:
em `Engine.AnalyzeAsync` (linha ~84), depois de classificar todos, verificar se
algum `layerByProject[name] == Layer.Unknown` e:
- Se sim, lançar `LayerTaggingError` com a lista dos projetos não classificados
  e instruções: "Adicione esses projetos ao `explicit_map` ou ajuste o
  `convention_map` em `lintty.yml`."
- O `Lintty.WebInspector.Jobs.JobWorker.cs:319` já mapeia esse erro pra
  `JobErrorCode.LayerTaggingError` — sem trabalho extra na ponta web.

**Parte B — Suportar layer "unknown" como bucket visível** (alternativa
defensiva caso o A seja considerado disruptivo demais pra V0):
- Adicionar `"unknown"` em `Engine.cs:163` na lista de layers iniciais.
- Mostrar a linha "Não classificado" no `layerSummary` do PDF.
- Pior pro pitch ("se o motor não consegue classificar, ele admite") mas
  mantém o motor rodando contra solutions reais sem `lintty.yml`.

**Recomendação:** Parte A (fail-fast) + criar `engine/lintty.yml` com
`explicit_map` cobrindo todos os 9 projetos do próprio motor.
Self-scan vira parte do CI: motor sobre o motor tem que devolver A com 0
LNTY-009 (ou C/D após calibração — ver item C). Esse é o melhor demo possível
("a gente come a própria comida").

**Parte C — `engine/lintty.yml` proposto:**
```yaml
canon_version: "1.0.0"
layer_tagging:
  mode: explicit
  explicit_map:
    "Lintty.Engine.Core.csproj":          "domain"
    "Lintty.Engine.Cli.csproj":           "presentation"
    "Lintty.Engine.Reporter.csproj":      "infrastructure"
    "Lintty.Docs.Pdf.csproj":             "infrastructure"
    "Lintty.WebInspector.csproj":         "presentation"
    "Lintty.Engine.Cli.Tests.csproj":     "presentation"
    "Lintty.Engine.Core.Tests.csproj":    "domain"
    "Lintty.Engine.Reporter.Tests.csproj":"infrastructure"
    "Lintty.WebInspector.Tests.csproj":   "presentation"
```

Justificativa de cada layer: o motor é internamente uma arquitetura de duas
camadas (Core = domain estável, todo o resto = adapters de I/O), então
classificar `Reporter` e `Docs.Pdf` como "infrastructure" e o `Cli`/
`WebInspector` como "presentation" representa a realidade. Tests herdam o
layer da peça que testam.

### Impacto em fixtures / determinism
- Saint/Sinner/Ninja-01: já têm `lintty.yml` com `explicit_map` — não afetados.
- Saint-no-sln: tem `lintty.yml` — não afetado.
- `expected.json` desses 4 fixtures: não muda.
- Self-scan (engine sobre engine): laudo passa a refletir os 9 projetos. Não
  existe expected.json pra self-scan ainda; ver "Fixtures novas" no fim.

---

## Bug #4 — Inconsistência "1 projeto, 23 arquivos, 9380 LoC" no Sumário Executivo (MÉDIO)

### Evidência no laudo
Sumário Executivo:
> "Análise estática type-aware sobre solution `Lintty.Engine.sln` (1 projeto, 23 arquivos, 9380 LoC)"

Escopo da análise (logo abaixo):
> "9 projetos carregados via Lintty.Engine.sln."

### Root cause
- O número "1 projeto" do Sumário Executivo vem de
  `ExecutiveSummary.cs:62-66` somando `r.LayerSummary.Values.Projects`. Esse
  somatório está intoxicado pelo Bug #3 — só Domain (1) entra no total porque
  os outros 8 viraram Unknown e foram filtrados em `Engine.cs`.
- O número "9 projetos" do bloco "Escopo da análise" vem de
  `metrics.projects_analyzed` (`MetricsDto.ProjectsAnalyzed = projects.Count` em
  `Engine.cs:222`) — esse é o total de `compilations` carregadas pelo
  workspace, **antes** do filtro de layer.
- Resultado: dois campos contam coisas diferentes na mesma página.

### Fix proposto
Resolver via **Bug #3 Parte A** (fail-fast). Uma vez que todo projeto fica
classificado, os dois somatórios convergem para 9. Não tem fix isolado pra
esse bug — ele é sintoma do #3.

Defesa adicional: o cálculo de `totalProjects/totalFiles` em
`ExecutiveSummary.BuildParagraph` deveria usar
`metrics.projects_analyzed` (a fonte da verdade) em vez de somar
`LayerSummary` — assim, mesmo que o LayerSummary fique inconsistente por
algum bug futuro, o Sumário Executivo conta o que o motor de fato carregou.
Pequena mudança em `ExecutiveSummary.cs:60-67` para ler `r.Metrics.ProjectsAnalyzed`
diretamente.

### Impacto em fixtures / determinism
- Mudar `ExecutiveSummary.BuildParagraph` afeta o PDF byte-a-byte — quebra
  `DeterminismTests` na primeira execução pós-mudança? **Não** —
  `DeterminismTests` faz `GeneratePdf(json) → PdfReporter`, gera 2× e
  compara entre si; ambos vão usar o novo código. PDF antigo não é
  reprodutível mas isso não é teste.
- `expected.json` não muda — é puramente cosmético no Reporter.

---

## Bug #5 — LNTY-009 dispara em código de teste (MÉDIO)

### Evidência no laudo
`tests/Lintty.WebInspector.Tests/WorkerIntegrationTests.cs:147 RunFixtureAsync (86 LoC)`
aparece como violação. Métodos de teste com Arrange/Act/Assert longos são
normais — esse é falso positivo.

### Root cause
- `Lnty009_MethodExceedsAnalyzability` itera todas as `compilation.SyntaxTrees`
  sem filtrar paths de teste.
- `GeneratedCodeFilter.IsGenerated(tree)` só pega arquivos com markers tipo
  `<auto-generated>` — não considera convenção de path (`tests/`, `*.Tests.csproj`).
- O canon não diz nada explicitamente sobre tests/. O comportamento atual é
  consequência de ausência, não de design.

### Fix proposto
Excluir trees pertencentes a projetos cujo csproj termina em `.Tests.csproj`
ou cujo path contém `/tests/` (case-insensitive, separador normalizado).
Implementar como filtro **opcional global**, não só no LNTY-009 — outras regras
podem se beneficiar (ex: LNTY-006 não devia avisar sobre `OrderHelper` num teste,
LNTY-008 não devia exigir port em adapter de teste).

Onde:
- Novo helper `Workspace/TestProjectFilter.cs`:
  ```csharp
  public static bool IsTestProject(Project p)
      => p.FilePath?.EndsWith(".Tests.csproj", StringComparison.OrdinalIgnoreCase) == true
         || (p.Name?.EndsWith(".Tests", StringComparison.Ordinal) == true);
  ```
- `Lnty009.AnalyzeAsync` itera `context.Projects` e pula os onde
  `TestProjectFilter.IsTestProject(project)`.

**Decisão pendente** que precisa do `software-architect`: aplicar filtro
*também* a LNTY-001/002/006/008? LNTY-007 (cycles) precisa rodar — ciclo
incluindo teste é OK. LNTY-003 também. Recomendação: por ora aplicar **só
em LNTY-009** (o único que claramente sofre falso positivo de teste). As
outras regras saem por demanda quando aparecer ruído.

Alternativa mais conservadora: aumentar o threshold de teste pra 100 LoC
(em vez de 60) — mantém a régua, só relaxa pra teste. Pior: dois thresholds
em código.

**Recomendação**: excluir tests/ do LNTY-009 totalmente. Documentar no canon
LNTY-009 §"Escopo": "regra não se aplica a projetos `*.Tests`."

### Impacto em fixtures / determinism
- Saint/Sinner/Ninja-01 fixtures não têm projetos de teste internos — nenhum
  expected.json afetado.
- Self-scan: 1 violação a menos (`WorkerIntegrationTests.cs:147` sai).

---

## C — Calibração de LNTY-009: peso, threshold ou cap (NÃO É BUG)

### Estado atual (com bugs corrigidos hipoteticamente)
- 24 LNTY-009 × peso 4 (medium) = 96 pontos deduzidos.
- Score = max(0, 100 − 96) = 4, arredondado a 5/100. Grade F.
- Mas nenhum hard lock disparou. F está vindo de um único antipadrão repetido,
  não de risco arquitetural fundamental.

A mensagem que isso passa pro comprador do laudo é desbalanceada: "F"
significa "irrecuperável, não dá selo, refaz tudo". Métodos longos não são
isso — são dívida cosmético-estrutural endereçável.

### Distribuição empírica do self-scan
24 violações. Pelo PDF lido pelo usuário:
- 21 com `loc_count` entre 61 e 99 (perto do limite, refactor de 1–2h cada).
- 2 com `loc_count` entre 100 e 150 (precisam atenção).
- 1 com `loc_count = 172` (`MegaPipelineWorkflow` ou similar — refactor real).

A régua atual trata os 24 idênticos. É insensível a magnitude.

### Opções

**(a) Baixar LNTY-009 para Low (peso 1).**
- 24 × 1 = 24 pontos → score 76 → C.
- Honesto: dívida cosmética não é F.
- Custo: muda canon, muda Sinner também — `MegaRepository.DoEverything` cai pra 1pt
  e Sinner perde uma violação medium. Não muda o `F` do Sinner (ele tem 3 hard locks).

**(b) Manter Medium, afrouxar threshold para 90 LoC.**
- Self-scan: ~5 violações sobreviveriam. Score 80 → B.
- Custo: o Sinner `MegaRepository.DoEverything` (80 LoC) **sai do laudo** —
  perde uma das 9 violações que `SinnerFixtureTests` exige. Quebra fixture.
- Para preservar Sinner, teria que aumentar o método pra 91+ LoC. Aceitável,
  mas é mexer em fixture pra acomodar régua.

**(c) Cap por regra: violações da MESMA regra após N=10 contam metade do peso.**
- 24 LNTY-009 = 10×4 + 14×2 = 68 pontos. Score 30 → F. Menos drástico que 5,
  ainda F.
- Pra cair de F: N=5 com cap em 1pt: 5×4 + 19×1 = 39 pontos → score 60 → D.
- Custo: mudança em `Scorer.cs` que afeta todas as regras potencialmente.
  Difícil de explicar ao prospect ("por que esse score?"). Quebra o pitch
  "fórmula simples e auditável".

**(d) Combinar (a) + threshold mais alto pra teste/legacy.**
- Low (1pt) globalmente + threshold 60 LoC mantém detecção, mas score
  passa a ser direcionalmente útil sem ser brutal.

### Recomendação
**Opção (a)**: baixar LNTY-009 para **Low (peso 1)**. Justificativa:
- Honesto com a natureza do problema (cosmético-estrutural, não rastreável a risco arquitetural).
- Não muda nenhum hard lock ou trava no Sinner.
- Mantém detecção (a violação aparece no laudo, só não destrói o score).
- Mudança em 2 lugares: `Lnty009_MethodExceedsAnalyzability.cs:57`
  (`Severity.Medium` → `Severity.Low`) e canon (`docs/02-canon-v1.md` §LNTY-009).

Efeito numérico esperado **após Bug #3 Parte A + Bug #5 fix + opção (a)**:
- Self-scan engine: ~22 violações Low (24 menos as ~2 de teste já filtradas).
  Score 100 − 22 = 78 → arredondado a 80 → B. **Grade B é honesto pra um motor
  com método longos mas sem dívida arquitetural fundamental.** Boa demo.

### Impacto em fixtures / determinism
- `fixtures/the-sinner/expected.json`: a única LNTY-009 (`MegaRepository.DoEverything`)
  passa de severity `medium` (peso 4) pra `low` (peso 1). Isso muda o
  `score` calculado para o Sinner? **Não** — Sinner já é F por hard locks
  (LNTY-001/002/007), o cálculo de score é irrelevante. Mas o JSON muda em
  `severity` de uma violação. Atualizar `expected.json`.
- Saint: sem LNTY-009. Sem mudança.
- Ninja-01: sem LNTY-009. Sem mudança.
- `SinnerFixtureTests` continua passando (testa contagem ≥9 e presença de
  rule_id, não severity).

---

## Resumo do impacto em `expected.json`

| Fixture | Bug #1 | Bug #2 | Bug #3 | Bug #4 | Bug #5 | Calibração C |
|---|---|---|---|---|---|---|
| `fixtures/the-saint/expected.json` | – | – | – | – | – | – |
| `fixtures/the-sinner/expected.json` | – | **muda 1 evidence** (`tokenizer`) | – | – | – | **muda 1 severity** (`low`) |
| `fixtures/the-ninja-01/expected.json` | – | – | – | – | – | – |
| `fixtures/the-saint-no-sln/expected.json` | – | – | – | – | – | – |

Apenas **Sinner** precisa atualizar. As mudanças são determinísticas: rodar
`dotnet run --project engine/src/Lintty.Engine.Cli -- analyze --solution
fixtures/the-sinner/Sinner.sln --output json` após implementação dos fixes
gera o novo `expected.json` e o developer só precisa diff-comparar
manualmente que as mudanças são exatamente as duas listadas.

**`DeterminismTests` (Reporter)**: continua válido. Roda 2× sobre o mesmo
JSON e compara — mudar `RuleCatalog` ou `ExecutiveSummary` afeta os dois
runs igual. Atualizar `expected.json` do Sinner antes de rodar `dotnet test`,
para o teste consumir o JSON novo.

**Cross-determinism CLI ↔ Web Inspector**: nenhum dos fixes introduz
timestamp, host metadata, request ID ou variável de ambiente. Gate preservado.

---

## Fixtures novas necessárias

### 1. `fixtures/golden/lnty-009/just-pass/` (60 LoC limite inferior)
Método com exatamente 60 LoC executáveis. Não dispara LNTY-009. Calibra a
borda inferior. Útil pra detectar regressão "alguém mudou MaxLocPlaceholder
sem atualizar canon".

### 2. `fixtures/golden/lnty-009/just-fail/` (61 LoC, dispara)
Mesmo método, +1 statement. Dispara. Confirma o threshold real.

### 3. `fixtures/golden/lnty-009/test-project-excluded/` (Bug #5)
Solution com 1 `*.Tests.csproj` contendo método de 100 LoC. Após fix, **não
deve disparar**. Validar que filtro de teste funciona.

### 4. `fixtures/golden/layer-tagging/unknown-fail-fast/` (Bug #3)
Solution com projeto cujo nome não casa nenhum convention pattern (ex:
`MyApp.Custom`) e sem `lintty.yml`. **Espera**: `LayerTaggingError`
(exit code 2 do CLI). Hoje o teste falharia porque o motor não lança a
exceção — esse é o teste que valida o fix da Parte A do Bug #3.

### 5. `fixtures/golden/layer-tagging/explicit-map-rescue/` (Bug #3)
Mesmo projeto `MyApp.Custom` mas com `lintty.yml` mapeando ele explicitamente
pra `infrastructure`. **Espera**: scan completa sem erro, layer summary
correto.

### 6. `fixtures/the-engine-self-scan/` (novo — opcional V0+)
Snapshot do próprio engine como fixture. Se `engine/lintty.yml` for criado
(Bug #3 Parte C), o self-scan vira teste de regressão. Útil porque bate em
todas as superfícies de adapter (Cli/Reporter/WebInspector) sem ser código de
brincadeira. Convém deixar pra fase pós-fix — fica como follow-up.

---

## Plano de execução proposto (para o `backend-dev-dotnet`)

Sequência sugerida (ordem para minimizar conflito):

1. **Bug #1** — `RuleCatalog.cs` LNTY-009 ArchitecturalReasoning. Sem efeito JSON.
2. **Bug #2** — `Lnty009_MethodExceedsAnalyzability.cs:69` `tokenizer = "loc_v1"`. Atualizar
   `fixtures/the-sinner/expected.json` (1 string).
3. **Bug #5** — adicionar `Workspace/TestProjectFilter.cs` e aplicar em LNTY-009.
4. **Calibração C** — `Lnty009_MethodExceedsAnalyzability.cs:57`
   `Severity.Medium → Severity.Low`. Atualizar `fixtures/the-sinner/expected.json`
   (1 severity).
5. **Bug #3 Parte C** — criar `engine/lintty.yml` com `explicit_map`.
6. **Bug #3 Parte A** — fail-fast no `Engine.AnalyzeAsync` para
   `Layer.Unknown`. Adicionar fixture golden `unknown-fail-fast`.
7. **Bug #4** — `ExecutiveSummary.cs:60-67` ler `r.Metrics.ProjectsAnalyzed`.
   Cosmético no PDF.

Após cada item: rodar `dotnet test engine/Lintty.Engine.sln`. Os 4
`DeterminismTests` (Reporter) + 4 `*FixtureTests` + `DeterminismTests` (Core)
têm que ficar verdes. Se yellow, parar e investigar antes de seguir.

Atualização de `docs/02-canon-v1.md` LNTY-009 (texto e severity) deve
acompanhar o PR de calibração — canon em sync com motor é parte do contrato.

---

## Não-objetivos deste documento

- Não implementa nenhum fix. Aguarda autorização do `software-architect` pra
  liberar `backend-dev-dotnet`.
- Não reabre o debate da régua LNTY-009 ser baseada em LoC — é placeholder
  V0 documentado, vira tokenizer real só na V1+. Documento só pede pra parar
  de **mentir** sobre o que ele mede (Bug #1, Bug #2).
- Não cobre LNTY-004/005 (LLM, desativadas no V0).
- Não toca `DeterminismTests` cross-platform (release smoke test) — fora do
  escopo desse self-scan; gate continua aplicável.
