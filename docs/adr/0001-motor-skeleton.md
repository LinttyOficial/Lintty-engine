# ADR 0001 — Motor Skeleton (Sprint 0)

- **Status:** Proposed
- **Date:** 2026-04-27
- **Author:** software-architect
- **Audience:** backend-dev-dotnet, qa-engineer
- **Sprint:** 0 (Sales Cut Tier 1, semana 1) — ver `docs/12-sales-cut.md` §2.1 e §7
- **Canon pinado:** `1.0.0` (`docs/02-canon-v1.md`)

## Revisão de design (estrutura canônica)

1. **Cabe no canon arquitetural?** Sim. O motor é o componente determinístico de `docs/03-motor-cli.md` §5 (pipeline), executando offline (sem rede, sem LLM, sem Cloud Run).
2. **Princípios afetados:** #2 (Layered design — o próprio motor é em camadas), #3 (determinismo factual), #6 (hard locks sagrados), #7 (single source of truth = canon pinado).
3. **Fronteira:** binário standalone CLI → stdout JSON. Sem chamadas de rede, sem LLM, sem persistência. Consumidor downstream (Control Plane / reporter) recebe o JSON via stdin/file.
4. **`inference_signature` / audit chain:** placeholder no Sprint 0 (campo presente, valor `null`). Sprint 1 preenche com `model+snapshot+prompt_hash+fewshot_hash`. Sprint 2 entra no audit chain.
5. **Próxima decisão:** depois deste ADR aceito, backend-dev-dotnet abre `engine/` e implementa Layer Tagging + LNTY-001 contra fixtures do qa-engineer (Saint mínimo).

## 1. Árvore de diretórios

**Decisão: solution multi-projeto, um único binário publicado.**

Justificativa: separar `Engine.Core` (lógica analisável, testável sem `MSBuildWorkspace`) de `Engine.Cli` (entrypoint que monta a infra Roslyn) reduz superfície de teste e força a fronteira que vamos vender. `Engine.Tests` fica no mesmo solution para CI rápido. Single project não escala quando entrar `Engine.Llm` no Sprint 1.

```
lintty/
└── engine/
    ├── Lintty.Engine.sln
    ├── Directory.Build.props          # TargetFramework, LangVersion, Nullable, deterministic build
    ├── Directory.Packages.props       # Central Package Management (versões pinadas)
    ├── src/
    │   ├── Lintty.Engine.Core/
    │   │   ├── Lintty.Engine.Core.csproj
    │   │   ├── Workspace/
    │   │   │   ├── SolutionLoader.cs       # MSBuildLocator + MSBuildWorkspace + WorkspaceFailed vigilante (03-motor-roslyn §2)
    │   │   │   └── GeneratedCodeFilter.cs  # exclui IsGeneratedCode + obj/** + Generated/**
    │   │   ├── Tagging/
    │   │   │   ├── LayerTagger.cs          # convention + explicit; FAIL-FAST se não classifica (02-canon §Layer Tagging)
    │   │   │   └── LintttyConfig.cs        # parser de lintty.yml
    │   │   ├── Analyzers/
    │   │   │   ├── IAnalyzer.cs            # contrato comum (recebe Compilation + LayerMap, devolve IEnumerable<Violation>)
    │   │   │   ├── Lnty001_DomainLayerIsolation.cs
    │   │   │   ├── Lnty002_PersistenceContamination.cs
    │   │   │   ├── Lnty003_ForbiddenInstantiation.cs
    │   │   │   ├── Lnty006_UbiquitousLanguageLeak.cs
    │   │   │   ├── Lnty007_DependencyCycles.cs
    │   │   │   ├── Lnty008_PortsAtBoundaries.cs
    │   │   │   └── Lnty009_MethodExceedsAnalyzability.cs   # placeholder Sprint 0 (sem tokenizer)
    │   │   ├── Suppressions/
    │   │   │   └── LinttyIgnoreParser.cs   # extrai @lintty-ignore (03-motor §6)
    │   │   ├── Scoring/
    │   │   │   └── Scorer.cs               # fórmula do canon (02-canon §Cálculo de Score)
    │   │   ├── Output/
    │   │   │   ├── JsonReport.cs           # serializa Report → JSON v1.0 (03-motor §7)
    │   │   │   └── ReportSchema.cs         # POCOs do contrato
    │   │   └── Model/
    │   │       ├── Violation.cs
    │   │       ├── Layer.cs                # enum: Domain | Application | Infrastructure | Presentation | DomainAbstractions
    │   │       └── Severity.cs             # enum: Critical | High | Medium | Low
    │   └── Lintty.Engine.Cli/
    │       ├── Lintty.Engine.Cli.csproj
    │       └── Program.cs                  # System.CommandLine, exit codes
    └── tests/
        ├── Lintty.Engine.Tests/
        │   ├── Lintty.Engine.Tests.csproj  # xUnit + FluentAssertions
        │   └── Analyzers/                   # unit por regra
        └── Lintty.Engine.Golden/
            ├── Lintty.Engine.Golden.csproj # CI gate; roda fixtures e bate JSON contra expected.json
            └── fixtures/                    # symlink/path para repo lintty-golden-tests
```

