# Decisões — Laudo Self-Scan Fixes (2026-05-06)

Implementação dos 5 bugs catalogados em `qa-laudo-self-scan-2026-05-06.md`.
Documento registra **o que foi feito** e — mais importante — **o que o QA
propôs e o usuário rejeitou**, para que a próxima leitura do tópico não
revivа a discussão.

Owner do PR: `backend-dev-dotnet`. Validação cross-cuts: `qa-engineer`.
Estado: implementado, suíte 45/45 verde.

---

## Resumo das mudanças

| Bug | QA propôs | Usuário decidiu | Implementado? |
|---|---|---|---|
| #1 LNTY-009 mente sobre 8K tokens | Reescrever `RuleCatalog.cs` para citar 60 LoC | (mesmo) | Sim |
| #2 `tokenizer=placeholder_sprint0` vazando | Trocar por `loc_v1` (Opção A) | (mesmo) | Sim |
| #3 Layer tagging silencioso | (a) fail-fast OU (b) bucket "unknown" visível OU (c) ambos | **(c) fail-fast com mensagem útil** | Sim |
| #4 "1 projeto vs 9 projetos" no Sumário Executivo | Ler `r.Metrics.ProjectsAnalyzed` direto | (mesmo) | Sim |
| #5 LNTY-009 em código de teste | Filtro `*.Tests.csproj` aplicado só a LNTY-009 | (mesmo) | Sim |
| **Calibração C** — baixar LNTY-009 para Low (peso 1) | (a) baixar peso, virar grade B | **REJEITADA. Mantém Medium.** | Não (intencional) |

---

## Decisão do usuário sobre a Calibração C (LNTY-009 severity)

O QA propôs baixar LNTY-009 de Medium (peso 4) para Low (peso 1) para
"acomodar" o self-scan, que estava saindo F com 24 violações Medium contra
o motor. A grade F era "honestamente desbalanceada" para um problema
cosmético-estrutural, virando B com a mudança.

**O usuário rejeitou explicitamente:**

> "Não quero baixar a régua pro meu próprio projeto e roubar pra conseguir
> nota maior. Não ligo pra nota, ligo pra como o código está de verdade."

A canon não foi alterada. `Lnty009_MethodExceedsAnalyzability.cs` continua
emitindo `Severity.Medium` (peso 4). O Lintty rodando contra ele mesmo
continua tirando F enquanto não refatorarmos os métodos longos de verdade
— **isso é o ponto.** Self-scan não é régua que o produto se ajusta para
parecer bem; é diagnóstico que pauta refactor real.

Implicação operacional:
- `fixtures/the-sinner/expected.json` **não muda** o `severity` da LNTY-009
  do Sinner. Sinner continua F por hard locks (LNTY-001/002/007), com a
  Medium do MegaRepository contribuindo para o cálculo da mesma forma que
  antes.
- A pendência "refatorar os métodos > 60 LoC do motor" entra na backlog
  pós-Sprint-2-auth. Não é blocker do laudo do cliente; é blocker da
  vaidade interna.

---

## Detalhe técnico das implementações

### Fix #1 — `RuleCatalog.cs` LNTY-009 ArchitecturalReasoning

`engine/src/Lintty.Engine.Reporter/Model/RuleCatalog.cs`. O texto antigo
prometia "budget cognitivo de análise (8K tokens)" — leftover do plano
V1+ LLM, falso para o motor V0 que mede LoC. Substituído por:

> "O método excede o teto de 60 linhas executáveis adotado pelo Canon como
> proxy estrutural para violação severa de Single Responsibility Principle.
> Métodos nesse tamanho normalmente concentram múltiplas responsabilidades
> — validação, persistência, regra de negócio e formatação — que pertencem
> a Domain Services dedicados ou a métodos privados extraídos. A refatoração
> em unidades menores precede qualquer outra correção arquitetural, porque
> métodos longos escondem bugs de invariante e impedem code review honesto."

