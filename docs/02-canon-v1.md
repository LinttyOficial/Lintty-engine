# 02 — Canon v1.0 (LOCKED)

O Canon é o conjunto de regras arquiteturais que o Lintty aplica. Cada Projeto pina uma versão imutável do Canon no momento de sua criação. Atualizações de versão são opt-in com aviso explícito.

## Resumo

> **Nota Sales Cut (pivô 2026-04-27):** o Sales Cut roda **Zero-IA**. As regras LNTY-004 e LNTY-005 (Motor=LLM) ficam **OFF por default no Sales Cut** — coluna "Sales Cut" abaixo. Voltam a ser ON quando reativarmos a camada LLM em Production MVP, sob ZDR contratualizado.

| Código | Nome | Severidade | Hard Lock | Motor | Default Canon | Sales Cut |
|--------|------|-----------|-----------|-------|---------------|-----------|
| LNTY-001 | Domain Layer Isolation | Crítica | ✅ | Roslyn | ON | ON |
| LNTY-002 | Persistence Contamination | Crítica | ✅ | Roslyn | ON | ON |
| LNTY-003 | Forbidden Instantiation | Média | — | Roslyn | ON | ON |
| LNTY-004 | Business Logic in Repository | Alta | — | LLM | ON | **OFF (V1+)** |
| LNTY-005 | Anemic Domain | Média | — | LLM | OFF (opt-in) | **OFF (V1+)** |
| LNTY-006 | Ubiquitous Language Leak | Baixa | — | Roslyn (blacklist) | ON | ON |
| LNTY-007 | Dependency Cycles | Crítica | ✅ | Roslyn | ON | ON |
| LNTY-008 | Ports at Boundaries | Alta | — | Roslyn | ON | ON |
| LNTY-009 | Method Exceeds Analyzability | Média | — | Roslyn | ON | ON |

**Total:** 9 regras (6 Roslyn determinísticas + 2 LLM semânticas + 1 Roslyn de salvaguarda).

**Estatística:** 3 hard locks Críticas (não-suprimíveis), 6 ignoráveis com justificativa.

**Sales Cut ativo:** 7 regras determinísticas Roslyn (LNTY-001/002/003/006/007/008/009). Saint passa em A. Sinner cai em F com 3 hard locks. Ninja-01 detecta SQL via constant folding. **Zero alucinação, zero custo de inferência, determinismo bit-a-bit** — argumentos centrais de venda.

---

## LNTY-001 — Domain Layer Isolation (Crítica, Hard Lock)

**Princípio:** O assembly classificado como Domain não pode depender de assemblies classificados como Infrastructure ou Presentation. Apenas dependências para si mesmo, contratos puros (`Domain.Abstractions`), `System.*` e pacotes NuGet declarados como "puros" são permitidas.

**Detecção (camada dupla):**
1. **Project-level:** análise de `<ProjectReference>` em `.csproj`. Se Domain referencia Infrastructure → violação.
2. **Symbol-level:** visitor sobre `IdentifierNameSyntax` em arquivos do Domain. Para cada uso: `SemanticModel.GetSymbolInfo(node).Symbol.ContainingAssembly`. Se assembly resolvido está classificado como Infrastructure → violação.

A camada dupla é necessária porque um NuGet package interno pode "re-exportar" tipos de Infrastructure, fazendo o check project-level passar enquanto o domínio é poluído de fato.

**Whitelist (não viola):**
- `<self>` (mesmo assembly)
- Assemblies marcados como `domain.abstractions` no layer tagging
- `System.*`
- NuGets declarados em `lintty.yml → canon.pure_packages` (ex: `MediatR.Contracts`)

**Severidade:** Crítica. **Hard Lock:** Sim. Selo não é emitido se houver qualquer violação aberta.

---

## LNTY-002 — Persistence Contamination (Crítica, Hard Lock)

