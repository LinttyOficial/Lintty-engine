# 09 — Golden Test Suite

A Golden Test Suite é o **escudo legal e técnico** do Lintty. Quando uma agência contestar um laudo, é o que prova consistência, repetibilidade e correção do motor.

**Mandatária no MVP.** PR ao motor que quebre qualquer caso da suite **não merge**. Sem exceção.

---

## 1. Estrutura do repositório

```
lintty-golden-tests/
├── README.md
├── runner/                    (CLI que executa a suite e compara)
│   ├── run.sh
│   └── compare.cs
├── fixtures/
│   ├── the-saint/
│   │   ├── solution/          (código C# perfeito)
│   │   │   ├── MyCompany.Saint.sln
│   │   │   ├── MyCompany.Saint.Domain/
│   │   │   ├── MyCompany.Saint.Application/
│   │   │   ├── MyCompany.Saint.Infrastructure/
│   │   │   └── MyCompany.Saint.Api/
│   │   ├── lintty.yml
│   │   └── expected.json      (output esperado do motor)
│   ├── the-sinner/
│   │   ├── solution/          (código com 9 violações óbvias)
│   │   ├── lintty.yml
│   │   └── expected.json
│   └── the-ninja-01-constant-folded-sql/
│       ├── solution/
│       ├── lintty.yml
│       └── expected.json
└── .github/workflows/
    └── ci.yml                 (gate em CI do motor)
```

### Convenção `expected.json`

Versão simplificada do output do motor com apenas os campos verificáveis (timestamps e UUIDs são strippados):

```json
{
  "compile_status": "success",
  "score_grade": "F",
  "violations": [
    {
      "rule_id": "LNTY-001",
      "severity": "critical",
      "target": "MyCompany.Sinner.Domain",
      "evidence_kind": "forbidden_project_reference"
    }
  ],
  "ai_candidates_count": 2,
  "suppressions_parsed_count": 0,
  "suppressions_valid_count": 0
}
```

O runner compara cada campo via diff estruturado. Discrepância em qualquer campo = falha.

---

## 2. The Saint — código perfeito

### Objetivo

Provar que o motor **não inventa violações** em código bem desenhado. Um falso positivo aqui é fatal: se a Saint cair para B, todas as agências do mundo vão abrir disputa.

### Conteúdo

Solution Hexagonal de exemplo, com 4 projetos:
- `MyCompany.Saint.Domain` — entidades, value objects, ports (`IOrderRepository`)
- `MyCompany.Saint.Application` — use cases, handlers que usam os ports
- `MyCompany.Saint.Infrastructure` — adapters (`OrderRepository : IOrderRepository`), DbContext, EF migrations
- `MyCompany.Saint.Api` — controllers, DI registration

Características:
- Domain depende **apenas** de System.* e ele mesmo
- Infrastructure depende de Domain (para implementar ports) e EF Core
- Sem ciclos
- Naming dentro do domínio (Order, Customer, Payment) — sem Manager/Helper/Util
- Repositórios contêm **apenas** persistência: `_db.Add`, `_db.SaveChangesAsync`, queries LINQ legítimas
- Entidades de Domain têm comportamento (`Order.AddItem(item)`, `Order.ApplyDiscount(percentage)`)

### `expected.json`

```json
{
  "compile_status": "success",
  "score_raw": 100,
  "score_grade": "A",
  "violations": [],
  "ai_candidates_count": 1,  // 1 método de Repository com complexidade > 3
  "suppressions_parsed_count": 0
}
```

O `ai_candidates_count: 1` mostra que o motor identificou um candidato (e a LLM deve marcar como NO_VIOLATION com confidence ≥0.85).

### Critério de sucesso

Score = **A**. Zero violações registradas. Se a LLM tiver disponível na pipeline de teste, NO_VIOLATION com alta confidence.

---

## 3. The Sinner — todas as 9 violações

### Objetivo

Provar que o motor **detecta o óbvio**. Se uma regra falha aqui, o motor está quebrado para aquela regra.

### Conteúdo (uma violação por regra)

