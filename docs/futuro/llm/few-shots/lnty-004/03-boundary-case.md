# Few-shot 03 — LNTY-004 — Boundary case (medium confidence)

> **STATUS: V1+ ROADMAP** — preservado para reativação pós-Sales Cut.

## Input

```json
{
  "candidate_id": "sha256:3333cccc3333cccc3333cccc3333cccc",
  "rule_id": "LNTY-004",
  "file": "MyCompany.Orders.Persistence/OrderRepository.cs",
  "line": 113,
  "column": 9,
  "symbol_fqn": "MyCompany.Orders.Persistence.OrderRepository.UpsertWithVersionAsync",
  "code_snippet": "public async Task<Order> UpsertWithVersionAsync(Order order, CancellationToken ct = default)\n{\n    var existing = await _context.Orders\n        .FirstOrDefaultAsync(o => o.Id == order.Id, ct);\n\n    if (existing == null)\n    {\n        order.Version = 1;\n        _context.Orders.Add(order);\n    }\n    else\n    {\n        if (existing.Version != order.Version)\n            throw new ConcurrencyException(\"Optimistic lock failed.\");\n\n        order.Version = existing.Version + 1;\n        _context.Entry(existing).CurrentValues.SetValues(order);\n    }\n\n    await _context.SaveChangesAsync(ct);\n    return order;\n}",
  "ast_kind": "MethodDeclarationSyntax",
  "layer": "Infrastructure",
  "referenced_symbols": [
    "MyCompany.Orders.Domain.Order",
    "MyCompany.Orders.Domain.ConcurrencyException",
    "Microsoft.EntityFrameworkCore.DbSet`1.Add",
    "Microsoft.EntityFrameworkCore.EntityEntry.CurrentValues",
    "Microsoft.EntityFrameworkCore.DbContext.SaveChangesAsync"
  ],
  "additional_context": {
    "containing_type": "MyCompany.Orders.Persistence.OrderRepository",
    "implements": ["MyCompany.Orders.Domain.IOrderRepository"],
    "cyclomatic_complexity": 4,
    "comparison_operators_outside_linq": 2,
    "calls_outside_persistence_namespaces": [
      "MyCompany.Orders.Domain.Order.set_Version",
      "MyCompany.Orders.Domain.ConcurrencyException..ctor"
    ],
    "method_loc": 19,
    "is_repository_by_naming": true,
    "is_repository_by_interface": true
  }
}
```

## Expected output

```json
{
  "candidate_id": "sha256:3333cccc3333cccc3333cccc3333cccc",
  "rule_id": "LNTY-004",
  "verdict": "NO_VIOLATION",
  "defenses_considered": [
    {
      "defense_summary": "Version increment and concurrency check are optimistic-locking idiom — a persistence concern, not domain logic",
      "assessment": "Sustained with caveat. Optimistic locking is a well-established persistence pattern (RowVersion in EF Core, @Version in JPA); the version field exists primarily to coordinate writes, and detecting a stale version IS a persistence-layer responsibility. However, the version semantics belong to the Order aggregate — placing the increment in repository is borderline.",
      "accepted": true
    },
    {
      "defense_summary": "Branching on existing == null is purely insert-vs-update routing, mechanical persistence",
      "assessment": "Sustained. Distinguishing INSERT from UPDATE is the canonical job of an upsert method; this is not a business rule.",
      "accepted": true
    },
    {
      "defense_summary": "Throwing ConcurrencyException could be argued as business rule enforcement",
      "assessment": "Weak. ConcurrencyException is a persistence-layer concept (defined in Domain only because Domain owns abstractions); the throw is a reaction to a persistence-layer detection of stale state, not a domain decision.",
      "accepted": true
    }
  ],
  "rejection_reasons": [],
  "evidence_quote": "Method 'UpsertWithVersionAsync' implements optimistic-locking upsert with version increment and concurrency-conflict detection. While the version increment arguably belongs in the Order aggregate, the pattern as a whole is a recognized persistence-layer idiom and does not encode domain pricing, validation, or workflow rules.",
  "severity_proposed": "high",
  "confidence": "medium"
}
```

## Calibration note

Caso fronteira que ensina o LLM a usar `confidence: "medium"` honestamente. Optimistic locking é um padrão consagrado da literatura de persistência mas o version increment imperativo pode legitimamente ser argumentado como responsabilidade do aggregate. A defesa procede mas com caveat — exatamente o sinal que o motor usa (canon §LNTY-004 + `04-llm-ops.md` §4) para decidir se dispara Prompt B refutador no Sprint 2+. Sprint 1 simplesmente registra a violação como NO_VIOLATION com confidence média e expõe ao auditor humano via PDF.