**Princípio:** SQL e operações de persistência não podem aparecer em código classificado como Domain. É o "pecado original" de DDD.

**Detecção (duas passagens):**

1. **Type-aware:** `SymbolFinder.FindReferencesAsync` para tipos dos namespaces:
   - `Microsoft.EntityFrameworkCore.*`
   - `MongoDB.Driver.*`
   - `Dapper.*`
   - `System.Data.*`
   - `Npgsql.*`
   - `Microsoft.Data.SqlClient.*`

2. **String literal SQL:** visitor sobre `LiteralExpressionSyntax` (string) **e** `BinaryExpressionSyntax(+)` cujos operandos resolvem para constante string via `SemanticModel.GetConstantValue`. Regex aplicado ao valor resolvido:
   ```regex
   ^\s*(SELECT|INSERT|UPDATE|DELETE|MERGE)\s+.+\s+FROM\s+
   ```
   (variantes para `INSERT INTO` sem FROM)

A constant folding via `GetConstantValue` é crítica: pega construções como `const string verb = "SE" + "LECT";` que regex puro de literal não detecta.

**Severidade:** Crítica. **Hard Lock:** Sim. Não-suprimível por `@lintty-ignore`.

---

## LNTY-003 — Forbidden Instantiation (Média)

**Princípio:** Em Application e Presentation, é proibido `new` de tipos que deveriam ser injetados via DI (typically Infrastructure adapters).

**Detecção:** visitor sobre `ObjectCreationExpressionSyntax`. Para cada nó:
- `SemanticModel.GetTypeInfo(node.Type)` resolve o tipo concreto
- Violação se: (a) tipo está classificado como Infrastructure, **OU** (b) existe interface `I<TypeName>` no Domain (sinaliza que o autor original esperava abstração)

**Exceções (não viola):**
- Records (`record`)
- Structs e value types (`DateTime`, `Guid`, `TimeSpan`)
- DTOs
- Tipos em `System.*`
- Exceções (`new Exception(...)`)

**Severidade:** Média. **Suprimível** com justificativa.

---

## LNTY-004 — Business Logic in Repository (Alta, LLM)

> **STATUS: V1+ ROADMAP — DESATIVADO NO SALES CUT.** Esta regra exige LLM ("advogado de defesa") com ZDR contratualizado. Reativada na fase Production MVP, após sinal de "go" comercial. Especificação preservada para esse momento.

**Princípio:** Repositórios devem ser coleções-como-abstração: apenas mecânica de persistência. Cálculos, validações de negócio, e branching baseado em regras de domínio pertencem à camada Domain ou Application.

**Pré-filtro Roslyn:** métodos em tipos com nome `*Repository` ou que implementam `IRepository<T>`, satisfazendo qualquer um destes:
- Complexidade ciclomática > 3
- Operadores de comparação (`>`, `<`, `>=`, `<=`) fora de contextos LINQ
- Chamadas para métodos fora de namespaces de persistência ou `System.*`

**Inferência LLM:** snippet candidato é enviado para Claude Sonnet 4.6 com prompt que descreve a regra e fatos do contexto (ver `04-llm-ops.md` para detalhes do prompt).

**Auto-consistência:** se confidence < 0.85 E verdict = VIOLATION, dispara segunda chamada (Prompt B "advogado de defesa"). Outcomes:
- Concordam VIOLATION → registra
- Concordam NO_VIOLATION → ignora
- Discordam → INCONCLUSIVE (não conta para score)

**Severidade:** Alta. **Suprimível** com justificativa.

---

## LNTY-005 — Anemic Domain (Média, LLM, OPT-IN)

> **STATUS: V1+ ROADMAP — DESATIVADO NO SALES CUT.** Mesma justificativa de LNTY-004 (LLM-required). Reativada em Production MVP.

**Princípio:** Em projetos que adotam DDD rico, entidades de domínio devem encapsular comportamento, não ser apenas containers de getters/setters (Anemic Domain Model é antipadrão).