### Target framework e configurações globais

- **Target:** `net8.0` (LTS até nov/2026; `docs/03-motor-cli.md` §2 lista .NET 6–9 como targets analisáveis, mas o motor em si compila em .NET 8 por estabilidade no piloto).
- `LangVersion=12`, `Nullable=enable`, `TreatWarningsAsErrors=true`, `Deterministic=true`, `ContinuousIntegrationBuild=true` (no CI).
- `<InvariantGlobalization>true</InvariantGlobalization>` no CLI (evita diff de cultura na serialização).

### Pacotes NuGet (Central Package Management em `Directory.Packages.props`)

Versões pinadas. Sem floats. Atualização vira PR.

| Pacote | Versão | Onde |
|---|---|---|
| `Microsoft.CodeAnalysis.CSharp` | `4.11.0` | `Engine.Core` |
| `Microsoft.CodeAnalysis.CSharp.Workspaces` | `4.11.0` | `Engine.Core` |
| `Microsoft.CodeAnalysis.Workspaces.MSBuild` | `4.11.0` | `Engine.Core` |
| `Microsoft.Build.Locator` | `1.7.8` | `Engine.Core` |
| `System.CommandLine` | `2.0.0-beta4.22272.1` | `Engine.Cli` (beta é o padrão de facto; aceitar até GA) |
| `YamlDotNet` | `16.1.3` | `Engine.Core` (parse de `lintty.yml`) |
| `xunit` | `2.9.0` | testes |
| `xunit.runner.visualstudio` | `2.8.2` | testes |
| `FluentAssertions` | `6.12.1` | testes |
| `Microsoft.NET.Test.Sdk` | `17.11.1` | testes |

> Não incluímos `System.Text.Json` — vem com .NET 8. Não incluímos tokenizer da Anthropic no Sprint 0 (LNTY-009 fica em modo placeholder).

## 2. CLI surface

Binário publicado: `lintty-engine` (`dotnet publish -c Release -r linux-x64 --self-contained` para a demo Linux; `win-x64` e `osx-arm64` opcionais).

```
lintty-engine analyze
    --solution <path>           (obrigatório) caminho para .sln
    --canon-version <ver>       (opcional, default lê de lintty.yml; sem yml e sem flag → "1.0.0")
    --config <path>             (opcional, default <solution-dir>/lintty.yml; ausente é OK na demo Sprint 0)
    --output <json|pretty>      (opcional, default "json")
    --output-file <path>        (opcional; se ausente, escreve em stdout)
    --fail-on-grade <A|B|C|D|F> (opcional, default "D" — exit 1 se grade pior ou igual a este)
```

Subcomandos futuros (não no Sprint 0, listados para reservar a forma):
- `lintty-engine version` — imprime versão do binário e canon embutido.
- `lintty-engine validate-config <path>` — só valida `lintty.yml` sem rodar análise.

### Exit code semantics

| Código | Significado |
|---|---|
| `0` | Análise rodou; grade ≥ `--fail-on-grade` invertido (ou seja, melhor que o gate). Default: A/B/C. |
| `1` | Análise rodou; grade pior que o gate. Default: D/F. |
| `2` | Erro de execução: solution não abre, build falhou, `LayerTaggingError`, `lintty.yml` inválido, IO. |
| `3` | Erro de uso: argumentos inválidos. |

> Hard lock atingido **não** muda o exit code por si só — ele força grade F via canon, e F dispara exit 1 pelo gate default. Isso mantém uma única política ("exit code segue grade").

### Stdout/stderr discipline

