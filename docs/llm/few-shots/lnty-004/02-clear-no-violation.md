# Few-shot 02 — LNTY-004 — Clear NO_VIOLATION

> **STATUS: V1+ ROADMAP** — preservado para reativação pós-Sales Cut.

## Input

```json
{
  "candidate_id": "sha256:2222bbbb2222bbbb2222bbbb2222bbbb",
  "rule_id": "LNTY-004",
  "file": "MyCompany.Orders.Persistence/OrderRepository.cs",
  "line": 82,
  "column": 9,
  "symbol_fqn": "MyCompany.Orders.Persistence.OrderRepository.FindActiveOrdersAsync",
  "code_snippet": "public async Task<IReadOnlyList<Order>> FindActiveOrdersAsync(int customerId, CancellationToken ct = default)\n{\n    return await _context.Orders\n        .AsNoTracking()\n        .Include(o => o.Items)\n        .Include(o => o.Customer)\n        .Where(o => o.CustomerId == customerId\n                 && o.Status == OrderStatus.Active\n                 && o.CreatedAt > DateTime.UtcNow.AddDays(-90))\n        .OrderByDescending(o => o.CreatedAt)\n        .Take(100)\n        .ToListAsync(ct);\n}",
  "ast_kind": "MethodDeclarationSyntax",
  "layer": "Infrastructure",
  "referenced_symbols": [
    "MyCompany.Orders.Domain.Order",
    "MyCompany.Orders.Domain.OrderStatus",
    "Microsoft.EntityFrameworkCore.DbSet`1.AsNoTracking",
    "Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.Include",
    "Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync"
  ],
  "additional_context": {
    "containing_type": "MyCompany.Orders.Persistence.OrderRepository",
    "implements": ["MyCompany.Orders.Domain.IOrderRepository"],
    "cyclomatic_complexity": 4,
    "comparison_operators_outside_linq": 0,
    "comparison_operators_inside_linq": 3,
    "calls_outside_persistence_namespaces": [],
    "method_loc": 12,
    "is_repository_by_naming": true,
    "is_repository_by_interface": true
  }
}
```

## Expected output

```json
{
  "candidate_id": "sha256:2222bbbb2222bbbb2222bbbb2222bbbb",
  "rule_id": "LNTY-004",
  "verdict": "NO_VIOLATION",
  "defenses_considered": [
    {
      "defense_summary": "Multiple Where conditions with comparison operators are LINQ expression-tree filters, not imperative business branching",
      "assessment": "Sustained. All three comparisons (CustomerId equality, Status equality, CreatedAt > 90 days ago) live inside a single Where(...) expression tree that EF Core translates to SQL. This is query shape, the legitimate responsibility of a repository.",
      "accepted": true
    },
    {
      "defense_summary": "The 90-day window could be argued as a business rule embedded in persistence",
      "assessment": "Plausible concern, but the 90-day filter is a query scope (which orders to load), not a decision computed on loaded data. It would be a violation only if the method first loaded all orders and then filtered/branched in-memory based on age.",
      "accepted": true
    }
  ],
  "rejection_reasons": [],
  "evidence_quote": "Method 'FindActiveOrdersAsync' constructs a parameterized EF Core query with eager loading and pagination; all comparison operators are inside the Where expression tree and translate to SQL. This is legitimate persistence concern.",
  "severity_proposed": "high",
  "confidence": "high"
}
```

## Calibration note

Caso óbvio de NO_VIOLATION que **parece** violação ao pré-filtro Roslyn (complexidade 4, três comparações). O exemplo ensina o LLM a distinguir comparações **dentro de expression tree LINQ** (legítimas) de comparações **fora de LINQ** (suspeitas). É o caso enganoso da §"Não-violação enganosa" de `04-llm-ops.md` §2 — crítico para reduzir falsos positivos, que são o pior tipo de erro segundo o agent definition (perde-se confiança do contratante).