**Default: OFF.** Esta regra **não está ativa por default**. O contratante deve ligar explicitamente o toggle quando o contrato exige DDD rico. A razão: muitos times legitimamente usam modelos anêmicos (CQRS com read models, Value Objects imutáveis).

**Pré-filtro Roslyn:** tipos em camada Domain com >N propriedades públicas e ≤1 método não-trivial (excluindo getters/setters/constructors/`ToString`/`Equals`/`GetHashCode`).

**Inferência LLM:** ver `04-llm-ops.md`. O prompt deve considerar contra-argumentos como "este é um Value Object imutável" ou "este é um read model de CQRS".

**Severidade:** Média. **Suprimível** com justificativa.

---

## LNTY-006 — Ubiquitous Language Leak (Baixa, Determinística)

**Princípio:** Nomes de classes, métodos e namespaces em Domain devem refletir a linguagem do negócio, não termos genéricos técnicos.

**Detecção (blacklist regex, sem LLM):** identifiers em Domain que batem com:
- `^(Data|Info|Temp|Stuff|Thing|Misc|Generic)\d*$`
- `(Manager|Helper|Util|Utils|Utility)$`

**Decisão deliberada:** o MVP NÃO usa LLM para esta regra. A análise semântica completa de "este nome é um termo do negócio?" exige um glossário do bounded context fornecido pelo contratante. Isso entra em **v1.1**. No MVP, a blacklist resolve 80% dos casos óbvios com 0 custo de token e 100% determinismo.

**Severidade:** Baixa. **Suprimível** com justificativa.

---

## LNTY-007 — Dependency Cycles (Crítica, Hard Lock)

**Princípio:** Não pode haver ciclos de dependência entre projetos ou entre bounded contexts. Ciclos quebram qualquer arquitetura em camadas, independente do estilo.

**Detecção (duas passagens distintas):**

1. **Grafo de Projects:** nó = `.csproj`, aresta = `<ProjectReference>`. Algoritmo de Tarjan para encontrar Componentes Fortemente Conectados (SCCs). Qualquer SCC com >1 nó é ciclo.

2. **Grafo de Bounded Contexts:** nó = namespace raiz detectado (configurável via `lintty.yml`), aresta = qualquer referência cruzada de símbolos. Mesma análise SCC.