| Arquivo | Violação | Regra |
|---------|----------|-------|
| `Domain/MyCompany.Sinner.Domain.csproj` | ProjectReference para Infrastructure | LNTY-001 |
| `Domain/Services/OrderService.cs` | `using Microsoft.EntityFrameworkCore;` + uso de DbContext | LNTY-002 |
| `Application/Handlers/PlaceOrderHandler.cs` | `new OrderRepository()` em vez de inject via interface | LNTY-003 |
| `Infrastructure/Repositories/OrderRepository.cs` | Método `Save` que aplica desconto antes de persistir | LNTY-004 (LLM) |
| `Domain/Entities/Order.cs` | Apenas getters/setters, zero comportamento (LNTY-005 OFF por default, então **só dispara se toggle for ligado em lintty.yml do fixture**) | LNTY-005 |
| `Domain/Helpers/OrderHelper.cs` | Nome com sufixo `Helper` em Domain | LNTY-006 |
| `Domain` ↔ `Application` referência circular | Ciclo de projetos | LNTY-007 |
| `Infrastructure/PaymentClient.cs` (público, usado em Application) sem interface `IPaymentClient` em Domain | LNTY-008 |
| `Infrastructure/Repositories/MegaRepository.cs` método `DoEverything()` com 3000 LoC | LNTY-009 |

### `lintty.yml` do Sinner

LNTY-005 ligada explicitamente para esta fixture (já que default é OFF).

### `expected.json`

```json
{
  "compile_status": "success",
  "score_raw": 0,
  "score_grade": "F",
  "violations_count": 9,
  "violations_by_rule": {
    "LNTY-001": 1,
    "LNTY-002": 1,
    "LNTY-003": 1,
    "LNTY-004": 1,
    "LNTY-005": 1,
    "LNTY-006": 1,
    "LNTY-007": 1,
    "LNTY-008": 1,
    "LNTY-009": 1
  },
  "hard_lock_triggered": true,
  "ai_candidates_count": 2,
  "suppressions_parsed_count": 0
}
```

### Critério de sucesso

Score = **F**. Cada regra tem exatamente 1 violação. Hard lock ativo. Nem 0.85 da LNTY-004 vai salvar — Crítica aberta = F.

---

## 4. Ninja Test Progression

Cada Ninja é um trap focado, diagnóstico de uma capacidade específica do motor.

### Ninja #1 — "The Constant-Folded SQL" (Semana 2, MVP)

**Diagnóstico:** o motor usa `SemanticModel.GetConstantValue()` corretamente?

**Setup:**
```
Ninja01.sln
├── MyCompany.Orders.Domain.csproj          [tagged: domain]
│   └── Services/OrderQueryBuilder.cs
└── MyCompany.Orders.Infrastructure.csproj  [tagged: infrastructure]
    └── (vazio, só pra ter o tagging completo)
```

**Código armadilhado:**

```csharp
// MyCompany.Orders.Domain/Services/OrderQueryBuilder.cs
namespace MyCompany.Orders.Domain.Services;

public sealed class OrderQueryBuilder
{
    private const string Verb = "SE" + "LECT";
    private const string FromClause = "FR" + "OM";
    private const string Table = "Ord" + "ers";

    public string BuildLookupSql(int orderId)
    {
        // Nenhum literal SQL "puro" aparece. Regex em literal não pega.
        return $"{Verb} * {FromClause} {Table} WHERE Id = {orderId}";
    }

    public string BuildSummary(int orderId)
    {
        return "SE" + "LECT COUNT(*) " + "FR" + "OM " + Table;
    }
}
```

**Por que é diagnóstico:**

Visualmente, **nenhum string literal contém SQL completo**. Um motor ingênuo (regex sobre `LiteralExpressionSyntax`) **passa este código como limpo** → Score A → cliente assina → produção quebra → Lintty perde credibilidade.

A única forma de pegar é via **constant folding**: `SemanticModel.GetConstantValue(node)` resolve `"SE" + "LECT"` para `"SELECT"` antes de o programa rodar.

**`expected.json`:**

```json
{
  "compile_status": "success",
  "score_grade": "F",
  "violations": [
    {
      "rule_id": "LNTY-002",
      "severity": "critical",
      "target": "MyCompany.Orders.Domain.Services.OrderQueryBuilder.BuildLookupSql",
      "location": { "file": "Services/OrderQueryBuilder.cs", "line_start": 13 },
      "evidence_kind": "sql_literal_via_constant_folding",
      "resolved_string": "SELECT * FROM Orders WHERE Id = {0}"
    },
    {
      "rule_id": "LNTY-002",
      "severity": "critical",
      "target": "MyCompany.Orders.Domain.Services.OrderQueryBuilder.BuildSummary",
      "evidence_kind": "sql_literal_via_string_concat",
      "resolved_string": "SELECT COUNT(*) FROM Orders"
    }
  ],
  "hard_lock_triggered": true
}
```

