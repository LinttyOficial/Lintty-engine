# ADR 0006 — Target Resolution: `.sln`, `.csproj`, e Escopo Declarado em `lintty.yml`

- **Status:** Proposed
- **Date:** 2026-05-04
- **Author:** software-architect
- **Audience:** backend-dev-dotnet (implementador no `engine/`), qa-engineer (fixture nova + gate cruzado), tech-writer-sales (mensagens de erro como argumento de venda), product-owner (ratificar)
- **Sprint:** V0 — semana corrente (`docs/15-roadmap-curto.md`); habilitador de adoção do CLI Self-Service e do Web Inspector em repos sem `.sln`.
- **Canon pinado:** `1.0.0` (`docs/02-canon-v1.md`) — **não muda**.
- **Pré-requisitos atendidos:** ADR 0001 (motor é CLI standalone com contrato JSON `1.0` LOCKED), ADR 0003 (PDF determinístico bit-a-bit), ADR 0005 (CLI Self-Service como default).
- **Substitui:** o pressuposto implícito no ADR 0001 §2 e em `docs/03-motor-cli.md` §3 de que "o motor sempre recebe um `.sln`". Não invalida ADRs 0001–0005; estende-os no eixo de **especificação de escopo de análise**.

## Revisão de design (estrutura canônica)

1. **Cabe no canon arquitetural?** Sim. O motor continua opinionado e fail-fast; o que muda é **como o usuário declara o escopo**. Nenhuma regra do Canon v1.0 é alterada e nenhum gate de determinismo é relaxado.
2. **Princípios afetados:**
   - **#1 (mesmo motor nos dois caminhos)** — preservado: o algoritmo de resolução de target roda dentro do motor, não no Web Inspector. O wrapper do Web Inspector só tem uma exceção minúscula (clone com exatamente 1 `.csproj`) que se traduz para a mesma chamada interna.
   - **#2 (determinismo bit-a-bit)** — preservado: a ordem de carregamento dos projetos passa a ser **ordem literal de declaração no YAML** (em vez da ordem do `.sln`). Continua sendo um total order estável dado o mesmo input.
   - **#7 (single source of truth: canon pinado)** — reforçado: `lintty.yml` ganha mais um campo (`projects:`), o canon não muda.
   - **#9 (schema JSON `1.0` LOCKED)** — preservado: nenhum campo novo é adicionado ao JSON do motor. `metrics.projects_analyzed` já carrega a contagem; o reporter só passa a renderizar texto explícito no PDF a partir desse campo existente.
3. **Fronteira:** algoritmo determinístico no `Lintty.Engine.Core/Workspace/SolutionLoader.cs` (ou em um novo `TargetResolver` chamado antes dele). Nada vaza para fora do motor — o Web Inspector continua invocando o CLI por subprocess com um único flag de entrada.
4. **V0 ou V1+?** V0. É bloqueador prático para rodar Web Inspector em repos GitHub reais (vários não têm `.sln`).
5. **Mudanças no contrato:**
   - **CLI:** flag `--solution` ganha alias deprecated `--target` apontando para o mesmo handler — ver §3.
   - **`lintty.yml`:** campo novo opcional `projects:` (lista). YAMLs antigos continuam válidos.
   - **JSON do motor:** **zero mudança**. `schema_version: "1.0"` permanece.
   - **Web Inspector:** dois `error_code` novos (`ambiguous_target`, `target_not_found`); `no_sln` é mantido por backcompat mas vira sub-caso de "no_target".
6. **Próxima decisão (após este ADR aceito):** backend-dev-dotnet implementa o `TargetResolver` e atualiza `LinttyConfig` + CLI + JobWorker em PR único, com fixture nova `the-saint-no-sln` entregue pelo qa-engineer em paralelo.

## 1. Contexto

`docs/03-motor-cli.md` §3 e ADR 0001 §2 cravaram `--solution <path>` como entrada obrigatória do motor. Implementação do `SolutionLoader` (em `engine/src/Lintty.Engine.Core/Workspace/SolutionLoader.cs`) parseia `.sln` ou usa fallback `AdhocWorkspace` reconstruindo o grafo a partir das `.csproj`. O Web Inspector (`docs/13-web-inspector.md` §4 e §12) procura "a primeira `*.sln` na raiz" do clone e falha com `error_code: no_sln` se não encontrar.

Em produção real (e em vários repos públicos relevantes para a demo do Web Inspector), encontramos:

- Repos .NET modernos que **não versionam `.sln`** — só `.csproj` espalhados sob `src/`. Visual Studio gera `.sln` localmente quando alguém abre, e o Git ignora.
- Repos com **múltiplas `.sln`** (ex: `Foo.sln`, `Foo.Tests.sln`, `Foo.AllPlatforms.sln`) — pegar "a primeira" é arbitrário e quebra determinismo cruzado entre máquinas com filesystem ordering distinto.
- Repos com **um único `.csproj` na raiz** — caso simples e comum para libs e exemplos.

Sem suporte a esses casos, o CLI Self-Service vira inviável para parcela significativa do mercado .NET, e o Web Inspector falha cedo numa demo pública.

A diretriz aprovada em conversa com o founder em 2026-05-03:

- **Quando há `.sln`** → fluxo atual intocado (compatibilidade retroativa total).
- **Quando não há `.sln`** → o usuário declara explicitamente os projetos em `lintty.yml` via campo novo `projects:`. **Sem auto-discovery por padrão** — Lintty é fail-fast e canônico, não advinha escopo.
- **Exceção do Web Inspector**: se o clone tem **exatamente um** `.csproj` e não tem `.sln` nem `lintty.yml`, aceita esse `.csproj` como alvo. Cobre o caso simples sem virar magic generalizado.

Esta ADR formaliza essa diretriz, fixa as decisões abertas, e serve de spec de implementação.

## 2. Decisão (resumo executivo)

1. **CLI surface** — `--solution` é renomeado para `--target` com `--solution` mantido como alias deprecated; aceita três formas (`.sln`, `.csproj`, ou caminho/diretório de `lintty.yml`). Detalhes em §3.
2. **`lintty.yml` schema** — campo novo opcional `projects:` (lista de paths relativos a `.csproj`). Sem glob, sem path absoluto, sem auto-discovery. Detalhes em §4.
3. **Algoritmo de resolução** — determinístico, fail-fast, prioriza flag explícito → `.sln` na raiz → `lintty.yml` com `projects:` → exceção 1-csproj exclusiva do Web Inspector → erro nomeado. Detalhes em §5.
4. **Carregamento Roslyn** — quando o alvo são `.csproj`s declarados, usa o caminho do `AdhocWorkspace` que o `SolutionLoader.ManualLoadAsync` já implementa, montando uma `Solution` virtual a partir da lista declarada. Detalhes em §6.
5. **Reporte no PDF** — bloco "Escopo da análise" no rodapé do executive summary, derivado de `metrics.projects_analyzed` (campo existente). Sem mudança no schema JSON. Detalhes em §7.
6. **Web Inspector** — dois `error_code` novos (`ambiguous_target`, `target_not_found`), `no_sln` permanece como caso particular de `no_target` para backcompat de URLs documentadas. Detalhes em §8.
7. **Fixtures e testes** — fixture nova `the-saint-no-sln` (clone do Saint sem `.sln`, com `lintty.yml` declarando os 4 projetos), gate cruzado novo "PDF do Saint == PDF do Saint-no-sln modulo `solution_path`". Detalhes em §9.