Sem efeito no JSON. Sem efeito em `expected.json`. Texto display-only,
honesto sobre o que o motor realmente mede.

### Fix #2 — `tokenizer=placeholder_sprint0` → `loc_v1`

`engine/src/Lintty.Engine.Core/Analyzers/Lnty009_MethodExceedsAnalyzability.cs:69`.
Trocado para `"loc_v1"`. Comunica honestamente: "a métrica é LoC, não
tokenizer". Mantém o campo no JSON (zero mudança de schema observável,
clientes que dependem do contrato continuam parsando). A evolução prevista
fica documentada no comentário da classe: V1+ pode trocar pelo tokenizer
Anthropic real (vira `tokenizer=anthropic_v1`).

`fixtures/the-sinner/expected.json` atualizado em 1 lugar (string
substituída em `MegaRepository.DoEverything`). Os outros 3 fixtures
(Saint, Ninja-01, Saint-no-sln) não têm LNTY-009.

### Fix #3 — Layer tagging fail-fast com mensagem útil

Implementado em três peças, conforme a decisão (c) do usuário:

1. **`engine/src/Lintty.Engine.Core/Tagging/LayerTaggingError.cs`** (nova
   exception). Carrega a lista de projetos não-classificados e gera uma
   mensagem que **ensina** o que fazer:

   ```
   ERR LayerTaggingError: project 'Foo.Bar' could not be classified to a layer.

   Add it to lintty.yml under explicit_map:

     layer_tagging:
       mode: explicit
       explicit_map:
         Foo.Bar.csproj: application

   Accepted layer tags: domain | domain.abstractions | application | infrastructure | presentation.
   See: docs/03-motor-cli.md#configuracao-de-camadas
   ```

   Para múltiplos projetos não-classificados, a mensagem lista todos com
   uma sugestão de bloco YAML completo. Default sugerido é `application`
   (bucket neutro mais seguro; cliente edita pra valor real).

2. **`engine/src/Lintty.Engine.Core/Engine.cs`** — após classificar todos
   os projetos via `LayerTagger.Classify(...)`, varre `layerByProject` em
   busca de `Layer.Unknown`. Se houver, lança `LayerTaggingError`. Convention
   mode continua tentando primeiro; só falha quando convenção *e*
   `explicit_map` falham juntas. Cliente continua podendo adicionar quantos
   projetos quiser ao map.

3. **`engine/src/Lintty.Engine.Cli/Program.cs`** — catch dedicado para
   `LayerTaggingError`. Imprime a mensagem com prefixo `ERR ` no stderr e
   sai com exit code 2 (execution error). **Sem stack trace** — o stack
   traz ruído que esconde o conselho acionável.

4. **`engine/lintty.yml`** (novo) — mapa explícito dos 9 projetos do
   próprio motor:

   | csproj | layer |
   |---|---|
   | `Lintty.Engine.Core.csproj` | domain |
   | `Lintty.Engine.Cli.csproj` | presentation |
   | `Lintty.Engine.Reporter.csproj` | infrastructure |
   | `Lintty.Docs.Pdf.csproj` | presentation |
   | `Lintty.WebInspector.csproj` | presentation |
   | `Lintty.Engine.Core.Tests.csproj` | domain |
   | `Lintty.Engine.Cli.Tests.csproj` | presentation |
   | `Lintty.Engine.Reporter.Tests.csproj` | infrastructure |
   | `Lintty.WebInspector.Tests.csproj` | presentation |

   **Justificativa das classificações** (comentário no YAML também):

   - **`Core` = domain**: implementa o canon (regras, scoring, modelo,
     workspace loader). Pure logic sobre Roslyn — Roslyn é primitiva, não
     infraestrutura, do mesmo jeito que `System.IO`.
   - **`Reporter` = infrastructure**: recebe JSON, gera PDF. File I/O +
     fontes embedded + QuestPDF. Adapter de saída puro.
   - **`Cli` = presentation**: argv → Core → stdout. Adapter de entrada
     do usuário.
   - **`WebInspector` = presentation** (não Application). QA sugeriu
     considerar Application por "orquestrar". Decidi manter Presentation
     porque o WebInspector não tem operações de domínio próprias —
     delega tudo para o subprocess da CLI. Em DDD/Hexagonal, "Application"
     é onde moram os Application Services que coordenam operações do
     domínio; o WebInspector é um adapter HTTP que envelopa a CLI, mais
     parecido com um controller do que com um use case.
   - **`Docs.Pdf` = presentation**: CLI auxiliar para documentos
     institucionais (briefings, ADRs, manuais). Não é o laudo. É um
     entrypoint user-facing.
   - **Tests**: cada test project herda o layer do que testa.

   Com esse `explicit_map`, o self-scan agora classifica os 9 projetos
   sem fail-fast e produz layer summary completo no PDF.

