# Triage do self-scan — 2026-05-06

> Estado: motor rodou contra `Lintty.Engine.sln` depois dos 5 fixes do laudo anterior. Resultado: **F (score 0)**, **24 LNTY-009** + **1 LNTY-008**. Decisão do PO: refatorar pra valer, não baixar régua. Esta triagem decide ação por método.
>
> Cap canon de supressão: 10% das Médias/Altas. 25 violações (24 médias + 1 alta) → cap = **3 supressões** (acima disso o canon força F automático). Esta triagem usa **3 supressões** — todas reservadas para algoritmos canônicos onde quebrar piora a leitura.
>
> Fonte: `engine/laudo-engine-v2.json` (gerado nesta sessão).

## 1. Tabela de decisão (25 violações)

Legenda: REFAC = refatorar (próxima rodada do `backend-dev-dotnet`); SUPPRESS = `@lintty-ignore` justificado; REVIEW = precisa abrir o método antes de decidir. Risco: B = baixo (fora do caminho do determinismo / coberto por golden tests), M = médio (Web Inspector / CLI), A = alto (Lintty.Engine.Core e/ou Reporter no caminho do `DeterminismTests`).

### Críticos (>100 LoC)

| # | Local | LoC | Decisão | Tipo de refator | Risco | Justificativa | Status |
|---|---|---:|---|---|:---:|---|:---:|
| 1 | `Lintty.Engine.Cli/Program.cs:31 Main` | 180 | REFAC | Extract method: `ArgsParser.Parse(string[]) → ParsedArgs`, `Commands.RunAnalyzeAsync(ParsedArgs)`, `Help.Print()` | M | Parser+dispatch+I/O misturados. CLI contract é gate (exit codes, stdout JSON-only) — refator preserva contrato, só separa parsing de execução. | [x] |
| 2 | `Lintty.WebInspector/Jobs/JobWorker.cs:87 RunJobAsync` | 160 | REFAC | Extract method por fase já comentada (`── 1. Clone ──` … `── 6. Purge ──`) → `CloneStageAsync`, `ResolveTargetStage`, `RunEngineStageAsync`, `MarkCompletedAsync`. Estado partilhado vira um `JobExecutionContext` local. | M | Fases já delimitadas com comentários — extract method natural. Sandbox/cleanup do `try/finally` permanece no método principal (importante manter purge garantido). | [x] |
| 3 | `Lintty.Engine.Core/Workspace/SolutionLoader.cs:217 BuildSolutionFromProjectsAsync` | 150 | REFAC | Extract method por passe: `CreateProjectIds`, `BuildMetadataReferences`, `LoadProjectDocuments`, `ResolveProjectReferences`, `CompileInDeclarationOrder`. | A | Caminho do determinismo (ADR 0006 §4.3/§6.3 — ordem de iteração). Refator é mecânico mas DeterminismTests **deve** rodar pré e pós. Fixtures Saint/Sinner/Ninja-01 cobrem. | [x] |
| 4 | `Lintty.Engine.Core/Engine.cs:163 BuildReport` (9 params) | 146 | REFAC | **Parameter Object + Extract method**: criar `record BuildReportInputs(...)`, depois extrair `BuildLayerSummary`, `BuildMetrics`, `MapViolations`, `MapExceptions`, `MapDiagnostics`, `ComputeIds` cada um aceitando uma fatia do input. | A | 9 params é sintoma forte de SRP violado. Funções já são fatiáveis por seção do schema. Cada extract acompanhado de DeterminismTests; ordering deve ficar igual byte-a-byte. | [x] |
| 5 | `Lintty.Engine.Reporter/Layout/ViolationsList.cs:41 RenderViolation` | 123 | REFAC | Extract method por bloco visual: `RenderHeaderRow`, `RenderFileLineRow`, `RenderSymbolFqn`, `RenderRuleCopy`, `RenderAdditionalEvidence`, `RenderSnippet`, `RenderFingerprint`. | A | Reporter no caminho de `DeterminismTests` (PDF binário). Refator é puramente extração de blocos QuestPDF — composição declarativa, não muda saída. Rodar Determinism antes/depois. | [x] |

### Substanciais (80–99 LoC)