## 3. CLI surface — opção escolhida e por quê

### 3.1 Decisão: **Opção B (renomear para `--target`, manter `--solution` como alias deprecated por uma versão)**

**Por que:**
- O nome `--solution` mente quando o argumento é um `.csproj` ou um diretório de `lintty.yml` — perpetuar a mentira degrada a primeira impressão do produto. CLI nominado errado é dívida que cresce.
- Manter o alias `--solution` por uma versão (até `v0.2.0` mínimo, `v0.3.0` ideal) garante que o Web Inspector — que invoca o CLI via subprocess (`docs/13-web-inspector.md` §12) — não quebra durante a transição. A `EngineSubprocessRunner` pode migrar para `--target` em PR separado, sem coordenação cross-componente.
- Custo de migração para um usuário externo é zero (alias funciona); custo de adiar é "agora todo material de venda usa o nome errado".

**Mensagem de deprecação** (em stderr quando `--solution` é usado):

```
warning: --solution is deprecated; use --target. Will be removed in v0.3.0.
```

A linha vai para stderr (não polui o stdout JSON). Não é erro nem warning fatal.

### 3.2 Forma final do CLI

```
lintty-engine analyze
  --target <path>                   (obrigatório; aceita .sln, .csproj, ou diretório com lintty.yml)
  --solution <path>                 (deprecated alias para --target; warning em stderr)
  [--canon-version <ver>]
  [--config <path>]                 (opcional; força lintty.yml de outro caminho — ver §5.6)
  [--output json|pretty]            (default: json)
  [--output-file <path>]
  [--fail-on-grade A|B|C|D|F]       (default: D)
  [--pdf <path>]
```

Behavior do `--target`:

| Forma do path | Tratamento |
|---------------|------------|
| Termina em `.sln` | Carrega como solution (fluxo atual). |
| Termina em `.csproj` | Trata como lista de 1 projeto declarado. Não exige `lintty.yml`, mas se houver `lintty.yml` no mesmo diretório do `.csproj` ou em ancestral, **continua sendo usado** para layer tagging e supressões. |
| Diretório existente | Procura `lintty.yml` ali. Se achar, usa o `projects:` declarado. Se não achar, erro `no_target` apontando o diretório como cwd da resolução. |
| Caminho de arquivo nomeado `lintty.yml` | Usa esse YAML como fonte de `projects:`. Útil para CI passar config explícita. |
| Qualquer outra coisa | Erro `target_not_found`. |

### 3.3 Por que NÃO as outras opções

- **Opção A (`--solution` poliforme):** o nome continua errado. O atrito de explicar "passe o `.csproj` na flag chamada `--solution`" em material de venda é real. Vetado.
- **Opção C (`--project` separado, mantém `--solution` exclusivo de `.sln`):** explosão combinatória de flags. Já temos `--solution`, `--config`, `--output-file`, `--canon-version`, `--fail-on-grade`, `--pdf`. Adicionar `--project` cria a pergunta "qual eu uso quando?" — e mutually-exclusive flags são bug-magnet em System.CommandLine. Vetado.

### 3.4 Impacto em material de venda e docs

- `docs/03-motor-cli.md` §3 → atualizar a definição do CLI (tabela §3 deste ADR vira referência operacional).
- `docs/14-cli-distribution.md` (smoke commands) → trocar exemplo `--solution` por `--target`.
- `docs/12-sales-cut.md` §2 (talk-track) → "o cliente roda `lintty-engine analyze --target Foo.sln`" no lugar de `--solution`.
- README do repo público de release → idem.

## 4. `lintty.yml` schema — campo novo `projects:`

### 4.1 Decisão de forma

Campo novo opcional, no nível raiz do YAML (mesmo nível que `canon_version`, `layer_tagging`, `pure_packages`):

```yaml
canon_version: 1.0.0

projects:
  - src/MyCompany.Domain/MyCompany.Domain.csproj
  - src/MyCompany.Application/MyCompany.Application.csproj
  - src/MyCompany.Infrastructure/MyCompany.Infrastructure.csproj
  - src/MyCompany.Api/MyCompany.Api.csproj

layer_tagging:
  mode: convention
  # explicit_map continua keyed por assembly name, NÃO por path. Ver §4.4.
```

### 4.2 Regras de validação (todas fail-fast)

| Regra | Comportamento se violada | Motivação |
|-------|--------------------------|-----------|
| **Path absoluto é proibido.** | Erro `invalid_projects_entry` com mensagem `"projects[i] must be a relative path; got absolute: <path>"`. | Lintty.yml tem que ser portável entre máquinas e entre o CLI local e o Web Inspector. Path absoluto vaza filesystem do autor. |
| **Glob é proibido no V0.** | Erro `invalid_projects_entry` com mensagem `"globs are not supported in v1.0; declare each .csproj explicitly"`. | Ordem de expansão de glob varia entre filesystems (case-sensitive vs case-insensitive, ordenação literal vs locale-aware). Determinismo cruzado quebra. Reabrir em V1+ se houver demanda real e for viável especificar uma ordem canônica de expansão. |
| **Path tem que terminar em `.csproj`.** | Erro `invalid_projects_entry` com mensagem `"projects[i] must end in .csproj; got: <path>"`. | Não queremos suportar `.fsproj` ou `.vbproj` no V0 — só C#. |
| **Path tem que existir no momento da resolução.** | Erro `target_not_found` com mensagem `"projects[i] not found relative to lintty.yml: <path>"`. | Fail-fast; nada de "ignora silenciosamente entradas faltando". |
| **Path tem que estar contido no diretório de `lintty.yml` ou abaixo.** Não pode usar `..` para escapar. | Erro `invalid_projects_entry` com mensagem `"projects[i] must not escape the lintty.yml directory; got: <path>"`. | Defesa contra YAMLs maliciosos rodando no Web Inspector apontando para `/etc/...` ou similar. |
| **Lista vazia (`projects: []`) é igual a "não declarado".** | Cai no fluxo de descoberta normal (§5). Não é erro. | Preserva semântica: YAML que carrega só layer tagging continua válido. |
| **Caminho duplicado** | Erro `invalid_projects_entry` com mensagem `"projects[] contains duplicate: <path>"`. | Determinismo: ordem é a de declaração; duplicata gera ambiguidade na ordem efetiva. |