### Fix #4 — `ExecutiveSummary.cs` lê `Metrics.ProjectsAnalyzed`

`engine/src/Lintty.Engine.Reporter/Layout/ExecutiveSummary.cs:60-67`.
Linha do Sumário Executivo passa a usar `r.Metrics.ProjectsAnalyzed`
(fonte da verdade do workspace loader, antes de qualquer classificação)
em vez de somar `r.LayerSummary.Values.Projects`.

Defesa em profundidade: com Fix #3 implementado, projetos Unknown viram
`LayerTaggingError`, então as duas fontes deveriam concordar sempre. Mas
se um bug futuro quebrar o `LayerSummary`, o Sumário Executivo continua
contando o que o motor de fato carregou.

`totalFiles` continua somando `LayerSummary` porque o loader não expõe
um total agregado (e o critério de "arquivos analisados" exclui código
gerado, que é um filtro de layer, não de loader).

### Fix #5 — `TestProjectFilter.cs` aplicado só a LNTY-009

**Arquivo novo:** `engine/src/Lintty.Engine.Core/Workspace/TestProjectFilter.cs`.
Convenção: csproj termina em `.Tests.csproj` OU `Project.Name` termina em
`.Tests`.

**Importante** — a versão inicial do filtro também sniffava
`MetadataReferences` em busca de `xunit`. Foi **removida** depois que o
test runner expôs um falso positivo: o `ManualLoadFromSolutionAsync`
fallback em `SolutionLoader.cs` usa `TRUSTED_PLATFORM_ASSEMBLIES` do
processo host, e quando o motor roda dentro do test runner xUnit, **toda**
projeto-load herda xunit nos `MetadataReferences` — incluindo
`Sinner.Infrastructure.MegaRepository`. O sniffing virava IsTestProject
em código de produção, mascarando LNTY-009 em fixtures. Convenção via
nome de csproj/projeto é suficiente e mais previsível.

**Aplicado só em LNTY-009.** No `AnalyzeAsync`:

```csharp
foreach (var (project, compilation) in context.Projects)
{
    if (TestProjectFilter.IsTestProject(project)) continue;
    // ... resto da análise
}
```

LNTY-001/002/003/006/007/008 continuam rodando em testes. Suas
preocupações arquiteturais (dom inio puro, persistência, ciclos, ports)
não dependem do tipo de código que está chamando. A justificativa fica
comentada na classe — "do not generalize this filter into a global skip"
— para não revisitarmos a decisão por fadiga.

---

## Validação

