# Laudo de Referência — The Sinner

> **Pivô 2026-04-27 (Zero-IA):** este markdown deixa de ser "mock para Word/Figma" e passa a ser **especificação de conteúdo do PDF gerado pelo motor** via QuestPDF (Sprint 1, `docs/adr/0003-pdf-reporter.md`). O PDF do Sales Cut é artefato vivo — sai do `lintty-engine analyze --pdf`.
> Caso: fixture `the-sinner` (Sinner.sln) — grade F, 10 violações, 3 hard locks.
> Este markdown define o **conteúdo**. O **layout visual** vem do projeto `Lintty.Engine.Reporter` (QuestPDF).
> `[NO SALES CUT, O PDF AINDA NÃO É ASSINADO. PDF de produção (V1+) será assinado digitalmente com PAdES-B-LT + TSA RFC 3161, garantindo timestamp e validade jurídica.]`

---

## Página 1 — Capa

```
┌────────────────────────────────────────────────────────────┐
│  [LOGO LINTTY MASTER PNG]                                  │  ← chancela top-left, altura 44pt
│                                                            │
│  LAUDO DE AUDITORIA ARQUITETURAL                           │  ← kicker uppercase, BrandNavy, tracking
│  ─────                                                     │  ← fio teal accent (56pt × 1.5pt)
│                                                            │
│                                                            │
│                       ┌─────────┐                          │
│                       │         │                          │
│                       │    F    │                          │  ← grade 160pt Bold, vermelho semântico
│                       │         │                          │
│                       └─────────┘                          │
│                                                            │
│                  SELO NÃO EMITIDO                          │  ← uppercase, vermelho #B91C1C
│              Score numérico: 0/100                         │
│                                                            │
│  ─────────────────────────────────────────────────────     │  ← hairline #E5E7EB
│  Solution    │  Sinner.sln                                 │
│  Canon       │  v1.0.0 (rule_set v1.0.0)                   │
│  Run ID      │  run_99da20b203e00b63  (mono)               │
│  Compile     │  success                                    │
│  Grade       │  F  •  Score 0/100                          │
└────────────────────────────────────────────────────────────┘
```

> Visual: a logo master Lintty é a única assinatura institucional na capa (top-left, 44pt). Brand frame (kicker BrandNavy, fio BrandAccent) é discreto — a paleta funcional de severidade (vermelho/verde/âmbar) governa a leitura. Grade gigante e SELO permanecem no centro do terço inferior, vermelho #B91C1C para F, verde #15803D para A. Sem ilustração decorativa, sem sombra, sem ícone extra. Spec da identidade: ADR 0003 §4.4.

---

## Página 1 (continuação) — Sumário Executivo

> Análise estática type-aware sobre solution `Sinner.sln` (3 projetos, 14 arquivos, 1.842 LoC). 9 violações detectadas, 3 hard locks atingidos. Selo arquitetural não emitido conforme Lintty Canon v1.0.0 §"Hard Locks em Violações Críticas" (LNTY-001, LNTY-002, LNTY-007). Score final: F.

---

## Página 1 (continuação) — Sumário de Exceções

| `rule_id` | `arquivo:linha` | `autor_git` | Justificativa |
|-----------|-----------------|-------------|---------------|
| LNTY-009  | `Application/Services/ReportService.cs:127` | dev@empresa.com.br | "Método de geração de relatório fiscal SPED — complexidade ciclomática inerente ao layout legal do bloco C170, refatorar quebraria conformidade." |

> 1 supressão `@lintty-ignore` válida. Não houve excesso do cap de impunidade (10% sobre Médias/Altas). Justificativa com 30+ caracteres e autor git rastreável.

---

## Página 2 — Lista de Violações Detectadas

### LNTY-001 — Domain Layer Isolation

- **Severity:** Crítica (Hard Lock)
- **Arquivo:** `Domain/Orders/Order.cs:42`
- **`ast_kind`:** `UsingDirectiveSyntax`
- **Justificativa:** Camada Domain importa `System.Data.SqlClient` — viola isolamento de domínio.

```csharp
// Domain/Orders/Order.cs
using System;
using System.Data.SqlClient;   // ← violação: Domain não pode conhecer infra
using System.Collections.Generic;

namespace Sinner.Domain.Orders
{
    public class Order
```

---

### LNTY-002 — Persistence Contamination

