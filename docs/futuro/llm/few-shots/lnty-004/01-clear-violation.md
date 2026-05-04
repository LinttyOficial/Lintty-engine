# Few-shot 01 — LNTY-004 — Clear VIOLATION

> **STATUS: V1+ ROADMAP** — preservado para reativação pós-Sales Cut.

## Input

```json
{
  "candidate_id": "sha256:1111aaaa1111aaaa1111aaaa1111aaaa",
  "rule_id": "LNTY-004",
  "file": "MyCompany.Orders.Persistence/OrderRepository.cs",
  "line": 47,
  "column": 9,
  "symbol_fqn": "MyCompany.Orders.Persistence.OrderRepository.SaveAsync",
  "code_snippet": "public async Task SaveAsync(Order order, CancellationToken ct = default)\n{\n    if (order.Total > 1000m)\n    {\n        order.Total *= 0.90m; // 10% loyalty discount\n    }\n    else if (order.Total > 500m)\n    {\n        order.Total *= 0.95m; // 5% mid-tier\n    }\n\n    if (string.IsNullOrEmpty(order.CustomerName))\n        throw new InvalidOperationException(\"Customer is required.\");\n\n    _context.Orders.Add(order);\n    await _context.SaveChangesAsync(ct);\n}",
  "ast_kind": "MethodDeclarationSyntax",
  "layer": "Infrastructure",
  "referenced_symbols": [
    "MyCompany.Orders.Domain.Order",
    "Microsoft.EntityFrameworkCore.DbSet`1.Add",
    "Microsoft.EntityFrameworkCore.DbContext.SaveChangesAsync"
  ],
  "additional_context": {
    "containing_type": "MyCompany.Orders.Persistence.OrderRepository",
    "implements": ["MyCompany.Orders.Domain.IOrderRepository"],
    "cyclomatic_complexity": 5,
    "comparison_operators_outside_linq": 2,
    "calls_outside_persistence_namespaces": [
      "MyCompany.Orders.Domain.Order.set_Total"
    ],
    "method_loc": 17,
    "is_repository_by_naming": true,
    "is_repository_by_interface": true
  }
}
```

## Expected output

```json
{
  "candidate_id": "sha256:1111aaaa1111aaaa1111aaaa1111aaaa",
  "rule_id": "LNTY-004",
  "verdict": "VIOLATION",
  "defenses_considered": [
    {
      "defense_summary": "Threshold check (order.Total > 1000m) could be argued as input validation enforcing a database constraint",
      "assessment": "Rejected. The code does not reject or normalize input — it MUTATES order.Total by applying a discount multiplier. This is policy execution, not validation.",
      "accepted": false
    },
    {
      "defense_summary": "Discount application could be a pre-persistence denormalization step legitimate for ORM",
      "assessment": "Rejected. ORM denormalization typically projects already-computed values; here the discount is COMPUTED imperatively based on business tiers (1000/500), which is a pricing rule.",
      "accepted": false
    },
    {
      "defense_summary": "CustomerName null check could be a database NOT NULL constraint pre-check",
      "assessment": "Partially valid as defense for that single line, but does not redeem the discount logic above. Even granting this defense, the threshold-based discount remains a clear domain rule in the repository.",
      "accepted": false
    }
  ],
  "rejection_reasons": [
    "Method branches on Order.Total against business thresholds (1000, 500) and applies tiered discount multipliers (0.90, 0.95) before persisting; this is pricing policy, a domain concern.",
    "Mutation of Order.Total prior to SaveChangesAsync couples the persistence boundary to business rules that belong in the Order aggregate or a PricingService."
  ],
  "evidence_quote": "Method 'SaveAsync' applies threshold-based loyalty discount logic (10% above 1000, 5% above 500) prior to persistence, coupling the repository to domain pricing rules.",
  "severity_proposed": "high",
  "confidence": "high"
}
```

## Calibration note

Caso óbvio de VIOLATION para ancorar o LLM. O método combina dois sinais que canon §LNTY-004 lista no pré-filtro Roslyn (complexidade > 3, comparações fora de LINQ) com lógica de pricing imperativa. O exemplo ensina o LLM a (a) rejeitar a defesa "é validação" quando o código MUTA estado em vez de só validar, e (b) emitir `evidence_quote` em tom clínico apropriado para o PDF.