---

### Ninja #2 — "The NuGet Bridge" (Semana 3, MVP)

**Diagnóstico:** o motor faz o **segundo pass de LNTY-001 via SymbolInfo.ContainingAssembly**?

**Setup:**

```
Ninja02.sln
├── MyCompany.Domain.csproj          [tagged: domain]
│   └── ProjectReferences: ZERO infra reference (parece limpo)
│   └── PackageReference: MyCompany.Internal.DataKit
├── MyCompany.Internal.DataKit/      (NuGet local com dummy)
│   └── Re-exporta Microsoft.EntityFrameworkCore.DbContext
│       como MyCompany.Internal.DataKit.PersistedDbContext
└── MyCompany.Infrastructure.csproj  [tagged: infrastructure]
```

**Código armadilhado:**

```csharp
// MyCompany.Domain/Services/OrderService.cs (em projeto Domain!)
using MyCompany.Internal.DataKit;  // NuGet privado

namespace MyCompany.Domain.Services;

public class OrderService
{
    private readonly PersistedDbContext _db;  // Tipo do NuGet,
                                              // mas resolve para EF Core.
    public OrderService(PersistedDbContext db) { _db = db; }
}
```

**Por que é diagnóstico:**

`Project.AllProjectReferences` do Domain.csproj **não tem nenhum referência a Infrastructure** — passa o pass 1 de LNTY-001 limpo. Mas o symbol `PersistedDbContext`, quando resolvido via `SemanticModel.GetSymbolInfo(node).Symbol.ContainingAssembly`, aponta para `Microsoft.EntityFrameworkCore` (porque o NuGet só re-exporta).

Motor que faça **apenas** o pass project-level: passa como limpo → falha grave.
Motor com pass symbol-level: detecta corretamente.

**`expected.json`:**

```json
{
  "compile_status": "success",
  "score_grade": "F",
  "violations": [
    {
      "rule_id": "LNTY-001",
      "severity": "critical",
      "target": "MyCompany.Domain.Services.OrderService",
      "evidence_kind": "forbidden_assembly_reference_via_alias",
      "resolved_assembly": "Microsoft.EntityFrameworkCore",
      "import_chain": "PersistedDbContext (MyCompany.Internal.DataKit) → Microsoft.EntityFrameworkCore.DbContext"
    }
  ],
  "hard_lock_triggered": true
}
```

---

### Ninja #3 — "The Ghost Cycle" (final Sprint 0)

**Diagnóstico:** o motor faz o segundo pass de LNTY-007 sobre o **grafo de bounded contexts**, não só de projects?

**Setup:**

```
Ninja03.sln
├── Orders.csproj            [bounded context: orders]
├── Payments.csproj          [bounded context: payments]
└── Shared.csproj            [bounded context: shared]
```

**Sem ciclo entre projects:**
- Orders → Shared (referencia interface)
- Payments → Shared (implementa interface)
- Shared → ninguém

Mas no nível de **bounded contexts**:
- Orders chama interface `IPaymentPort` que **vive em Shared mas é implementada em Payments**
- Payments chama código em Orders via outra abstração também em Shared

**Código armadilhado (resumo):**

```csharp
// Shared/IPaymentPort.cs
public interface IPaymentPort { void Charge(decimal amount); }

// Shared/IOrderQuery.cs
public interface IOrderQuery { Order Get(int id); }

// Orders/OrderService.cs
public class OrderService { 
    private readonly IPaymentPort _payment;  // resolve para Payments
    public OrderService(IPaymentPort p) { _payment = p; }
}

// Payments/PaymentService.cs
public class PaymentService : IPaymentPort {
    private readonly IOrderQuery _orders;  // resolve para Orders
    public PaymentService(IOrderQuery o) { _orders = o; }
    public void Charge(decimal amount) { /* usa _orders */ }
}
```

**Por que é diagnóstico:**

Project graph: `Orders → Shared`, `Payments → Shared`, sem ciclo. Tarjan sobre projects: zero violação.

Bounded context graph (cross-namespace symbol refs): `Orders → Payments → Orders`. Ciclo claro.

Motor com Tarjan apenas sobre projects: passa como limpo → falha grave (espaguete escondido).
Motor com dois Tarjans: detecta corretamente.

**`expected.json`:**