- `stdout` carrega **apenas** o JSON (ou pretty) do laudo. Logs vão em `stderr`. Permite `lintty-engine ... | jq`.
- `--output pretty` é console-friendly (cores, sumário) mas **não é parseável**; CI sempre usa `json`.

## 3. Contrato de saída JSON

Schema baseado em `docs/futuro/motor-roslyn-blueprint.md` §7 (versão original, preservada como referência), expandido com os campos exigidos pelo prompt (`grade`, `seal_eligible`, `hard_locks_hit`, `layer_summary`, `exceptions`, `workspace_diagnostics`, `inference_signature`). **Os campos do canon (`scan_id`, `metrics`, `ai_candidates`, `suppressions_parsed`, `sandbox_integrity`) são preservados** — o motor não pode emitir um superset divergente do contrato canônico, mesmo com placeholders `null` no V0.

### Top-level (anotado)

```jsonc
{
  "schema_version": "1.0",                    // canon §7 — forward-compat
  "run_id": "01HXYZ...",                      // ULID (sem timestamp humano embutido — só random; ver §5)
  "canon_version": "1.0.0",
  "rule_set_version": "1.0.0",                // == canon_version no MVP; campo separado para evoluir patches sem bumpar canon
  "solution_path": "MyCompany.sln",           // SEMPRE relativo à pasta passada em --solution; nunca absoluto. Após ADR 0006, conteúdo é polimórfico — ver nota abaixo.

  "score": 0,                                 // 0-100, arredondado a passos de 5 (canon §Cálculo)
  "grade": "F",                               // A | B | C | D | F
  "seal_eligible": false,                     // false se hard_locks_hit não-vazio OU supressões > 10% (canon §Travas)
  "hard_locks_hit": ["LNTY-001", "LNTY-002", "LNTY-007"],

  "layer_summary": {
    "Domain":         { "projects": 2, "files": 87,  "violations": 5 },
    "Application":    { "projects": 1, "files": 34,  "violations": 1 },
    "Infrastructure": { "projects": 3, "files": 112, "violations": 2 },
    "Presentation":   { "projects": 1, "files": 22,  "violations": 1 }
  },

  "violations": [ /* ver §3.1 */ ],
  "exceptions": [ /* @lintty-ignore válidos OU inválidos; ver §3.2 */ ],
  "workspace_diagnostics": [ /* ver §3.3 */ ],

  "inference_signature": null,                // Sprint 0 placeholder; preenchido em Sprint 1

  // Campos canônicos do 03-motor-roslyn §7 — mantidos para não divergir contrato:
  "compile_status": "success",
  "metrics": { /* total_sloc_physical, sloc_per_layer, projects_analyzed, ... */ },
  "ai_candidates": [],                        // Sprint 0 sempre vazio (sem LLM)
  "sandbox_integrity": { "source_destroyed_at": null, "egress_violations": [] }
                                              // Sprint 0: motor roda local, campos null/[]
}
```

> **Nota (após ADR 0006):** o conteúdo de `solution_path` é polimórfico — pode terminar em `.sln`, `.csproj`, ou `lintty.yml`, dependendo de como o usuário declarou o alvo da análise. O campo continua sendo string-livre dentro do schema JSON `1.0`, que permanece LOCKED. O Reporter deriva o modo (sln-driven vs csproj-driven vs yaml-driven) a partir do sufixo deste campo para renderizar o bloco "Escopo da análise" do PDF.

### 3.1 `violation` (objeto)

```jsonc
{
  "rule_id": "LNTY-002",
  "severity": "critical",                    // critical | high | medium | low (lower-case)
  "is_hard_lock": true,                      // copiado do canon, NÃO computado em runtime (princípio #7)
  "file": "MyCompany.Domain/OrderRepository.cs",   // relativo à raiz da solution
  "line": 23,
  "column": 17,
  "symbol_fqn": "MyCompany.Domain.OrderRepository.GetActiveSql",
  "evidence": {
    "code_snippet": "const string sql = \"SE\" + \"LECT * FROM Orders WHERE Active=1\";",
    "ast_kind": "BinaryExpressionSyntax",
    "additional_context": {
      "constant_value": "SELECT * FROM Orders WHERE Active=1",
      "matched_pattern": "^\\s*(SELECT|INSERT|UPDATE|DELETE|MERGE)\\s+.+(\\s+FROM\\s+|\\s+INTO\\s+)",
      "detection_pass": "constant_folding"
    }
  },
  "fingerprint": "sha256:..."                // canon §5 — sha256(rule_id || code_slice_normalized || canon_version)
}
```

