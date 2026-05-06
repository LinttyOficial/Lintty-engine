# Foreigner — fixture for permissive layer-tagging

Three projects with names that do **not** match any convention pattern in
`LinttyConfig.DefaultConventionMap` (no `*.Domain`, `*.Application`,
`*.Infrastructure`, `*.Web`, etc.):

- `Acme.PaymentProcessor`
- `Acme.Webhooks`
- `Acme.SharedKernel`

There is **no `lintty.yml`** at the fixture root.

## Why it exists

Before 2026-05-06, the engine refused to produce a laudo when any project
fell through to `Layer.Unknown` — it threw `LayerTaggingError` and exited
with code 2. That's hostile to the public Web Inspector flow, where most
prospects don't ship a `lintty.yml`.

Per the permissive layer-tagging change, the engine now:

1. Tags unclassifiable projects as `Layer.Unknown`.
2. Skips layer-aware rules (LNTY-001 Domain Layer Isolation, LNTY-008
   Ports at Boundaries) on those projects.
3. Still runs layer-agnostic rules (LNTY-007 Dependency Cycles, LNTY-009
   Method Exceeds Analyzability).
4. Adds an `"Unknown"` row to `layer_summary` and a CTA in the laudo PDF.

The Foreigner fixture exercises that path end-to-end.

## Expected behavior

- Grade: **A** (no violations, no hard locks).
- `layer_summary`: contains an `"Unknown"` entry with `projects: 3`.
- `seal_eligible`: `true`.
- The PDF laudo carries a yellow "Unknown" callout above the hard-lock
  callout slot (which is empty in this case).