| Gate | Estado |
|---|---|
| `dotnet build Lintty.Engine.sln` | 0 warnings, 0 errors |
| `dotnet test Lintty.Engine.sln` | **45/45 verdes** (2 Cli + 16 Core + 10 Reporter + 17 WebInspector) |
| `DeterminismTests` (Reporter + Core) | verde |
| `WorkerIntegrationTests` (cross-determinism CLI ↔ Web Inspector) | verde |
| `SinnerFixtureTests` | verde — LNTY-009 ainda detectada em Sinner.Infrastructure |
| Fail-fast manual test (`MyApp.Custom` sem `lintty.yml`) | exit code 2, mensagem útil |
| Self-scan determinístico (2 runs do motor sobre o motor) | byte-idêntico |

**Schema `1.0` preservado.** A única mudança observável no JSON é o valor
do campo `evidence.additional_context.tokenizer` em violações LNTY-009
(de `"placeholder_sprint0"` para `"loc_v1"`). Campos placeholder
(`inference_signature: null`, `audit_chain: null`, `ai_candidates: []`,
`sandbox_integrity`, etc.) intactos.

**Severidades intactas.** Nenhuma regra muda peso ou hard-lock-status.

---

## Diff observável no laudo do próprio Lintty

| Métrica | `laudo-engine.pdf` (antes) | `laudo-engine-v2.pdf` (depois) |
|---|---|---|
| Grade | F | F (decisão do usuário — não baixou régua) |
| Score | 5/100 | 0/100 (mais violações detectadas com layer mapping) |
| Hard locks | 0 | 0 |
| Projetos analisados (Sumário Executivo) | "1 projeto" | "9 projetos" |
| Projetos no Layer Summary | Domain 1, demais 0 | Domain 2, Application 0, Infrastructure 2, Presentation 5 |
| Arquivos analisados | 23 | 99 (32 + 0 + 22 + 45) |
| LoC analisada | 9.380 | 9.754 |
| Total de violações | 24 | 25 (24 LNTY-009 + 1 LNTY-008) |
| Texto LNTY-009 ArchitecturalReasoning | Cita "8K tokens" e "tokenizer" | Cita "60 linhas executáveis" e SRP |
| Evidência adicional `tokenizer` | `placeholder_sprint0` | `loc_v1` |
| Test code aparece em LNTY-009? | Sim (1 método `RunFixtureAsync` em `WorkerIntegrationTests.cs`) | Não (filtrado) |

A mudança em "Total de violações" (24 → 25) merece nota: o motor antigo
enxergava só 1 projeto (Domain), perdia LNTY-008 nos adapters
(`Reporter`/`WebInspector`/etc.) que estavam classificados como Unknown.
Com layer mapping correto, o motor agora detecta 1 LNTY-008 em
`Lintty.WebInspector` (port faltando em algum adapter público da
camada Presentation). O LNTY-009 caiu de 24 → 24 (filtro de teste retira
1 método de test, mas o número líquido é coincidência — o número exato
depende da contagem de métodos > 60 LoC em código de produção do motor
hoje, não do que aparece no laudo antigo).

---

## Não-objetivos deste PR

- Não refatorou os métodos > 60 LoC do motor — fica como dívida visível
  no laudo, intencional, "comer a própria comida".
- Não atualizou `docs/02-canon-v1.md` §LNTY-009. O texto canônico já
  fala em LoC na detecção V0 e tokenizer no V1+; estava OK. Se algum
  ponto ainda mencionar "8K tokens" como threshold V0, o
  `tech-writer-sales` pode varrer no follow-up.
- Não criou as fixtures golden propostas (`just-pass`, `just-fail`,
  `test-project-excluded`, `unknown-fail-fast`, `explicit-map-rescue`).
  São úteis e o `qa-engineer` pode adicionar como follow-up — não foram
  parte do contrato deste PR (Sprint 2 auth está bloqueado nestes 5
  fixes, não nas fixtures novas).
- Não tocou em `JobErrorCode.LayerTaggingError` (já existia desde antes
  e mapeia corretamente quando a stderr do CLI vira "ERR LayerTaggingError:
  ...", graças ao `ClassifyEngineError` em `JobWorker.cs:319` que faz
  case-insensitive contains).