| # | Local | LoC | Decisão | Tipo de refator | Risco | Justificativa | Status |
|---|---|---:|---|---|:---:|---|:---:|
| 6 | `Lintty.Docs.Pdf/Program.cs:21 Main` | 99 | REFAC | Extract method: `ParseDocsArgs`, `RunCoverCommand`, `RunSampleCommand`. | B | CLI separada (white-label brand). Não toca laudo. Refator linear. | [x] |
| 7 | `Lintty.Engine.Core/Engine.cs:51 AnalyzeAsync` | 110 (laudo lista 110, listagem do user dizia 97) | REFAC | Extract method: `LoadAndTag`, `RunAllAnalyzers`, `CollectSuppressions`. Mantém `BuildReport` como ponto único de assemblagem. | A | Coração do pipeline. Refator esperado: passar `AnalysisContext` adiante; loop de analyzers vira `RunAllAnalyzersAsync(context, _analyzers)`. DeterminismTests obrigatório. | [x] |
| 8 | `Lintty.Engine.Reporter/Model/ReportViewParser.cs:15 Parse` | 97 | REFAC | Extract method por seção do schema: `ParseHardLocks`, `ParseLayerSummaryDict`, `ParseViolationsArray`, `ParseExceptionsArray`, `ParseDiagnosticsArray`. Helpers locais `Str/Int/Bool` ficam no escopo principal. | M | Parser auxiliar do Reporter; tem golden via DeterminismTests. Cada seção é independente — extract method ortogonal. | [x] |
| 9 | `Lintty.WebInspector/Endpoints/JobsEndpoints.cs:116 CreateJob` | 84 | REFAC | Extract method: `ValidateRateLimit`, `ValidateUrl`, `CheckIdempotency`, `EnqueueJob`. Endpoint vira sequência declarativa. | M | Web Inspector — alto valor manter legível (security-critical). Não toca caminho do determinismo. | [x] |
| 10 | `Lintty.Engine.Core/Analyzers/Lnty008_PortsAtBoundaries.cs:27 AnalyzeAsync` | 84 | REFAC | Extract method: `IsBoundaryCandidate(symbol)`, `HasMatchingPortInDomain(symbol, ctx)`, `BuildViolation(symbol)`. Mantém o walk principal enxuto. | A | Analyzer no caminho do determinismo. Padrão: walk + classify + emit. Espelhar a estrutura dos outros analyzers (Lnty001/003 já estão menores). | [x] |
| 11 | `Lintty.Engine.Core/Tagging/LinttyConfig.cs:44 LoadOrDefault` | 83 | REFAC | Extract method: `ReadYamlOrEmpty`, `ParseExplicitMap`, `ParseConventionMap`, `ParseToggles`, `ApplyDefaults`. | A | Config carrega regra/canon — qualquer bug aqui muda comportamento global. Cobertura existente nos fixtures (Saint usa convention, Sinner usa explicit_map). | [x] |
| 12 | `Lintty.Engine.Core/Workspace/SolutionLoader.cs:50 LoadFromSolutionAsync` | 81 | REFAC | Extract method: `OpenMsBuildWorkspace`, `CollectWorkspaceWarnings`, depois delegar a `BuildSolutionFromProjectsAsync` (já existente). Já é fino — só está com setup misturado. | A | Determinismo cross-platform. Refator quase só separa setup do MSBuildLocator/Workspace. | [x] |
| 13 | `Lintty.WebInspector/Jobs/EngineSubprocessRunner.cs:39 RunAsync` | 80 | REFAC | Extract method: `BuildProcessStartInfo`, `CaptureStreamsAsync`, `EnforceTimeout`. Process orchestration tem fases distintas. | M | Wrap do CLI; o que sai daqui alimenta o cross-determinism gate (PDF idêntico CLI vs Web). Não toca o conteúdo — só lifecycle do processo. | [x] |
| 14 | `Lintty.WebInspector/Jobs/GitCliClient.cs:34 CloneAsync` | 79 | REFAC | Extract method: `BuildCloneArgs`, `ExecuteGitProcess`, `MapCloneError`. Hoje monta args, dispara processo e classifica erro num bloco só. | M | Sandbox-critical (egress github.com only). Refator não muda comportamento de rede. | [x] |