- **Severity:** Crítica (Hard Lock)
- **Arquivo:** `Domain/Customers/Customer.cs:88`
- **`ast_kind`:** `LiteralExpressionSyntax` (string SQL)
- **Justificativa:** Literal SQL embutido em método de domínio — contamina camada com persistência.

```csharp
// Domain/Customers/Customer.cs
public bool IsEligibleForCredit()
{
    var query = "SELECT credit_limit FROM customers WHERE id = " + this.Id;
    return ExecuteRaw(query) > 0;
}
```

---

### LNTY-003 — Forbidden Instantiation

- **Severity:** Média
- **Arquivo:** `Application/Inventory/Inventory.cs:54`
- **`ast_kind`:** `ObjectCreationExpressionSyntax`
- **Justificativa:** Application instancia `SqlConnection` diretamente em vez de receber port via DI.

```csharp
// Application/Inventory/Inventory.cs
public void RestockItem(int itemId, int qty)
{
    var conn = new SqlConnection(_connectionString);   // ← violação: instanciação direta de adapter
    conn.Open();
    // ...
}
```

---

### LNTY-006 — Ubiquitous Language Leak

- **Severity:** Baixa (Determinística)
- **Arquivo:** `Application/Orders/OrderService.cs:18`
- **`ast_kind`:** `ClassDeclarationSyntax`
- **Justificativa:** Classe nomeada `OrderManager` em projeto cujo glossário define `OrderService` como termo canônico.

```csharp
// Application/Orders/OrderService.cs
namespace Sinner.Application.Orders
{
    public class OrderManager   // ← violação: glossário define 'OrderService'
    {
        // ...
    }
}
```

---

### LNTY-007 — Dependency Cycles

- **Severity:** Crítica (Hard Lock)
- **Arquivo:** `Application/Sinner.Application.csproj:12` ↔ `Infrastructure/Sinner.Infrastructure.csproj:9`
- **`ast_kind`:** `ProjectReference` (graph cycle)
- **Justificativa:** Ciclo de dependência detectado entre Application e Infrastructure — Infrastructure referencia Application e vice-versa.

```xml
<!-- Application/Sinner.Application.csproj -->
<ItemGroup>
  <ProjectReference Include="..\Infrastructure\Sinner.Infrastructure.csproj" />
</ItemGroup>

<!-- Infrastructure/Sinner.Infrastructure.csproj -->
<ItemGroup>
  <ProjectReference Include="..\Application\Sinner.Application.csproj" />
</ItemGroup>
```

---

### LNTY-008 — Ports at Boundaries

- **Severity:** Alta
- **Arquivo:** `Application/Orders/OrderService.cs:33`
- **`ast_kind`:** `ParameterSyntax`
- **Justificativa:** Construtor de `OrderService` recebe `SqlOrderRepository` (classe concreta de Infrastructure) em vez de port `IOrderRepository`.

```csharp
// Application/Orders/OrderService.cs
public OrderService(SqlOrderRepository repo)   // ← violação: deveria ser IOrderRepository
{
    _repo = repo;
}
```

---

### LNTY-008 — Ports at Boundaries (segunda ocorrência)

- **Severity:** Alta
- **Arquivo:** `Application/Inventory/InventoryService.cs:22`
- **`ast_kind`:** `FieldDeclarationSyntax`
- **Justificativa:** Field tipado como `EmailSender` (adapter) em vez de port `INotificationPort`.

```csharp
// Application/Inventory/InventoryService.cs
public class InventoryService
{
    private readonly EmailSender _sender;   // ← violação: deveria ser INotificationPort
}
```

---

### LNTY-009 — Method Exceeds Analyzability

- **Severity:** Média
- **Arquivo:** `Application/Services/ReportService.cs:127`
- **`ast_kind`:** `MethodDeclarationSyntax`
- **Justificativa:** Método `GenerateMonthlyReport` excede limite de complexidade ciclomática do canon (>15).
- **Status:** Suprimida via `@lintty-ignore` válido (ver Sumário de Exceções na página 1).

```csharp
// Application/Services/ReportService.cs
// @lintty-ignore: LNTY-009 reason="Método de geração de relatório fiscal SPED — complexidade ciclomática inerente ao layout legal do bloco C170, refatorar quebraria conformidade."
public Report GenerateMonthlyReport(int month, int year)
{
    // ... 180 linhas, complexidade ciclomática 22
}
```

---