```json
{
  "compile_status": "success",
  "score_grade": "F",
  "violations": [
    {
      "rule_id": "LNTY-007",
      "severity": "critical",
      "target_type": "bounded_context_cycle",
      "evidence_kind": "scc_in_bounded_context_graph",
      "cycle_chain": ["orders", "payments", "orders"]
    }
  ],
  "hard_lock_triggered": true
}
```

---

## 5. Ninjas reservados para v1.1 (NÃO MVP)

Tentativas de burla mais avançadas. Motor MVP não é obrigado a pegar — entram como hardening em v1.1 via possíveis novas regras (LNTY-011, LNTY-012, LNTY-013).

### Ninja #4 — "Reflection-based DI Bypass"

```csharp
var asm = Assembly.Load("MyCompany.Infrastructure");
var t = asm.GetType("MyCompany.Infrastructure.OrderRepository");
var instance = Activator.CreateInstance(t);
```

LNTY-003 não pega isso (não é `ObjectCreationExpressionSyntax`). Resolveria via uma regra LNTY-011 que detecta o pattern `Activator.CreateInstance + Type.GetType + string`.

### Ninja #5 — "Source Generator Smuggling"

Source generator emite código de Infraestrutura em assembly Domain. Como motor exclui código gerado da análise, esse código passa "incógnito".

Mitigação V1.1: regra LNTY-012 que verifica **quais source generators estão referenciados em projetos Domain** e flagga aqueles conhecidos por emitir IO/persistência (EF Core source gen, etc).

### Ninja #6 — "Conditional Compilation Hide"

```csharp
#if DEBUG
using Microsoft.EntityFrameworkCore;  // só em debug
#endif
```

Mitigação V1.1: motor compila com **todas configurations** (DEBUG e RELEASE) e une as violações detectadas em qualquer uma.

---

## 6. Runner — como roda

### Local (durante dev do motor)

```bash
cd lintty-golden-tests
./runner/run.sh --engine ../lintty-engine/bin/Release/net9.0/lintty-engine
# ou específico:
./runner/run.sh --engine ../lintty-engine/... --case the-saint
```

Output:
```
✓ the-saint: PASSED (score=A, expected=A)
✓ the-sinner: PASSED (score=F, 9 violations matched)
✓ the-ninja-01: PASSED (LNTY-002 detected via constant folding)
✗ the-ninja-02: FAILED
  Expected violation LNTY-001 with evidence_kind=forbidden_assembly_reference_via_alias
  Got: no violations registered
  Diagnostic: motor parece estar fazendo apenas pass project-level
```

### CI

`.github/workflows/ci.yml` no repo do motor:

```yaml
on: [pull_request]
jobs:
  golden-tests:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - name: Setup .NET 9
        uses: actions/setup-dotnet@v4
        with: { dotnet-version: '9.0' }
      - name: Build engine
        run: dotnet build -c Release
      - name: Checkout golden tests
        uses: actions/checkout@v4
        with:
          repository: lintty/lintty-golden-tests
          path: golden-tests
      - name: Run golden suite
        run: |
          cd golden-tests
          ./runner/run.sh --engine ../bin/Release/net9.0/lintty-engine
```

Falha do golden = falha do CI = bloqueia merge.

---

## 7. Daily run em produção (drift detection)

Cloud Run Job scheduled diariamente:
1. Faz pull do `lintty-golden-tests`
2. Roda contra a infra **de produção** (incluindo LLM real)
3. Compara resultados
4. Qualquer divergência:
   - Para tráfego LLM novo (rollout pause)
   - Notifica equipe via PagerDuty
   - Avalia se foi mudança no modelo, atualização de prompt, ou bug regressivo

Esta é a defesa contra drift do LLM. Sonnet pode mudar comportamento sob hood; o golden suite é o detector primário.

---

## 8. Manutenção do Golden Suite

### Quando adicionar um novo caso

- Bug encontrado em produção que o golden não pegava → reproduzir como fixture, adicionar
- Nova regra publicada (Canon v1.1+) → fixtures dedicadas para testá-la
- Tentativa de burla descoberta em revisão de código real → entra como Ninja v1.1+

### Quando NÃO mexer

- "Ajustar expected porque o motor mudou" sem investigar **por que** mudou
- Adicionar `@lintty-ignore` no golden — golden é sagrado, não pode ter exceções

### Versionamento

Golden suite é versionada junto com o motor. Cada release do motor anota qual versão da suite passou:

```
lintty-engine v1.2.3 — golden suite v0.9.4 PASSED
```

Suite só pode evoluir **adicionando** casos. Remover/relaxar caso requer post-mortem documentado.