`additional_context` é polimórfico por regra (LNTY-001 carrega `{referenced_assembly, classified_as}`; LNTY-007 carrega `{cycle_members[], cycle_kind}`; etc.). Documentação por regra fica nos comentários do `ReportSchema.cs`.

### 3.2 `exception` (supressão `@lintty-ignore`)

```jsonc
{
  "file": "MyCompany.Application/PlaceOrderHandler.cs",
  "line": 42,
  "rule_id": "LNTY-004",
  "justification": "Constraint do EF Core, validação não é regra de negócio (>30 chars).",
  "author_git_email": "dev@agency.com",
  "valid": true,                             // false se justification < 30 chars OU regra hard-lock OU regra desconhecida
  "invalid_reason": null                     // "too_short" | "hard_lock_unsuppressible" | "unknown_rule" | null
}
```

### 3.3 `workspace_diagnostic`

Falhas não-fatais do `MSBuildWorkspace` (`docs/03-motor-cli.md` §4: falhas fatais já abortam com exit 2; aqui ficam `Warning`):

```jsonc
{ "kind": "warning", "message": "Project X targets net48; analyzed in compatibility mode.", "project": "X.csproj" }
```

### 3.4 Exemplo Saint (0 violações)

```jsonc
{
  "schema_version": "1.0",
  "run_id": "01HXSAINT0000000000000000",
  "canon_version": "1.0.0",
  "rule_set_version": "1.0.0",
  "solution_path": "Saint.sln",
  "score": 100,
  "grade": "A",
  "seal_eligible": true,
  "hard_locks_hit": [],
  "layer_summary": {
    "Domain":         { "projects": 1, "files": 12, "violations": 0 },
    "Application":    { "projects": 1, "files": 8,  "violations": 0 },
    "Infrastructure": { "projects": 1, "files": 15, "violations": 0 },
    "Presentation":   { "projects": 1, "files": 5,  "violations": 0 }
  },
  "violations": [],
  "exceptions": [],
  "workspace_diagnostics": [],
  "inference_signature": null,
  "compile_status": "success",
  "metrics": {
    "total_sloc_physical": 1200,
    "sloc_per_layer": { "domain": 400, "application": 250, "infrastructure": 380, "presentation": 170 },
    "projects_analyzed": 4
  },
  "ai_candidates": [],
  "sandbox_integrity": { "source_destroyed_at": null, "egress_violations": [] }
}
```

### 3.5 Exemplo Sinner (9 violações, 3 hard locks)

Score: `100 - (3×25 + 2×10 + 3×4 + 1×1) = 100 - 108 = -8 → max(0, -8) = 0`. Round-to-5 = 0. Grade canônica: F (0–49). Hard locks → grade forçada a F e `seal_eligible=false`.