O laudo reporta os dois tipos de ciclo separadamente. Ciclos entre arquivos no mesmo projeto não contam (são normais em C#).

**Severidade:** Crítica. **Hard Lock:** Sim.

---

## LNTY-008 — Ports at Boundaries (Alta)

**Princípio:** Toda classe pública em Infrastructure que é consumida fora de seu próprio assembly deve implementar uma interface (port) declarada em Domain ou em uma camada de abstrações. É a regra fundamental da Arquitetura Hexagonal.

**Detecção:** para cada tipo `public` em Infrastructure:
- `SymbolFinder.FindReferencesAsync` busca referências a esse tipo
- Se existe pelo menos uma referência cruzando assembly **E** não existe interface `I<TypeName>` declarada em Domain (ou Domain.Abstractions) → violação

**Heurística no MVP:** "interface equivalente" = match por nome (`IPaymentService` para `PaymentService`). V1.1 sofistica para match por public surface (assinaturas de métodos públicos).

**Severidade:** Alta. **Suprimível** com justificativa.

---

## LNTY-009 — Method Exceeds Analyzability (Média)

**Princípio:** Métodos cuja representação para análise semântica excede o budget de tokens da LLM (8.000 tokens) não podem ser auditados em conformidade. Por construção, isso indica violação severa do Single Responsibility Principle.

**Detecção:** após semantic slicing, se o slice excede 8K tokens, o motor:
1. **Não envia** o slice para a LLM (zero token desperdiçado)
2. Emite violação LNTY-009 com evidência: `token_count`, `loc_count`, `method_signature`

**Mensagem padrão da violação:** "Code block exceeds cognitive cyclomatic boundaries for semantic analysis. Refactor required."

**Severidade:** Média. **Suprimível** apenas com justificativa forte (raros casos legítimos como geradores de state machine).

---

## Layer Tagging — como o motor sabe qual camada é qual

Todas as regras dependem de **classificação confiável** dos `.csproj` em camadas. O Lintty oferece dois modos:

### Modo `convention` (default)

Patterns por nome de assembly:
```yaml
convention_map:
  domain:         ["*.Domain", "*.Core"]
  application:    ["*.Application", "*.UseCases"]
  infrastructure: ["*.Infrastructure", "*.Persistence", "*.Data"]
  presentation:   ["*.Api", "*.Web", "*.Controllers"]
```

### Modo `explicit` (override)

Mapping projeto-a-projeto, com precedência sobre convention:
```yaml
explicit_map:
  "MyCompany.Orders.Core.csproj":    "domain"
  "MyCompany.Orders.Storage.csproj": "infrastructure"
```

### Fail-fast

**Não há heurística de fallback.** Se o modo for `convention` e ≥1 `.csproj` não casa com nenhum pattern (e não tem entrada em `explicit_map`), o scan **falha com `LayerTaggingError`**, **não consome crédito**, e exibe mensagem clara: "Não foi possível classificar N projeto(s). Configure `lintty.yml`."

Esta decisão protege a integridade do laudo: nada de adivinhar.

---

## `lintty.yml` — schema completo

Arquivo no root do repositório do contratante. Fonte da verdade. Dashboard é UI sobre este arquivo.

```yaml
lintty:
  canon_version: "1.0.0"           # Imutável após criação do projeto
  project_id: "proj_abc123"

  projects:                        # Opcional. Lista explícita de .csproj quando o repo não tem .sln. Ver ADR 0006.
    - src/MyCompany.Domain/MyCompany.Domain.csproj
    - src/MyCompany.Application/MyCompany.Application.csproj
    - src/MyCompany.Infrastructure/MyCompany.Infrastructure.csproj
    - src/MyCompany.Api/MyCompany.Api.csproj

  layer_tagging:
    mode: "convention"             # convention | explicit | both
    convention_map:
      domain:         ["*.Domain", "*.Core"]
      application:    ["*.Application", "*.UseCases"]
      infrastructure: ["*.Infrastructure", "*.Persistence", "*.Data"]
      presentation:   ["*.Api", "*.Web", "*.Controllers"]
    explicit_map:
      "MyCompany.Orders.Core.csproj":    "domain"
      "MyCompany.Orders.Storage.csproj": "infrastructure"

  pure_packages:                   # NuGets que Domain pode referenciar
    - "MediatR.Contracts"
    - "FluentValidation"

  rules:
    LNTY-001: { enabled: true,  locked: true }
    LNTY-002: { enabled: true,  locked: true }
    LNTY-003: { enabled: true }
    LNTY-004: { enabled: true,  llm_min_confidence: 0.85 }
    LNTY-005: { enabled: false, disabled_reason: "Projeto usa CQRS com read models anêmicos" }
    LNTY-006: { enabled: true }
    LNTY-007: { enabled: true,  locked: true }
    LNTY-008: { enabled: true }
    LNTY-009: { enabled: true }
```

Campo `projects:` (opcional, especificado em [ADR 0006](adr/0006-target-resolution.md)): lista explícita de `.csproj` quando o repositório não tem `.sln`. Paths são relativos ao `lintty.yml`, sem glob, sem absoluto, sem `..` que escape o diretório. Ordem de declaração = ordem de carregamento. Quando o campo é omitido, o motor usa o `.sln` da raiz (comportamento histórico).

---

## Governança via GitOps

Mudanças em toggles ou layer tagging seguem fluxo:

1. Contratante edita no painel web do Lintty.
2. Ao "Salvar", o backend abre **PR automático** no repo-alvo:
   - Título: `chore(lintty): update Canon configuration`
   - Descrição: diff das regras + justificativa obrigatória (>30 chars) do que foi mudado
   - Autor: GitHub App do Lintty; co-autor: usuário que editou
3. Merge do PR = configuração ativa. **Git é a fonte da verdade.**
4. A agência vê a mudança no histórico do repo — elimina o argumento "mudaram as regras sem avisar".
5. Mudança dispara invalidação do cache de inferência LLM da configuração anterior.

---

## Cálculo de Score

### Fórmula

```
score_raw = 100 - Σ (peso[severidade] × count_violations_open)
score_arredondado = round_to_nearest(score_raw, step=5)
score = max(0, score_arredondado)
```

### Pesos (Canon v1.0)

| Severidade | Peso |
|-----------|------|
| Crítica | 25 |
| Alta | 10 |
| Média | 4 |
| Baixa | 1 |

### Conversão para Grade

| Score | Grade |
|-------|-------|
| 95–100 | A |
| 85–94 | B |
| 70–84 | C |
| 50–69 | D |
| 0–49 | F |

### Travas absolutas

- **Qualquer Crítica aberta:** Grade = F (selo não emitido), independente do score numérico.
- **Supressões > 10% de Médias/Altas:** Grade = F automático.

### Arredondamento

Score arredondado em passos de 5 absorve flutuações marginais entre execuções (especialmente do componente LLM). Diferença de 1-2 pontos entre scans do mesmo código = mesma grade. É proteção contra litigância marginal.

---

## Versionamento do Canon

### Pinning imutável

Cada Projeto, ao ser criado, **pina uma versão semver do Canon** (ex: `1.0.0`). Toda análise daquele projeto usa essa versão exata. **Não há atualização automática.**

### Upgrade opt-in

Se uma nova versão do Canon é publicada (ex: `1.1.0` adiciona novas regras), o contratante pode optar por atualizar o projeto. O dashboard exibe aviso:

> "Atenção: Mudar a versão do Canon de v1.0.0 para v1.1.0 adiciona 3 novas regras. Isso pode impactar o Score atual da agência. Confirme apenas se a agência foi notificada."

A versão do Canon é impressa no PDF: `Auditado sob Lintty Canon v1.0.0`.

### Compatibilidade entre versões

Versões do Canon são **imutáveis**. Uma vez publicado, `v1.0.0` nunca muda. Correções vão para `v1.0.1` (patch), novas regras vão para `v1.1.0` (minor), mudanças que quebram interpretação vão para `v2.0.0` (major).

---

## Suppression rules — como funcionam na prática

### Sintaxe

```csharp
// @lintty-ignore: LNTY-004 reason="Validação é constraint do EF Core, não regra de negócio"
public async Task<Order> Save(Order o) { ... }
```

### Validação

Para cada `@lintty-ignore` encontrada:

1. Regra existe no Canon pinado? Não → ignorada (justificativa não relevante).
2. Regra é Hard Lock (LNTY-001/002/007)? Sim → supressão **inválida**, violação mantida.
3. Justificativa tem ≥30 caracteres? Não → supressão **inválida**.
4. Caso contrário, supressão **válida** — violação descontada.

### Cap de impunidade

Após contagem de supressões válidas em um scan:

```
total_med_high = count_violations(severity in [media, alta])
total_suppressions_med_high = count_valid_suppressions(severity in [media, alta])

if total_suppressions_med_high > 0.10 * total_med_high:
    grade = "F"
    reason = "suppression_cap_exceeded"
```

### Sumário de Exceções no PDF

Toda supressão válida é listada na primeira página do PDF:
- Arquivo + linha
- Regra suprimida
- Justificativa literal
- Email do autor git do commit que adicionou a supressão
- Data

Isso elimina o argumento "ninguém viu que estávamos suprimindo" e dá ao contratante visibilidade total para tomar decisão informada.