> O nome do erro `invalid_projects_entry` é interno (CLI termina com `exit 2` + mensagem em stderr). No Web Inspector vira `error_code: invalid_config`.

### 4.3 Ordem de carregamento

**Decisão:** ordem dos projetos = **ordem literal de declaração no YAML**.

Justificativa: preserva determinismo, é trivial de auditar (autor olha o YAML e sabe a ordem), e bate com a intuição "primeiro tem mais peso na renderização". `JsonReport` continua emitindo violações ordenadas por `(file, line, column, rule_id, fingerprint)` independente da ordem de carregamento — então a ordem de `projects:` afeta apenas:

- A ordem de iteração interna do motor sobre `Compilation`s (relevante para LNTY-007, que constrói grafo de project dependencies).
- A ordem do `metrics.sloc_per_layer` agregado (irrelevante para o output, que é por layer name).

Nenhuma dessas duas afeta o JSON ou o PDF observáveis quando os inputs são iguais.

### 4.4 Interação com `layers.explicit_map`

**Decisão:** `projects:` e `explicit_map` **não interagem**. São dimensões ortogonais:

- `projects:` decide **quais `.csproj` carregar**. Lista de paths.
- `explicit_map` decide **como classificar cada assembly**. Mapping `assembly_name → layer`.

`explicit_map` continua keyed por assembly name (não path), exatamente como hoje. A razão: dois `.csproj` em paths diferentes podem ter o mesmo `<AssemblyName>` em casos legítimos (multi-target sharing), e o que importa para o canon é o assembly resolvido pela compilation.

Exemplo combinado:

```yaml
canon_version: 1.0.0

projects:
  - src/MyCompany.Core/MyCompany.Core.csproj
  - src/MyCompany.Persistence/MyCompany.Persistence.csproj

layer_tagging:
  mode: explicit
  explicit_map:
    MyCompany.Core: domain
    MyCompany.Persistence: infrastructure
```

### 4.5 Compat com YAMLs antigos

YAMLs sem `projects:` continuam 100% válidos. Quando o resolver encontra `lintty.yml` sem o campo:

- Se houver `.sln` na raiz → usa `.sln` (fluxo atual; YAML serve só para layer tagging e supressões, como sempre serviu).
- Se não houver `.sln` → cai no caso "exceção do Web Inspector" (§5.4) ou no erro `no_target` (§5.5).

### 4.6 Atualização do parser

O `LinttyConfig.LoadOrDefault` em `engine/src/Lintty.Engine.Core/Tagging/LinttyConfig.cs` ganha um campo:

```csharp
public IReadOnlyList<string> Projects { get; init; } = Array.Empty<string>();
```

Parsing: ler `projects` como `YamlSequenceNode` no scope raiz (e, por compatibilidade com o nesting `lintty: { ... }` que o parser já suporta, também sob `lintty.projects`). Validações de §4.2 ficam num método separado `ValidateProjects(LinttyConfig, string yamlDir)` chamado pelo `TargetResolver`, não dentro do parser — separa "ler YAML" de "interpretar contra filesystem".

## 5. Algoritmo de resolução do target

### 5.1 Pseudocódigo (canônico)

Roda dentro do motor, em um novo `Lintty.Engine.Core/Workspace/TargetResolver.cs`, antes do `SolutionLoader`.

```text
ResolveTarget(targetArg, configArg, mode):
    # mode ∈ { Cli, WebInspector } — só muda a tolerância da exceção 1-csproj.

    # Passo 1 — flag explícita, se presente
    if targetArg is not null:
        if targetArg ends with ".sln":
            require File.Exists(targetArg) else fail target_not_found(targetArg)
            return SolutionTarget(targetArg)
        if targetArg ends with ".csproj":
            require File.Exists(targetArg) else fail target_not_found(targetArg)
            return DeclaredProjectsTarget([targetArg], yamlDir = nearest lintty.yml ancestor or null)
        if File.Exists(targetArg) and basename(targetArg) == "lintty.yml":
            return ResolveFromYaml(targetArg)
        if Directory.Exists(targetArg):
            return ResolveFromDir(targetArg, mode)
        fail target_not_found(targetArg)

    # Passo 2 — sem flag, parte do cwd
    return ResolveFromDir(cwd, mode)


ResolveFromDir(dir, mode):
    # 2a — .sln na raiz tem precedência (compat retroativa)
    slnFiles = Directory.EnumerateFiles(dir, "*.sln", SearchOption.TopDirectoryOnly)
                        .OrderBy(p, Ordinal)
                        .ToList()
    yamlPath = Path.Combine(dir, "lintty.yml") if File.Exists else null

    if slnFiles.Count >= 1:
        if slnFiles.Count > 1:
            fail ambiguous_target_multiple_slns(slnFiles)
        # exatamente uma .sln
        if yamlPath is not null and yaml has projects:
            fail ambiguous_target_sln_and_projects(slnFiles[0], yamlPath)
        return SolutionTarget(slnFiles[0])

    # 2b — sem .sln; tenta lintty.yml com projects:
    if yamlPath is not null:
        return ResolveFromYaml(yamlPath)

    # 2c — Web Inspector only: exceção do .csproj solitário
    if mode == WebInspector:
        csprojFiles = Directory.EnumerateFiles(dir, "*.csproj", SearchOption.AllDirectories)
                               .Where(p => !contains_obj_or_bin_segment(p))
                               .OrderBy(p, Ordinal)
                               .ToList()
        if csprojFiles.Count == 1:
            return DeclaredProjectsTarget([csprojFiles[0]], yamlDir = null)
        if csprojFiles.Count >= 2:
            fail ambiguous_target_multiple_csprojs(csprojFiles)

    fail no_target(dir)


ResolveFromYaml(yamlPath):
    config = LinttyConfig.LoadOrDefault(yamlPath)
    yamlDir = Path.GetDirectoryName(yamlPath)
    projects = ValidateProjects(config.Projects, yamlDir)  # §4.2
    if projects.Count == 0:
        fail no_target(yamlDir)
    return DeclaredProjectsTarget(projects, yamlDir = yamlDir)
```

### 5.2 Códigos de erro (canônicos)

Internos do motor (CLI termina com `exit 2`; Web Inspector mapeia em §8):