```jsonc
{
  "schema_version": "1.0",
  "run_id": "01HXSINNER000000000000000",
  "canon_version": "1.0.0",
  "rule_set_version": "1.0.0",
  "solution_path": "Sinner.sln",
  "score": 0,
  "grade": "F",
  "seal_eligible": false,
  "hard_locks_hit": ["LNTY-001", "LNTY-002", "LNTY-007"],
  "layer_summary": {
    "Domain":         { "projects": 1, "files": 6, "violations": 5 },
    "Application":    { "projects": 1, "files": 4, "violations": 1 },
    "Infrastructure": { "projects": 1, "files": 7, "violations": 2 },
    "Presentation":   { "projects": 1, "files": 3, "violations": 1 }
  },
  "violations": [
    // Ordenadas por (file, line, column, rule_id) — ver §5
    { "rule_id": "LNTY-001", "severity": "critical", "is_hard_lock": true,
      "file": "Sinner.Domain/Sinner.Domain.csproj", "line": 12, "column": 1,
      "symbol_fqn": "Sinner.Domain",
      "evidence": { "code_snippet": "<ProjectReference Include=\"..\\Sinner.Infrastructure\\Sinner.Infrastructure.csproj\" />",
                    "ast_kind": "ProjectReference",
                    "additional_context": { "referenced_assembly": "Sinner.Infrastructure", "classified_as": "infrastructure" } },
      "fingerprint": "sha256:aaa..." },
    { "rule_id": "LNTY-002", "severity": "critical", "is_hard_lock": true,
      "file": "Sinner.Domain/OrderRepo.cs", "line": 18, "column": 9,
      "symbol_fqn": "Sinner.Domain.OrderRepo",
      "evidence": { "code_snippet": "using Microsoft.EntityFrameworkCore;",
                    "ast_kind": "UsingDirectiveSyntax",
                    "additional_context": { "forbidden_namespace": "Microsoft.EntityFrameworkCore", "detection_pass": "type_aware" } },
      "fingerprint": "sha256:bbb..." },
    { "rule_id": "LNTY-002", "severity": "critical", "is_hard_lock": true,
      "file": "Sinner.Domain/OrderRepo.cs", "line": 23, "column": 17,
      "symbol_fqn": "Sinner.Domain.OrderRepo.GetActiveSql",
      "evidence": { "code_snippet": "const string sql = \"SE\" + \"LECT * FROM Orders WHERE Active=1\";",
                    "ast_kind": "BinaryExpressionSyntax",
                    "additional_context": { "constant_value": "SELECT * FROM Orders WHERE Active=1", "detection_pass": "constant_folding" } },
      "fingerprint": "sha256:ccc..." },
    { "rule_id": "LNTY-003", "severity": "medium", "is_hard_lock": false,
      "file": "Sinner.Application/PlaceOrder.cs", "line": 31, "column": 21,
      "symbol_fqn": "Sinner.Application.PlaceOrder.Handle",
      "evidence": { "code_snippet": "var smtp = new SmtpEmailSender();",
                    "ast_kind": "ObjectCreationExpressionSyntax",
                    "additional_context": { "instantiated_type": "Sinner.Infrastructure.SmtpEmailSender", "has_port_interface": true } },
      "fingerprint": "sha256:ddd..." },
    { "rule_id": "LNTY-006", "severity": "low", "is_hard_lock": false,
      "file": "Sinner.Domain/OrderManager.cs", "line": 7, "column": 18,
      "symbol_fqn": "Sinner.Domain.OrderManager",
      "evidence": { "code_snippet": "public class OrderManager",
                    "ast_kind": "ClassDeclarationSyntax",
                    "additional_context": { "matched_pattern": "(Manager|Helper|Util|Utils|Utility)$" } },
      "fingerprint": "sha256:eee..." },
    { "rule_id": "LNTY-007", "severity": "critical", "is_hard_lock": true,
      "file": "Sinner.Domain/Sinner.Domain.csproj", "line": 1, "column": 1,
      "symbol_fqn": "<project_cycle>",
      "evidence": { "code_snippet": "Sinner.Domain ↔ Sinner.Application",
                    "ast_kind": "ProjectGraphCycle",
                    "additional_context": { "cycle_kind": "project_cycle", "cycle_members": ["Sinner.Domain", "Sinner.Application"] } },
      "fingerprint": "sha256:fff..." },
    { "rule_id": "LNTY-008", "severity": "high", "is_hard_lock": false,
      "file": "Sinner.Infrastructure/SmtpEmailSender.cs", "line": 8, "column": 14,
      "symbol_fqn": "Sinner.Infrastructure.SmtpEmailSender",
      "evidence": { "code_snippet": "public class SmtpEmailSender",
                    "ast_kind": "ClassDeclarationSyntax",
                    "additional_context": { "missing_port": "ISmtpEmailSender", "expected_in": ["domain", "domain.abstractions"] } },
      "fingerprint": "sha256:ggg..." },
    { "rule_id": "LNTY-008", "severity": "high", "is_hard_lock": false,
      "file": "Sinner.Infrastructure/PdfPrinter.cs", "line": 5, "column": 14,
      "symbol_fqn": "Sinner.Infrastructure.PdfPrinter",
      "evidence": { "code_snippet": "public class PdfPrinter",
                    "ast_kind": "ClassDeclarationSyntax",
                    "additional_context": { "missing_port": "IPdfPrinter", "expected_in": ["domain", "domain.abstractions"] } },
      "fingerprint": "sha256:hhh..." },
    { "rule_id": "LNTY-009", "severity": "medium", "is_hard_lock": false,
      "file": "Sinner.Presentation/CheckoutController.cs", "line": 44, "column": 5,
      "symbol_fqn": "Sinner.Presentation.CheckoutController.Process",
      "evidence": { "code_snippet": "public async Task<IActionResult> Process(...) { /* 312 LoC */ }",
                    "ast_kind": "MethodDeclarationSyntax",
                    "additional_context": { "loc_count": 312, "token_count_estimate": 9200, "tokenizer": "placeholder_sprint0" } },
      "fingerprint": "sha256:iii..." }
  ],
  "exceptions": [],
  "workspace_diagnostics": [],
  "inference_signature": null,
  "compile_status": "success",
  "metrics": {
    "total_sloc_physical": 2100,
    "sloc_per_layer": { "domain": 600, "application": 400, "infrastructure": 800, "presentation": 300 },
    "projects_analyzed": 4
  },
  "ai_candidates": [],
  "sandbox_integrity": { "source_destroyed_at": null, "egress_violations": [] }
}
```