### LNTY-002 — Persistence Contamination (segunda ocorrência via constant folding)

- **Severity:** Crítica (Hard Lock)
- **Arquivo:** `Domain/Inventory/StockPolicy.cs:64`
- **`ast_kind`:** `BinaryExpressionSyntax` (constant-folded literal)
- **Justificativa:** SQL ocultado por concatenação de constantes — Roslyn resolve em tempo de análise: `"SE" + "LECT"` → `"SELECT"`.

```csharp
// Domain/Inventory/StockPolicy.cs
private const string PFX = "SE";
private const string CMD = PFX + "LECT * FROM stock WHERE sku = @sku";
//                              ↑ constant-folded → SELECT * FROM stock WHERE sku = @sku
```

---

## Página 3 — Grafo de Dependências

```
[INSERIR DIAGRAMA DE DEPENDÊNCIAS — DESTACAR CICLO Application ↔ Infrastructure EM VERMELHO]

Representação ASCII de fallback:

    ┌─────────────────┐
    │     Domain      │
    └─────────────────┘
            ▲
            │ (ok)
            │
    ┌─────────────────┐         ┌─────────────────┐
    │   Application   │ ◀═════▶ │ Infrastructure  │
    └─────────────────┘  CICLO  └─────────────────┘
            ▲                          │
            │                          │
            │     ┌──────────────┐    │
            └─────│  Presentation│◀───┘
                  └──────────────┘
```

> Visual: [INSERIR GRAFO LIMPO COM CAIXAS POR PROJETO + SETAS DIRECIONAIS. SETA DUPLA ENTRE Application E Infrastructure EM VERMELHO COM LABEL 'CICLO LNTY-007'. RESTANTE EM CINZA NEUTRO.]

---

## Rodapé Técnico (impresso em todas as páginas)

```
canon_version: 1.0.0  •  rule_set_version: 1.0.0  •  run_id: 7f3a4d2e-9c81-4ba6-5f0d-11ee8aabbccd
inference_signature: null  (Sales Cut roda Zero-IA — sem LLM no pipeline)
hash_pdf (sha256): [GERADO NA EXPORTAÇÃO PELO MOTOR]
audit_chain_position: null  (audit chain entra em V1+)
generated_by: lintty-engine 0.1.0 (QuestPDF reporter)
```

> **Nota V0:** `inference_signature` permanece `null` enquanto a camada LLM estiver desativada (ver `docs/futuro/llm-ops.md` — V1+). Quando reativada, o campo carrega a tupla `{model, model_snapshot_id, prompt_hash, few_shot_hash, system_prompt_hash, temperature}` conforme spec do ADR 0002 (preservado em `docs/futuro/adr-0002-llm-sprint-1.md`) §6.

---

## Aviso explícito (rodapé da última página)

> **[NO SALES CUT, O PDF AINDA NÃO É ASSINADO DIGITALMENTE. PDF de produção (V1+) será assinado com PAdES-B-LT + TSA RFC 3161, garantindo timestamp e validade jurídica nacional.]**

---

## Notas para o `Lintty.Engine.Reporter` (QuestPDF — Sprint 1)

> Spec normativa de implementação em `docs/adr/0003-pdf-reporter.md`. Resumo abaixo.

- **Tipografia:** Inter (sans-serif) + JetBrains Mono (monospace), **embedded no PDF** (NuGet `QuestPDF.Companion` ou subset embedded direto). Determinismo bit-a-bit exige que fonte não dependa de instalação local do leitor.
- **Paleta:** cinza neutro (#1F2937, #6B7280, #E5E7EB), vermelho para F e hard locks (#B91C1C), verde apenas para A em outros laudos (#15803D).
- **Capa:** F gigante no centro, "SELO NÃO EMITIDO" em caixa alta abaixo. Sem ilustração decorativa.
- **Snippets de código:** monospaced, fundo cinza claro, com numeração de linha real. Linha violadora destacada.
- **Tabelas:** linhas finas, sem alternância chamativa. Profissional, não dashboard.
- **Sem stock photo. Sem ilustração genérica de "tecnologia". Sem ícone decorativo.**
- **Determinismo:** sem timestamps no conteúdo do PDF (só `run_id` UUID derivado do scan). Hash sha256 do binário PDF é gerado **após** a finalização e impresso no rodapé via segunda passagem (ou em arquivo `.sha256` ao lado).