| Código | Significado | Mensagem stderr |
|--------|-------------|-----------------|
| `no_target` | Resolver não achou `.sln` nem `lintty.yml` com `projects:` no diretório alvo. | `"No target found in <dir>. Provide --target <path/to/.sln \| .csproj \| lintty.yml> or add a 'projects:' list to lintty.yml."` |
| `target_not_found` | Path explícito (via flag ou via `projects:` entry) não existe no filesystem. | `"Target not found: <path>"` |
| `ambiguous_target_multiple_slns` | Diretório tem ≥2 `.sln` e nenhum foi escolhido explicitamente. | `"Multiple .sln files found in <dir>: <list>. Pick one with --target."` |
| `ambiguous_target_multiple_csprojs` | (Web Inspector only) Diretório não tem `.sln` nem `lintty.yml`, e tem ≥2 `.csproj` espalhados. | `"Multiple .csproj files found and no .sln or lintty.yml. Add a lintty.yml with 'projects:' to declare scope."` |
| `ambiguous_target_sln_and_projects` | Coexistem `.sln` na raiz E `lintty.yml` com `projects:`. Ver §5.3. | `"Both <sln> and 'projects:' in <yaml> declare scope. Remove 'projects:' or analyze the .csproj list directly with --target <path/to/lintty.yml>."` |
| `invalid_projects_entry` | Entrada de `projects:` viola uma regra de §4.2. | (mensagem específica da regra violada) |

Todos esses são reportados em stderr e disparam `exit 2` no CLI.

### 5.3 Decisão: `.sln` + `lintty.yml` com `projects:` — qual ganha?

**Decisão: erra com `ambiguous_target_sln_and_projects`.**

Avaliei duas alternativas:

- **Alternativa "sln ganha (silencioso)":** preserva 100% retrocompat e nunca quebra repos existentes. Desvantagem: usuário que adicionou `projects:` esperando que pegasse efeito vai ficar confuso quando o `.sln` antigo ainda é usado. Bug subtle, fere o princípio fail-fast que vendemos.
- **Alternativa "projects: ganha":** desfaz silenciosamente a precedência histórica. Pior — repos pré-existentes podem ter um `lintty.yml` com `projects:` antigo de um experimento e o comportamento muda do dia para a noite no upgrade do CLI.

Erro explícito é o caminho fail-fast canônico do Lintty. Mensagem aponta como resolver (apague um dos dois, ou explicite via `--target`). Em mundos onde "erro vs default silencioso" é dúvida, este produto sempre opta pelo erro.

### 5.4 Exceção do Web Inspector — exatamente 1 `.csproj`

A diretriz aprovada permite ao Web Inspector aceitar **um único `.csproj`** quando:

1. Não há `.sln` na raiz do clone.
2. Não há `lintty.yml` na raiz do clone (ou tem `lintty.yml` mas sem `projects:`).
3. Há **exatamente um** `.csproj` no clone (recursivamente, excluindo `obj/` e `bin/`).

Cobrir esse caso:

- Maximiza demos públicas funcionando (libs simples, samples, mini-apps).
- Mantém a regra fail-fast em todos os outros casos: zero `.csproj` → `no_target`; ≥2 `.csproj` → `ambiguous_target_multiple_csprojs` com mensagem instruindo o cliente a criar `lintty.yml`.

**Importante:** essa exceção é **exclusiva do Web Inspector** (`mode == WebInspector` no resolver). O CLI local **não** aceita 1-csproj implícito sem flag — usuário no laptop tem que apontar `--target Foo.csproj` ou `--target .` (com `lintty.yml`). Razões:

- Cliente local que digita `lintty-engine analyze` num diretório aleatório não está fazendo demo; tá esperando rodar contra escopo definido. "Achei um `.csproj`" parece magia agradável e vira bug-magnet ("rodou contra projeto errado e não percebi").
- Web Inspector recebe uma URL — implica intenção de "analise esse repo", então a tolerância na descoberta é justificável.

### 5.5 Casos de erro tabelados (CLI + Web Inspector)

| Cenário | Resolução | Exit / `error_code` |
|---------|-----------|---------------------|
| `--target Foo.sln` existe | Usa `.sln`. | OK |
| `--target Foo.csproj` existe | Carrega 1-projeto declarado. | OK |
| `--target .` (cwd com `Foo.sln`) | Usa `Foo.sln`. | OK |
| `--target .` (cwd sem `.sln`, com `lintty.yml` válido com `projects:`) | Carrega projetos declarados. | OK |
| `--target .` (cwd sem nada) | `no_target` | exit 2 / `error_code: no_target` |
| Sem flag, cwd com 2 `.sln` | `ambiguous_target_multiple_slns` | exit 2 / `error_code: ambiguous_target` |
| Sem flag, cwd com `.sln` + `lintty.yml` com `projects:` | `ambiguous_target_sln_and_projects` | exit 2 / `error_code: ambiguous_target` |
| Sem flag, cwd sem `.sln`, sem `lintty.yml`, 1 `.csproj`, modo WebInspector | Carrega o `.csproj` único. | OK |
| Sem flag, cwd sem `.sln`, sem `lintty.yml`, 1 `.csproj`, modo CLI | `no_target` (com mensagem sugerindo `--target Foo.csproj`). | exit 2 / N/A |
| Sem flag, cwd sem `.sln`, sem `lintty.yml`, 3 `.csproj`, modo WebInspector | `ambiguous_target_multiple_csprojs` | exit 2 / `error_code: ambiguous_target` |
| `lintty.yml` com `projects: [src/Foo.csproj]` mas Foo.csproj não existe | `target_not_found` | exit 2 / `error_code: target_not_found` |
| `lintty.yml` com `projects: [/etc/passwd]` | `invalid_projects_entry` (path absoluto) | exit 2 / `error_code: invalid_config` |
| `lintty.yml` com `projects: [src/**/*.csproj]` | `invalid_projects_entry` (glob) | exit 2 / `error_code: invalid_config` |

### 5.6 `--config` separado de `--target`

Caso de uso real: CI gera o YAML em lugar diferente do código (por exemplo, monorepo onde várias `lintty.yml` coexistem por subprojeto). O CLI já tem `--config <path>` documentado em ADR 0001 §2 mas não implementado em `Program.cs`. Este ADR confirma que `--config` é **complementar** a `--target`:

- `--target` define o **escopo** (o que carregar).
- `--config` define **sob qual configuração** (que canon, que layer tagging, que supressões).

Quando `--config` é omitido, o resolver procura `lintty.yml` na ordem: (a) diretório do `--target`, (b) ancestrais até a raiz, (c) `Default`.

Implementação de `--config` fica fora do escopo deste ADR (continua opcional/no-op se for omitido), mas a forma é confirmada para não criar conflito futuro.

## 6. Carregamento Roslyn quando o alvo são `.csproj`s

### 6.1 Decisão de implementação

Reuso do caminho que o `SolutionLoader.ManualLoadAsync` já implementa em `engine/src/Lintty.Engine.Core/Workspace/SolutionLoader.cs`. Esse método:

- Constrói uma `Solution` virtual via `AdhocWorkspace`.
- Adiciona cada `.csproj` como `ProjectInfo` com seus `.cs` files como `DocumentInfo`.
- Resolve `<ProjectReference>` por path do csproj.
- Retorna o `LoadResult` no mesmo formato que o caminho `.sln` (Solution, Projects+Compilations, Warnings, ProjectReferences).

A diferença é a fonte da lista de `(name, csprojAbs)`:

- **Hoje:** parse do `.sln` (regex sobre as linhas `Project(...) = "Name", "relPath", "{guid}"`).
- **Novo:** lista declarada em `lintty.yml.projects` (ou `[targetCsproj]` quando o flag é um único `.csproj`, ou `[soleCsprojFound]` quando é a exceção do Web Inspector).

Refatoração proposta:

1. Extrair de `ManualLoadAsync` um método privado `BuildSolutionFromProjects(List<(string Name, string CsprojAbs)> raw, string solutionPathForReporting)` que faz os Pass 2/3 atuais.
2. `LoadFromSolutionAsync(slnPath)` (renomear o caminho atual) chama o pass 1 (regex sobre `.sln`) e delega para `BuildSolutionFromProjects`.
3. `LoadFromProjectListAsync(IReadOnlyList<string> csprojAbsList, string solutionPathForReporting)` (novo) é a entrada quando o target é declarado.

`solutionPathForReporting` é o que vai parar em `report.solution_path` (campo do JSON). Decisões:

- **Modo `.sln`:** valor atual (caminho relativo ao cwd, do arquivo `.sln`).
- **Modo `--target Foo.csproj`:** caminho relativo do `.csproj`. O JSON ganha `solution_path: "src/Foo.csproj"`.
- **Modo `lintty.yml` com `projects:`:** caminho relativo do `lintty.yml`. O JSON ganha `solution_path: "lintty.yml"` (apontando o YAML como source-of-truth do escopo).

> **Importante:** o nome do campo no JSON continua `solution_path` (não muda para `target_path`), porque mexer em campo do schema bumpa para `1.1`. O conteúdo do campo passa a ser polimórfico (`*.sln`, `*.csproj`, ou `lintty.yml`), o que está dentro do contrato de string-livre do campo. Essa decisão é explicitada no PR + na atualização de `docs/adr/0001-motor-skeleton.md` §3 (parágrafo de nota, não rewrite).

### 6.2 `WorkspaceFailed` e fail-fast

Mantido. Política de `docs/03-motor-cli.md` §4 (qualquer falha de carga aborta) continua válida. O `LoadFromProjectListAsync` herda o mesmo tratamento.

### 6.3 Ordem de iteração

Garantida pela ordem da lista de entrada:

- Modo `.sln`: ordem alfabética (Ordinal) do nome do projeto, como hoje em `solution.Projects.OrderBy(p => p.Name, StringComparer.Ordinal)` (linha 79 do `SolutionLoader.cs`).
- Modo `lintty.yml` com `projects:`: **ordem literal de declaração no YAML** (§4.3). O loader não reordena.
- Modo 1-csproj (CLI ou exceção Web Inspector): lista de 1, ordem trivial.

Para o JSON e o PDF observáveis, isso é invariante (ver §4.3). Para o `metrics.sloc_per_layer` interno: agregação por layer name — também invariante.

### 6.4 LNTY-007 (ciclos) em escopo declarado

LNTY-007 detecta SCC sobre o grafo de project references. Quando o escopo é declarado:

- O grafo é o subgrafo induzido pelos `.csproj` declarados.
- `<ProjectReference>` que aponta para um `.csproj` **fora** do escopo declarado é **silenciosamente ignorado** na construção do grafo (não é violação, não é warning) — porque o usuário definiu o escopo, e regras só rodam dentro do escopo definido.

Consequência: ciclos que envolvem projetos não-declarados **não** são detectados. Isso precisa ser **explícito no PDF** (§7) para o leitor entender que a cobertura de LNTY-007 é parametrizada pelo escopo. Não é regressão de regra; é honestidade sobre o que o motor sabe.

## 7. Reporte no PDF (Reporter)

### 7.1 Decisão: bloco "Escopo da análise" no executive summary

Adicionar um bloco curto de duas linhas no executive summary do PDF (`engine/src/Lintty.Engine.Reporter/Layout/ExecutiveSummary.cs`), logo abaixo das contagens de violações e antes da lista de exceptions:

```
Escopo da análise
4 projetos declarados via lintty.yml.
Detecção de ciclos (LNTY-007) limitada ao subgrafo declarado.
```

Variantes condicionadas pelo modo de target:

| Modo | Texto fixo (em PT-BR, hardcoded em C#) |
|------|----------------------------------------|
| `.sln` | "N projetos carregados via Foo.sln." |
| 1-csproj (CLI ou Web Inspector) | "1 projeto: Foo.csproj." |
| `projects:` em `lintty.yml` | "N projetos declarados via lintty.yml. Detecção de ciclos (LNTY-007) limitada ao subgrafo declarado." |

A segunda linha sobre LNTY-007 só aparece nos modos não-`.sln` (onde o subgrafo declarado pode ser parcial). Nos modos `.sln` o grafo é todo o `.sln`, então a anotação é redundante.

### 7.2 Determinismo

- Texto **hardcoded em C#** — sem timestamp, sem locale-dependent formatting.
- Número de projetos lido de `report.metrics.projects_analyzed` (campo já presente em `ReportSchema.cs` desde Sprint 0 — confirmado no schema do ADR 0001 §3).
- Nome do `.sln` lido de `report.solution_path` (campo existente; já preenchido com path relativo).
- Para o modo `projects:`, o reporter precisa saber **que foi modo `projects:`**, não só o `solution_path`. **Decisão:** olhar o sufixo de `solution_path` (`.sln` → modo solution; `.csproj` → modo 1-csproj; `lintty.yml` → modo declarado). Sem campo novo no JSON, sem bump de schema.

### 7.3 Confirmação: schema JSON `1.0` permanece LOCKED

Verifiquei `ReportSchema.cs` (via referência no ADR 0001 §3.4) e o exemplo Saint:

```jsonc
"metrics": {
  "total_sloc_physical": 1200,
  "sloc_per_layer": { "domain": 400, "application": 250, "infrastructure": 380, "presentation": 170 },
  "projects_analyzed": 4
}
```

`metrics.projects_analyzed` está disponível. **Nenhum campo novo é necessário.** Schema fica `1.0`.

### 7.4 Gate de determinismo

`tests/Lintty.Engine.Reporter.Tests/DeterminismTests.cs` precisa ganhar um caso novo para o modo `projects:` (ver §9.2). O gate atual (Saint/Sinner/Ninja em modo `.sln`) continua válido sem mudança.

## 8. Códigos de erro do Web Inspector (`JobWorker`)

### 8.1 Mapeamento canônico

`engine/src/Lintty.WebInspector/Jobs/JobStatus.cs` ganha duas constantes novas em `JobErrorCode`:

```csharp
public const string AmbiguousTarget = "ambiguous_target";
public const string TargetNotFound = "target_not_found";
public const string InvalidConfig = "invalid_config";
```

`NoSln` é **mantido** mas redocumentado como "caso particular legado de `no_target`" em comentário Doxygen. URLs que cliente já tem com `error_code: no_sln` continuam significando a mesma coisa (zero `.sln` no clone, zero `lintty.yml`, modo `WebInspector`, mais que 1 ou 0 `.csproj` no clone). Em V1+ (próximo bump major do contrato HTTP) podemos remover `NoSln`; até lá, fica.

**Decisão de mapeamento (`exit 2` do motor → `JobWorker`):**

| Mensagem stderr do motor (parse) | `JobErrorCode` |
|----------------------------------|----------------|
| `no_target: ...` | `NoTarget` (novo, alias de `NoSln` em comentário; ver §8.2) |
| `target_not_found: ...` | `TargetNotFound` |
| `ambiguous_target_*: ...` | `AmbiguousTarget` |
| `invalid_projects_entry: ...` | `InvalidConfig` |
| `LayerTaggingError: ...` | `LayerTaggingError` (existente) |
| Falhas de compilação | `CompileFailed` (existente) |
| Timeout do worker | `Timeout` (existente) |
| Outros | `InternalError` (existente) |

### 8.2 Retroatividade do `NoSln`

**Decisão:** adicionar `NoTarget = "no_target"` como constante nova; **manter `NoSln = "no_sln"` como alias** que o `JobWorker` continua emitindo quando o erro é especificamente "0 `.sln` no clone" (mantém URLs legadas estáveis).

`no_sln` só é emitido no modo Web Inspector quando o motor erra com `no_target` E o JobWorker confirmou anteriormente via filesystem que o clone realmente não tinha `.sln` (caso legado dos primeiros pilotos). Para todos os outros `no_target` (ex: clone tem `.sln` mas o `lintty.yml` aponta para `projects:` inexistentes), emite `no_target`.

Em V1 do contrato HTTP, remove `no_sln` e fica só `no_target`. Documentar no `docs/13-web-inspector.md` §5 dentro deste sprint.

### 8.3 Mensagens user-facing (no front-end)

`docs/13-web-inspector.md` §7 ganha entradas:

| Erro | UX no front-end |
|------|-----------------|
| `no_target` (no_sln legado) | "Não encontramos `.sln`, `.csproj` único, ou `lintty.yml` com `projects:` na raiz. [Ver como configurar →]" |
| `ambiguous_target` | "Encontramos múltiplas `.sln` ou `.csproj` no repo. Adicione um `lintty.yml` com `projects:` na raiz para declarar o escopo. [Ver exemplo →]" |
| `target_not_found` | "Um caminho declarado em `projects:` não existe no repo: `<path>`. Verifique o `lintty.yml`." |
| `invalid_config` | "`lintty.yml` inválido: `<motivo>`. [Ver schema →]" |

A regra de `docs/13-web-inspector.md` §7 ("toda mensagem de erro aponta para o fallback do CLI local") continua válida — todas as mensagens acima podem fechar com "Ou rode o CLI localmente: `lintty-engine analyze --target <path>`".

## 9. Impacto em fixtures e testes

### 9.1 Fixtures atuais

**Sem alteração.** `fixtures/the-saint/`, `the-sinner/`, `the-ninja-01/` continuam com `.sln` e `expected.json` intactos. O fluxo `.sln` é caminho hot path — todos os testes existentes continuam servindo de regressão.

### 9.2 Fixture nova: `the-saint-no-sln`

**Decisão: adicionar.**

Estrutura:

```
fixtures/the-saint-no-sln/
├── lintty.yml                                    # com projects: explícito
├── expected.json                                 # bate-a-bate com expected.json do Saint exceto solution_path
├── src/
│   ├── Saint.Domain/        Saint.Domain.csproj  + .cs files (idênticos ao the-saint)
│   ├── Saint.Application/   Saint.Application.csproj
│   ├── Saint.Infrastructure/ Saint.Infrastructure.csproj
│   └── Saint.Api/            Saint.Api.csproj
# (sem Saint.sln)
```

`lintty.yml`:

```yaml
canon_version: 1.0.0
projects:
  - src/Saint.Domain/Saint.Domain.csproj
  - src/Saint.Application/Saint.Application.csproj
  - src/Saint.Infrastructure/Saint.Infrastructure.csproj
  - src/Saint.Api/Saint.Api.csproj
```

`expected.json` é idêntico ao `the-saint/expected.json` exceto:

- `solution_path`: `"lintty.yml"` em vez de `"Saint.sln"`.
- Tudo o mais (score, grade, layer_summary, violations vazia) **igual byte-a-byte**.

### 9.3 Testes novos no `Lintty.Engine.Core.Tests` / `Lintty.Engine.Reporter.Tests`

| Teste | Onde | O que verifica |
|-------|------|-----------------|
| `TargetResolverTests.Sln_Path_Wins_When_Both_Present` | Core.Tests | Resolver com `.sln` na raiz + `lintty.yml` com `projects:` → erra `ambiguous_target_sln_and_projects`. |
| `TargetResolverTests.Empty_Dir_Yields_NoTarget` | Core.Tests | Diretório vazio → `no_target`. |
| `TargetResolverTests.Multiple_Slns_Yields_AmbiguousTarget` | Core.Tests | 2 `.sln` na raiz → `ambiguous_target_multiple_slns`. |
| `TargetResolverTests.Single_Csproj_WebInspector_Mode_Resolves` | Core.Tests | Diretório com 1 `.csproj` em modo WebInspector → resolve. Em modo CLI → `no_target`. |
| `TargetResolverTests.Two_Csprojs_WebInspector_Mode_Yields_Ambiguous` | Core.Tests | Diretório com 2 `.csproj` em modo WebInspector → `ambiguous_target_multiple_csprojs`. |
| `TargetResolverTests.Projects_With_Glob_Yields_InvalidConfig` | Core.Tests | `lintty.yml` com `projects: [src/**/*.csproj]` → `invalid_projects_entry`. |
| `TargetResolverTests.Projects_With_Absolute_Path_Yields_InvalidConfig` | Core.Tests | Idem, path absoluto. |
| `TargetResolverTests.Projects_With_Path_Escape_Yields_InvalidConfig` | Core.Tests | Idem, `..` que escapa o diretório do YAML. |
| `TargetResolverTests.Missing_Project_File_Yields_TargetNotFound` | Core.Tests | `projects:` declara `.csproj` que não existe → `target_not_found`. |
| `SaintNoSlnFixtureTests.Resolves_Like_Saint` | Core.Tests | Roda motor contra `the-saint-no-sln` → JSON idêntico ao do Saint exceto `solution_path`. |
| `SaintNoSlnDeterminismTest` | Reporter.Tests | Modo `lintty.yml`-driven: 2 runs produzem PDF byte-idêntico. |
| `SaintNoSlnPdfHasScopeBlock` | Reporter.Tests | PDF do Saint-no-sln contém o texto do bloco "Escopo da análise" (§7.1). |
| `WorkerIntegrationTests.NoSln_NoYaml_Empty_Repo_Yields_NoTarget` | WebInspector.Tests | Worker contra clone vazio → `error_code: no_target` (ou `no_sln` legado). |
| `WorkerIntegrationTests.SingleCsproj_NoSln_Resolves` | WebInspector.Tests | Worker contra clone com 1 `.csproj` → completed. |
| `WorkerIntegrationTests.MultipleCsprojs_NoSln_Yields_AmbiguousTarget` | WebInspector.Tests | Worker contra clone com 2 `.csproj` sem yml → `error_code: ambiguous_target`. |

### 9.4 Gate de determinismo cruzado

`WorkerIntegrationTests` em `engine/tests/Lintty.WebInspector.Tests/` já implementa o gate "PDF do CLI == PDF do worker" para Saint e Sinner. Adicionar análogo para `the-saint-no-sln`:

- Rodar o CLI direto: `lintty-engine analyze --target fixtures/the-saint-no-sln/lintty.yml --pdf out1.pdf`.
- Rodar o JobWorker via `FixtureCopyGitClient` apontando para `fixtures/the-saint-no-sln/`.
- Comparar `sha256(out1.pdf) == sha256(out2.pdf)`.

Bate, ou bug de boot do .NET (locale, font, MSBuild) que precisa ser caçado antes do merge.

## 10. Conflitos detectados com docs existentes

Lista o que precisa ser editado em PR de seguimento (não escopo deste ADR):

| Arquivo | Conflito | Mudança necessária |
|---------|----------|--------------------|
| `docs/03-motor-cli.md` §3 | Define `--solution` como obrigatório. | Substituir por `--target`, com nota de retrocompat de `--solution` por uma versão. |
| `docs/03-motor-cli.md` §6 | Texto "Cada `.csproj` precisa ser classificado" não menciona o resolver. | Adicionar parágrafo que aponta para o resolver: "Antes do layer tagging, o motor resolve o escopo (qual `.sln`/`.csproj`/`lintty.yml` carregar) — ver ADR 0006." |
| `docs/13-web-inspector.md` §3 (passo 2 do worker) | "Localizar a primeira `*.sln` na raiz" é o algoritmo antigo. | Trocar por "Resolver target via `TargetResolver` (ADR 0006): `.sln` na raiz → `lintty.yml.projects` → 1-csproj exceção". |
| `docs/13-web-inspector.md` §5 | Lista de `error_code` não tem `ambiguous_target`, `target_not_found`, `invalid_config`. | Atualizar lista. |
| `docs/13-web-inspector.md` §7 | Tabela de erros user-facing referencia "no `.sln`" só. | Adicionar entradas conforme §8.3 deste ADR. |
| `docs/13-web-inspector.md` §10 (critério "pronto") | Item "[ ] PDF gerado pelo Web Inspector é byte-idêntico ao PDF do CLI local" só cobre `.sln`. | Adicionar item específico para o caso `lintty.yml`-driven (Saint-no-sln). |
| `docs/02-canon-v1.md` §`lintty.yml` | Schema de exemplo não menciona `projects:`. | Adicionar `projects:` como campo opcional, com link para este ADR. |
| `docs/adr/0001-motor-skeleton.md` §3 | `solution_path` documentado como "SEMPRE relativo à pasta passada em --solution". | Adicionar nota: "Quando o target é `lintty.yml`-driven, `solution_path` carrega o caminho relativo do `lintty.yml`. Quando é 1-csproj, carrega o caminho relativo do `.csproj`. Schema continua `1.0` — campo é polimórfico em string-livre." |
| `engine/src/Lintty.WebInspector/Jobs/JobStatus.cs` | Falta `AmbiguousTarget`, `TargetNotFound`, `NoTarget`, `InvalidConfig` em `JobErrorCode`. | Adicionar. |
| `engine/src/Lintty.Engine.Cli/Program.cs` | `solutionOption` está hard-coded como `--solution`. | Renomear para `targetOption` (`--target`); adicionar alias deprecated. |

**Não conflito** com canon (`docs/02-canon-v1.md`): nenhuma regra LNTY-* muda comportamento. Apenas o escopo onde elas rodam é parametrizado pelo `projects:` quando declarado, e LNTY-007 ganha menção explícita no PDF (§7.1) sobre limite de cobertura.

**Não conflito** com ADR 0003 (PDF determinístico): bloco "Escopo da análise" é texto hardcoded derivado de campo JSON existente. Determinismo gate continua válido.

**Não conflito** com ADR 0005 (CLI Self-Service como default): o argumento de venda "código nunca sai do equipamento" fica reforçado — usuário pode rodar contra escopo arbitrário (`.sln`, `.csproj`, monorepo via `projects:`) sem mandar nada para o Lintty.

## 11. Alternativas consideradas (e por que não)

### 11.1 Auto-discovery por padrão (descobrir todos os `.csproj` recursivamente)

**Tentação:** facilita demos públicas, "just works" em mais repos.

**Vetado por três motivos:**

1. **Quebra fail-fast.** O Lintty é vendido como "opinionado e auditável". Heurística de descoberta automática vira "Lintty descobriu 17 projetos, mas eu queria que rodasse só nos 4 do milestone, e não tem como saber qual ele pegou na hora de gerar o laudo".
2. **Quebra determinismo cruzado.** Ordem de `EnumerateFiles` pode variar entre filesystems (case-sensitivity, locale ordering, network FS). PDFs do CLI local e do Web Inspector divergem para o mesmo repo.
3. **Esconde monorepos misturados.** Repo com 4 microserviços, cada um com sua "domain" e "infra", se carregado como uma única `Solution` virtual gera detecção de ciclos cruzados que são falso-positivo. O `lintty.yml.projects:` força o usuário a declarar o escopo de cada análise (ex: 4 análises separadas, uma por microserviço), o que casa com a tese arquitetural do produto.

### 11.2 Glob no `projects:`

**Tentação:** menos verboso (`src/**/*.csproj`).

**Vetado por:**

- Ordem de expansão não-determinística entre filesystems.
- Mesmo problema de monorepo da §11.1.
- Reabrível em V1+ se demanda real aparecer e for acompanhado de uma especificação de ordem canônica de expansão (ex: "ordem alfabética Ordinal sobre o path absoluto normalizado, antes de `.ToList()`"). Out of scope para V0.

### 11.3 Inferir escopo a partir do `dotnet sln list` ou `dotnet build`

**Tentação:** delegar a descoberta para a toolchain oficial.

**Vetado:** adiciona dependência de `dotnet` no PATH com versão específica, abre porta para `MSBuild` rodando coisas (targets customizados, `<Exec>`, etc.), e ainda assim não resolve o caso "não tem `.sln`".

### 11.4 Aceitar `.csproj` em flag mas só `.sln` em `lintty.yml`

**Tentação:** simplifica o YAML schema.

**Vetado:** o caso de uso real do `projects:` é justamente "não tem `.sln`". Se o YAML não suporta lista de `.csproj`, o resolver vira inconsistente: CLI aceita 1-csproj direto, mas para 2+ csprojs sem `.sln` o usuário não tem como declarar. Forçaria ele a criar `.sln` artificial — pior dos mundos.

## 12. Plano de implementação

### 12.1 Engenharia (`backend-dev-dotnet`, ~2-3 dias)

1. **`TargetResolver`** novo em `engine/src/Lintty.Engine.Core/Workspace/TargetResolver.cs`. Implementa o pseudocódigo de §5.1. Modo (`Cli` vs `WebInspector`) passado como parâmetro.
2. **`LinttyConfig.Projects`** — campo novo + parser + validação (§4).
3. **Refator do `SolutionLoader`** — extrair `BuildSolutionFromProjects`, adicionar `LoadFromProjectListAsync` (§6.1).
4. **CLI** — `--target` (novo), `--solution` deprecated alias (§3.2). Wire-up para o resolver. Mensagens de erro (§5.2).
5. **`JobWorker`** — substituir o "find first `.sln`" pelo `TargetResolver(mode: WebInspector)`. Mapear códigos de erro (§8.1).
6. **`JobStatus.JobErrorCode`** — constantes novas (§8.1).

### 12.2 Fixtures (`qa-engineer`, ~0.5-1 dia)

7. **`fixtures/the-saint-no-sln/`** — copy do Saint sem `.sln`, com `lintty.yml.projects:` (§9.2). `expected.json` idêntico módulo `solution_path`.

### 12.3 Testes (`qa-engineer` + `backend-dev-dotnet`, ~1 dia)

8. **`TargetResolverTests`** — 9 testes em §9.3.
9. **`SaintNoSlnFixtureTests`** + **`SaintNoSlnDeterminismTest`** + **`SaintNoSlnPdfHasScopeBlock`** — 3 testes em §9.3.
10. **`WorkerIntegrationTests`** — 3 testes novos em §9.3.
11. **Gate de determinismo cruzado** estendido para Saint-no-sln (§9.4).

### 12.4 Docs (sequência, fora do PR de código)

12. PR separado com edits de §10. Aplicar depois que o código mergear (evita drift se o ADR ainda mudar em revisão).

### 12.5 Sequência sugerida

1. Founder ratifica este ADR (status Proposed → Accepted).
2. `qa-engineer` entrega `the-saint-no-sln/` (fixture só, sem testes ainda) — destrava `backend-dev-dotnet`.
3. `backend-dev-dotnet` PR único: `TargetResolver` + `LinttyConfig.Projects` + CLI + `JobWorker` + todos os testes de §9.3 + bloco "Escopo da análise" no PdfReporter.
4. CI verde inclui gate de determinismo cruzado novo.
5. PR de docs (§10) abre depois do merge do PR de código.

## 13. Métricas de sucesso

A decisão é considerada validada quando:

- **CLI rodando contra `the-saint-no-sln/lintty.yml`** produz PDF score A com bloco "Escopo da análise" mencionando `lintty.yml`.
- **Web Inspector rodando contra `https://github.com/lintty-demo/the-saint-no-sln`** produz o mesmo PDF byte-idêntico do CLI local (gate cruzado).
- **Web Inspector rodando contra um repo público real** que não tem `.sln` mas tem 1 `.csproj` (ex: lib simples) → completa com sucesso, devolve PDF.
- **Web Inspector rodando contra um repo público real** com 2+ `.csproj` sem `.sln` → falha cedo com `error_code: ambiguous_target` e mensagem clara apontando para `lintty.yml.projects:`.
- **Mensagem de erro `no_target` aparece em pelo menos um screenshot** da landing/docs como "Lintty é fail-fast: ele te diz exatamente o que está faltando para ele rodar" — vira argumento de venda.

A decisão é considerada errada se:

- **3+ usuários sucessivos** abrem issue dizendo "por que tenho que listar todos os `.csproj` se eu poderia ter um glob?" — sinal de que `projects: glob` precisa entrar em V0.5 (não V0).
- **Determinismo cruzado quebra** entre CLI local e Web Inspector na fixture Saint-no-sln — bug de carga via `AdhocWorkspace` que precisa ser caçado antes de qualquer release.
- **Renaming `--solution` → `--target`** quebra integração existente que não estava mapeada (pouco provável dado que o consumidor único hoje é o `EngineSubprocessRunner` interno, mas vale checar).

## 14. Decisões deixadas para depois

- **Glob em `projects:`** (V1+): só com especificação de ordem canônica de expansão.
- **Auto-discovery puro** (V2+, talvez nunca): só com mecanismo paralelo de "Lintty te mostra o que descobriu antes de rodar e você confirma" (UX modal). Hoje é vetado por ferir fail-fast.
- **Suporte a `.fsproj` / `.vbproj`** (V1+): exige analyzers correspondentes; canon em si é language-agnostic mas regras Roslyn estão em `Microsoft.CodeAnalysis.CSharp`.
- **Workspaces estilo Cargo / Nx** (V2+): repo com `lintty.workspace.yml` declarando múltiplos escopos analisáveis, cada um com sua `projects:` independente. Útil para monorepo grande. Out of scope para V0.
- **Remoção do alias deprecated `--solution`** (v0.3.0): após uma versão de transição.
- **Remoção do `error_code: no_sln`** (v1 do contrato HTTP): após `no_target` consolidar.

---

**Próxima decisão (após este ADR aceito):** `qa-engineer` clona `fixtures/the-saint/` em `fixtures/the-saint-no-sln/` removendo `Saint.sln` e adicionando `lintty.yml` com `projects:` conforme §9.2. Em paralelo, `backend-dev-dotnet` abre PR único implementando §12.1 + testes de §12.3 contra essa fixture. PR de docs (§10) abre depois do merge.