## 4. Fórmula de scoring (transcrita do canon)

**Decisão: usar a fórmula canônica de `docs/02-canon-v1.md` §Cálculo de Score.** O prompt sugeriu pesos diferentes (Critical -20, High -10, Medium -5, Low -2; bandas 90/80/70/60); essa proposta **diverge do canon v1.0 LOCKED** e seria reabertura indevida. Transcrevo o canônico abaixo.

### Pesos (Canon v1.0)

| Severidade | Peso |
|---|---|
| Crítica | 25 |
| Alta | 10 |
| Média | 4 |
| Baixa | 1 |

### Fórmula

```
score_raw          = 100 - Σ (peso[severidade] × count_violations_open)
score_arredondado  = round_to_nearest(score_raw, step=5)
score              = max(0, score_arredondado)
```

`count_violations_open` = total - supressões válidas (regra existe, não é hard lock, justificativa ≥30 chars).

### Conversão para Grade

| Score | Grade |
|---|---|
| 95–100 | A |
| 85–94 | B |
| 70–84 | C |
| 50–69 | D |
| 0–49 | F |

### Travas absolutas

- **Qualquer Crítica aberta** (= hard lock atingido ou Crítica não suprimida) → `grade = "F"` e `seal_eligible = false`, **independente** do score numérico.
- **Supressões válidas em Médias/Altas > 10%** do total Médias/Altas → `grade = "F"`, `seal_eligible = false`.

### Arredondamento

Round-half-to-even em passos de 5 (absorve flutuação LLM no Sprint 1+; no Sprint 0 sem LLM já é determinístico). Implementação: `Math.Round(raw / 5.0, MidpointRounding.ToEven) * 5`.

### Confirmar com canon owner antes de Sprint 0 fechar

> O prompt deste ADR sugeriu bandas A 90-100/B 80-89/C 70-79/D 60-69/F <60 e pesos -20/-10/-5/-2. Essas divergem do canon v1.0. Mantenho o canon. **Item de confirmação:** validar com o canon owner que as bandas/pesos do `02-canon-v1.md` permanecem válidas para o Sales Cut. Se sim, este ADR não muda. Se não, o canon é alterado primeiro (PR no `02-canon-v1.md`), depois este ADR é revisitado.

## 5. Determinismo bit-a-bit

Pontos onde precisa garantia explícita (testar em CI: `lintty-engine analyze` rodado N vezes sobre o mesmo input produz **byte-idêntico** stdout):