### Limítrofes (60–78 LoC)

| # | Local | LoC | Decisão | Tipo de refator | Risco | Justificativa | Status |
|---|---|---:|---|---|:---:|---|:---:|
| 15 | `Lintty.Engine.Core/Analyzers/Lnty007_DependencyCycles.cs:71 BuildBoundedContextGraphAsync` | 77 | **SUPPRESS** | — | — | Construção de grafo dirigido a partir de `SemanticModel` é coesa por natureza: visit → resolve symbol → arestas. Quebrar em N métodos privados que cada um precisa do mesmo `SemanticModel` + `LayerByProject` + acumulador esconde a topologia do que está sendo feito. Ver §3 abaixo. | [x] |
| 16 | `Lintty.Engine.Core/Workspace/TargetResolver.cs:192 ValidateAndResolveProjects` | 76 | REFAC | Extract method: `EnsureNoDuplicates`, `ResolveAndValidatePath`, `EnsureExists`. Validações empilhadas viram pipeline. | A | ADR 0006. Cobertura via fixtures. | [x] |
| 17 | `Lintty.Engine.Core/Analyzers/Lnty007_DependencyCycles.cs:189 TarjanScc` | 75 | **SUPPRESS** | — | — | Algoritmo de Tarjan iterativo (necessário pra evitar stack overflow em grafos profundos). Estrutura nominal de livro. Ver §3. | [x] |
| 18 | `Lintty.Engine.Core/Analyzers/Lnty001_DomainLayerIsolation.cs:37 AnalyzeAsync` | 72 | REFAC | Extract method: `IsDomainProject(project)`, `CheckUsingsAndReferences(syntaxTree, project)`, `EmitViolation(...)`. Espelhar padrão dos outros analyzers. | A | Hard lock — qualquer regressão é grave. Refator é só estrutura, não muda decisão de violation. Saint/Sinner cobrem. | [x] |
| 19 | `Lintty.Engine.Core/Workspace/TargetResolver.cs:94 ResolveFromDir` | 72 | REFAC | Extract method: `TryResolveFromYaml`, `TryResolveFromSln`, `TryResolveFromSingleCsproj`. Cada caminho do ADR 0006 vira um método nominal. | A | ADR 0006. Os 3 caminhos têm regras diferentes — extract method **melhora** legibilidade do contrato. | [x] |
| 20 | `Lintty.Engine.Reporter/Layout/DependencyGraphPlaceholder.cs:27 Compose` | 72 | REFAC | Extract method: `RenderHeader`, `RenderPlaceholderBlock`, `RenderFooter`. Mesmo padrão de `RenderViolation` (#5). | A | Determinismo do PDF. Refator puro de composição QuestPDF. | [x] |
| 21 | `Lintty.Docs.Pdf/Program.cs:127 BuildSampleDocument` | 65 | REFAC | Extract method por seção do sample (capa, sumário, blocos exemplo). | B | Documento de exemplo (white-label), não toca laudo. | [x] |
| 22 | `Lintty.Docs.Pdf/Layout/ContentPage.cs:37 Render` | 64 | REFAC | Extract method por bloco do content (header, body, callouts, footer). | B | White-label brand template, fora do caminho do laudo. | [x] |
| 23 | `Lintty.Engine.Core/Scoring/Scorer.cs:23 Compute` | 63 | **SUPPRESS** | — | — | Implementação literal da fórmula canônica do scoring (pesos C=25/H=10/M=4/L=1, arredondar pra 5 mais próximo, clamp 0–100, regras de F automático). Quebrar a fórmula em N privados esconde a lei do canon. Ver §3. | [x] |
| 24 | `Lintty.WebInspector/Program.cs:58 ConfigureServices` | 61 | REFAC | Extract method por bloco DI: `RegisterJobsInfrastructure`, `RegisterValidationServices`, `RegisterRateLimit`, `RegisterEngineRunner`. Padrão `IServiceCollection` extension methods. | B | Composition root. Refator é purgar inline em extension methods — zero impacto runtime. | [x] |

### Hard de design (LNTY-008)

| # | Local | Severidade | Decisão | Detalhe | Status |
|---|---|---|---|---|:---:|
| 25 | `Lintty.Engine.Reporter/PdfReporter.cs:24` (classe) | **high** | REVIEW + DESIGN CALL | Veja §4. Resolvido via caminho (c): reclassificar `Lintty.Engine.Reporter` para `presentation` no `lintty.yml` (Lote 0). | [x] |

## 2. Resumo dos REFACs por risco

| Risco | Itens | Total |
|---|---|---:|
| **Alto** (Core/Reporter, caminho do determinismo) | #3, #4, #5, #7, #10, #11, #12, #16, #18, #19, #20 | 11 |
| **Médio** (CLI/Web Inspector) | #1, #2, #6 (não — é B), #8, #9, #13, #14 | 6 |
| **Baixo** (Docs.Pdf / DI / fora do laudo) | #6, #21, #22, #24 | 4 |

Total REFAC: **21**. SUPPRESS: **3**. REVIEW (LNTY-008): **1**.

## 3. Supressões — strings literais para o `backend-dev-dotnet` colar

> As 3 supressões são todas em `Lintty.Engine.Core`. Vão exatamente acima da linha do método. Justificativas têm ≥30 caracteres e são razão arquitetural concreta — não "muito grande".

### 3.1 — `Lnty007_DependencyCycles.cs:189` `TarjanScc`

```csharp
// @lintty-ignore: LNTY-009 reason="Algoritmo de Tarjan SCC iterativo (necessário para evitar stack overflow em grafos profundos). Estrutura nominal de livro: index, lowlink, stack, onStack, recursão StrongConnect emulada com Stack<work>. Quebrar em métodos privados separa estado mutável compartilhado e degrada a leitura sem ganho arquitetural."
private static List<List<string>> TarjanScc(Dictionary<string, HashSet<string>> graph)
```

### 3.2 — `Scorer.cs:23` `Compute`

```csharp
// @lintty-ignore: LNTY-009 reason="Implementação literal da fórmula canônica de scoring (Canon §scoring): pesos C=25/H=10/M=4/L=1, arredondar para múltiplo de 5, clamp 0-100, regras de F automático por crítica aberta e por cap de supressão >10%. A fórmula é a lei do canon — fragmentar em métodos privados oculta a regra de negócio. Coesão é a feature."
public static Result Compute(IReadOnlyList<Violation> violations, IReadOnlyList<LinttyIgnoreParser.Suppression> suppressions)
```

### 3.3 — `Lnty007_DependencyCycles.cs:71` `BuildBoundedContextGraphAsync`

```csharp
// @lintty-ignore: LNTY-009 reason="Construção do grafo de bounded contexts a partir de SemanticModel é uma travessia coesa: para cada syntax tree, resolver o symbol type-aware, mapear o BC do declarante e do referenciado, registrar a aresta. Estado compartilhado (SemanticModel, LayerByProject, dicionário acumulador) torna extract method artificial — passar 4 parâmetros para um helper que faz uma linha de cada vez é piorar a leitura."
private async Task<Dictionary<string, HashSet<string>>> BuildBoundedContextGraphAsync(AnalysisContext context)
```

> **Cap atingido.** Qualquer outra supressão acima destas três força grade F automática (Canon §scoring §10%). Não conceder mais por estética; só por mérito arquitetural.

## 4. Análise do LNTY-008

**Local:** `engine/src/Lintty.Engine.Reporter/PdfReporter.cs:24` — classe `PdfReporter`.
**Severidade:** high (não é hard lock, mas é o único Alto do scan).
**Evidência do laudo:** `expected_in: domain`, `missing_port: IPdfReporter`.

### O que LNTY-008 está dizendo

A regra "Ports at Boundaries" exige que adaptadores de Infrastructure dependam de uma porta declarada no Domain (interface no namespace de Domain). Hoje `PdfReporter` é uma classe estática concreta no projeto `Lintty.Engine.Reporter`, e quem chama é o `Lintty.Engine.Cli/Program.cs` — diretamente, sem porta.

**Mas há um problema de fundo:** `Lintty.Engine.Reporter` **não é um adaptador de Infrastructure no sentido do canon.** Ele é **um second binary** ao lado do motor — recebe o JSON já produzido pelo Core e gera o PDF. Não roda dentro do pipeline de análise; é invocado pelo CLI depois que o `ReportDto` foi serializado.

O analyzer LNTY-008 está vendo `PdfReporter` como um adaptador no projeto Reporter (que ele tagueia como Infrastructure por convenção) e pedindo a porta correspondente em Domain. **Isso é um falso positivo do ponto de vista canônico** — não há domínio para `PdfReporter` (o "domínio" do laudo é o JSON; PDF é uma camada de apresentação alternativa, não um adaptador de port).

### Três caminhos possíveis

**(a) Tratar como falso positivo do analyzer** — refinar LNTY-008 pra reconhecer que projetos cujo único entrypoint recebe DTO já-construído não exigem porta. **Risco:** mexer em analyzer no caminho do determinismo. Precisa novo fixture.

**(b) Criar a porta de fachada** (`IPdfReporter` em Domain, implementação no Reporter, injetar no CLI). **Custo arquitetural:** introduz uma indireção que não serve a ninguém — só silencia o linter contra ele mesmo. **Não recomendo.**

**(c) Mudar a tag de `Lintty.Engine.Reporter` para `Presentation` em vez de `Infrastructure`** (via `lintty.yml` na raiz do `engine/`). PDF é apresentação alternativa do mesmo conteúdo (JSON é apresentação canônica). LNTY-008 só dispara em adaptadores de Infrastructure. **Custo:** zero — uma linha em `lintty.yml`. **Implicação:** layer_summary fica mais honesto também (Reporter sempre foi uma rota de apresentação, não uma integração com sistema externo).

### Recomendação

**Caminho (c).** Adicionar em `engine/lintty.yml`:

```yaml
explicit_map:
  Lintty.Engine.Reporter: presentation
  Lintty.Docs.Pdf: presentation
```

Isso reflete a verdade do código: ambos são geradores de artefato pra consumo humano, não integrações com sistema externo. Resolve LNTY-008 sem violência arquitetural. Discutir com `software-architect` antes de mergear caso o `qa-engineer` queira fixture novo pra cravar (recomendo: sim, fixture com adaptador real numa pasta `infrastructure/` que **deve** disparar LNTY-008, pra confirmar que o analyzer continua sensível).

> Atenção: caminho (c) **não dispensa** o REFAC do `RenderViolation` (#5). LNTY-009 continua válido em Presentation.

## 5. Ordem de execução sugerida

Princípio: cortar primeiro o que é seguro e dá ganho de clareza pro time, deixar pro fim os que tocam o caminho do determinismo. **Antes de cada lote, rodar `dotnet test tests/Lintty.Engine.Reporter.Tests` (DeterminismTests).** Se yellow, parar e investigar a fonte de não-determinismo no commit dessa rodada — não bypassar.

### Lote 1 — Quick wins fora do caminho do determinismo (risco B)
Aplicar todas, mergear em PRs de até 3 itens cada:
- #6 `Docs.Pdf/Program.Main` (99 LoC)
- #21 `Docs.Pdf/Program.BuildSampleDocument` (65 LoC)
- #22 `Docs.Pdf/ContentPage.Render` (64 LoC)
- #24 `WebInspector/Program.ConfigureServices` (61 LoC)

**Por quê primeiro:** zero risco de quebrar laudo ou fixtures, devs ganham musculatura no padrão de extract method, dá pra revisar PR rápido.

### Lote 2 — Resolver LNTY-008 (#25)
Aplicar caminho (c): editar `engine/lintty.yml`, rodar self-scan, confirmar que LNTY-008 some. Em paralelo, `qa-engineer` cria fixture cobrindo um adaptador real de Infrastructure (HTTP client / DB) sem porta em Domain — confirmar que o analyzer ainda dispara nesse caso.

### Lote 3 — Web Inspector e CLI (risco M)
- #1 `Cli/Program.Main`
- #2 `JobWorker.RunJobAsync`
- #9 `JobsEndpoints.CreateJob`
- #13 `EngineSubprocessRunner.RunAsync`
- #14 `GitCliClient.CloneAsync`

**Por quê:** caminho do cross-determinism gate, mas o conteúdo do PDF não muda — só o lifecycle do processo / wrapping HTTP. `qa-engineer` deve rodar gate cruzado CLI↔Web Inspector depois deste lote.

### Lote 4 — Reporter (risco A, mas mecânico)
- #5 `ViolationsList.RenderViolation`
- #8 `ReportViewParser.Parse`
- #20 `DependencyGraphPlaceholder.Compose`

**Por quê:** caminho direto do `DeterminismTests`. Rodar test antes/depois de cada PR. Refator é puramente extração de blocos QuestPDF — saída byte-idêntica é a meta.

### Lote 5 — Engine Core analyzers (risco A)
- #10 `Lnty008.AnalyzeAsync`
- #18 `Lnty001.AnalyzeAsync` (hard lock — extra cuidado)

**Por quê:** golden suite (Saint/Sinner/Ninja-01) cobre. Rodar todos os fixtures + DeterminismTests depois de cada extract.

### Lote 6 — Engine Core orchestration (risco A — o pior por último)
- #16 `TargetResolver.ValidateAndResolveProjects`
- #19 `TargetResolver.ResolveFromDir`
- #11 `LinttyConfig.LoadOrDefault`
- #12 `SolutionLoader.LoadFromSolutionAsync`
- #3 `SolutionLoader.BuildSolutionFromProjectsAsync`
- #7 `Engine.AnalyzeAsync`
- **#4 `Engine.BuildReport` por último** — Parameter Object + 6 extract methods, esse é o que mais tem chance de introduzir não-determinismo (ordering, capitalização, sorted dictionaries).

**Por quê por último:** se algum lote anterior introduziu não-determinismo, queremos isolar. Quando este lote começa, tudo o mais já está estável.

### Lote 7 — Aplicar as 3 supressões
Só **depois** dos 6 lotes acima. As supressões são uma decisão final, não um atalho. Se algum REFAC anterior reduzir um método pra <50 LoC, **a supressão correspondente não precisa existir.** Reavaliar §3 antes de aplicar.

## 6. O que esta triagem **não** decidiu

- **Threshold de 50 LoC do LNTY-009.** O canon-defined está em `docs/02-canon-v1.md` e este scan respeita o atual. Discussão sobre baixar/subir threshold é trabalho do `product-owner` + `software-architect`, não desta triagem.
- **Custom rules por cliente.** Continua proibido (princípio canon §3). Toggles sim, custom rules não.
- **Schema bump.** Nenhum dos REFACs propostos muda o schema JSON `1.0`. Se algum durante a execução exigir, abrir ADR antes de mergear.

---

**Próxima decisão a tomar (antes do Lote 1 começar):** confirmar com o PO se o caminho (c) do LNTY-008 é aceitável (mover Reporter/Docs.Pdf para Presentation via `lintty.yml`). Tudo o mais nesta triagem é decisão arquitetural já tomada.

---

## 7. Execução — registro 2026-05-06

Execução completa em **8 commits** (1 Lote 0 + 7 Lotes de refator/suppress). Suíte 45/45 verde em todos os commits; `DeterminismTests` 4/4 verde antes e depois de cada lote do caminho do determinismo.

| Lote | Itens cobertos | Commit |
|---|---|---|
| 0 | Reclassificar Reporter como Presentation | `ebc49b2` |
| 1 | #6, #21, #22, #24 (risco B) | `9657d01` |
| 3 | #1, #2, #9, #13, #14 (Web Inspector + CLI) | `b051cf9` |
| 4 | #5, #8, #20 (Reporter — caminho determinismo) | `308d744` |
| 5 | #10, #18 (analyzers) | `93060f1` |
| 6 | #3, #4, #7, #11, #12, #16, #19 (Engine Core orchestration — risco A pior) | `e62db40` |
| 7 | 3 SUPPRESS (#15, #17, #23) | `4caa760` |

**Self-scan v3:** `score=100, grade=A, violations=3 (todas suprimidas), hard_locks=[], exceptions=3 (válidas).** Cap canon esgotado nas 3 supressões reservadas pelo architect — qualquer LNTY-009 nova vai forçar F.