1. **Ordenação estável de violações:** chave composta `(file, line, column, rule_id, fingerprint)`. Implementação: `OrderBy(...).ThenBy(...).ThenBy(...).ThenBy(...).ThenBy(...)` — nunca `Sort()` sem comparer (instável em algumas plataformas) e nunca confiar em ordem de `Dictionary` ou `HashSet`.
2. **Ordenação estável de `exceptions`, `workspace_diagnostics`, `hard_locks_hit`, `layer_summary` keys:** mesma chave + ordem alfabética estável dos rule_ids (`LNTY-001` antes de `LNTY-002`).
3. **Sem timestamps no payload factual:** nada de `DateTime.UtcNow`, `Environment.TickCount`, `Stopwatch.GetTimestamp()` no JSON. `metrics.duration_ms` (que existe no canon §7) entra em campo separado e **não é serializado em modo determinístico** — ou é zerado em Sprint 0. (O canon `duration` é informativo; CI compara ignorando esse subobjeto.)
4. **Sem paths absolutos do host:** todos os `file` são `Path.GetRelativePath(solutionDir, absolutePath)` com separador normalizado para `/` (não `\`), independente da plataforma. Soluções abertas em `C:\Users\...\Sinner.sln` ou `/home/runner/Sinner.sln` produzem o mesmo JSON.
5. **Sem `Environment.MachineName`, `Environment.UserName`, `Process.Id`:** não aparecem em logs serializáveis.
6. **`run_id`:** ULID gerado a partir de `Random.Shared` é não-determinístico — **portanto, no Sprint 0, aceita-se `run_id` variável**, mas a Golden Suite compara ignorando esse campo (`expected.json` tem `"run_id": "<*>"` e o comparador faz match wildcarded). Alternativa: derivar `run_id` de `sha256(solution_path || canon_version)` para forçar determinismo total. **Decisão Sprint 0:** ULID + comparador wildcarded (mais simples; mantém run_id útil para debug em produção). Revisitar no Sprint 2.
7. **Fingerprints:** `sha256(rule_id || code_slice_normalized || canon_version)` (canon §5). `code_slice_normalized` = trim + line endings normalizados para `\n` + tabs → 4 espaços. Sem isso, Windows vs Linux diferem.
8. **Cultura:** `CultureInfo.InvariantCulture` em todo `ToString()`/`Parse()`. `<InvariantGlobalization>true</InvariantGlobalization>` no `.csproj` do CLI.
9. **JSON serialization:** `System.Text.Json` com `JsonSerializerOptions { WriteIndented = false, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }` e ordem de propriedades garantida via `[JsonPropertyOrder]` nas POCOs de `ReportSchema.cs`.
10. **Generated code filter aplicado uniformemente** (ver `GeneratedCodeFilter.cs`): se um arquivo é gerado em Linux mas não em Windows (por causa de wildcard de path com case), o JSON diverge. Caminhos comparados em lower-case invariant.

## 6. Decisões abertas (parking lot)

Listadas, **não decididas neste ADR**. Cada uma vira ADR próprio quando o sprint chegar.

| # | Decisão | Quando endereçar | Por que adiar |
|---|---|---|---|
| 1 | **Orquestrador backend (Python vs Go)** | Sprint 2 (infra) | Sales Cut roda CLI no laptop; orquestrador não bloqueia demo. `docs/12-sales-cut.md` §4.1. |
| 2 | **Invocação em Cloud Run (Job vs Service, scaling, 2nd-gen vs 1st-gen)** | V1+ (após sinal comercial) | Idem #1. `docs/futuro/motor-roslyn-blueprint.md` §1 lista recursos por tier mas não fixa job vs service. No V0, o Web Inspector roda numa única VM/serviço e isso é suficiente. |
| 3 | **Provedor de TSA (DigiCert / FreeTSA / outro RFC 3161)** | Sprint 3 (reporter assinado) | PDF mock no Sales Cut (§4.3 do sales cut). Decisão envolve due-diligence comercial. |
| 4 | **Formato exato do `inference_signature`** | Sprint 1 (LLM) | No Sprint 0 é placeholder `null`. Forma proposta no canon: `{model, snapshot_id, prompt_hash, fewshot_hash}` mas o hash exato (sha256? blake3?) e a ordem dos campos hashed precisam ser fixados quando integrarmos Anthropic. |
| 5 | **Tokenizer real para LNTY-009** | Sprint 1 | Sprint 0 usa estimativa por LoC × fator (placeholder). Tokenizer Anthropic chega com a integração da SDK. |
| 6 | **Frontend stack (Next.js? outro?)** | Pós-Sales Cut | Mockup Figma resolve a demo. `docs/12-sales-cut.md` §3.2. |
| 7 | **`run_id` determinístico vs ULID** | Sprint 2 | Decisão Sprint 0 documentada em §5.6; revisitar quando audit chain entrar. |

---

**Próxima decisão a tomar (após este ADR aceito):**
qa-engineer entrega `tests/Lintty.Engine.Golden/fixtures/saint-csharp/` com `Saint.sln` mínimo (4 projetos, 0 violações esperadas) + `expected.json` baseado no exemplo de §3.4. Em paralelo, backend-dev-dotnet faz scaffolding da árvore §1 e implementa `SolutionLoader` + `LayerTagger` + LNTY-001. Sem fixture, não tem regra (`docs/09-golden-tests.md` e agent definition do backend-dev-dotnet).
