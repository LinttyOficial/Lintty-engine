# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this repo is

Lintty is a B2B SaaS architecture oracle for outsourced .NET development. It analyzes a `.sln`, applies the **Lintty Canon** (architectural rules — Hexagonal/DDD), and emits a signed PDF audit report (the "laudo").

**Current phase: Sales Cut Tier 1 (pre-revenue validation).** Per the 2026-04-27 "Zero-IA, Zero-Cost" pivot, the V0 product runs **100% deterministic — Roslyn engine + QuestPDF reporter, no LLM, no cloud**. Authoritative scope and tradeoffs live in `docs/12-sales-cut.md`. Do not reintroduce LLM, GCP infra, PAdES signing, or hash-chain work without explicit "go" — those are V1+. The argument of sale is "same input → same PDF, byte-for-byte; zero hallucination."

## Repository layout

| Path | What it is | Status |
|---|---|---|
| `engine/` | .NET 8 solution `Lintty.Engine.sln` — the Roslyn engine (`Core`), CLI (`Cli`), audit PDF reporter (`Reporter`), and white-label brand PDF template (`Lintty.Docs.Pdf`, CLI `lintty-docs`) plus xUnit tests. **The only shipping code in the Sales Cut.** | Active |
| `fixtures/the-saint/`, `the-sinner/`, `the-ninja-01/` | Demo solutions with `lintty.yml` and `expected.json`. Saint → grade A, Sinner → F + 3 hard locks, Ninja-01 → LNTY-002 via constant-folded SQL. | Active |
| `landing/` | Static landing page (HTML + Tailwind CDN, no build step). `lintty.com` deploy target is Cloudflare Pages. | Active |
| `docs/` | All design/spec docs. `01-11` = full Blueprint (LOCKED but partially V1+). `12-sales-cut.md` is the **authoritative current scope**. `adr/` holds ADRs. `sales/`, `compliance/`, `llm/` carry artifacts. | Active |
| `docs/docs/` | Stale duplicate of `docs/` from earlier copy; **prefer the top-level `docs/`**. Don't write here. | Stale |
| `orchestrator/` | Python LLM orchestrator stub. **Deferred V1+.** Don't implement against it. | Stub |
| `.claude/agents/` | Specialized subagent definitions. See "Subagents" below. | Active |

## Engine commands (run from `engine/`)

```bash
dotnet build Lintty.Engine.sln                     # builds all 5 projects
dotnet test  Lintty.Engine.sln                     # runs all xUnit projects
dotnet test tests/Lintty.Engine.Reporter.Tests     # one project (e.g. determinism gate only)
dotnet test --filter "FullyQualifiedName~Sinner"   # one test class

# Run the CLI against a fixture (note: --solution path is relative to cwd):
dotnet run --project src/Lintty.Engine.Cli -- analyze \
  --solution ../fixtures/the-sinner/Sinner.sln \
  --pdf laudo-sinner.pdf
```

`global.json` pins SDK `9.0.300` with `rollForward: latestFeature`; the projects target `net8.0` (LTS, deterministic build). Central Package Management is in `engine/Directory.Packages.props` — **do not add `<PackageReference Version="...">`**, declare a `PackageVersion` there and reference it without a version in the csproj.

## CLI contract (versioned — be careful changing)

`lintty-engine analyze --solution <path> [--canon-version <ver>] [--output json|pretty] [--output-file <path>] [--fail-on-grade A|B|C|D|F] [--pdf <path>]`

- **stdout = JSON only** (or `pretty` table). All logs/diagnostics go to **stderr**, so pipelines like `... | jq` keep working.
- Exit codes (ADR 0001 §2): `0` grade better than `--fail-on-grade` (default `D`); `1` grade equal-or-worse; `2` execution error; `3` usage error.
- `--pdf` is additive (Sprint 1, ADR 0003): triggers `Lintty.Engine.Reporter.PdfReporter.GeneratePdf(json, path)`. Behavior without `--pdf` is preserved bit-for-bit.

## Architecture (big-picture)

### Engine pipeline (`Lintty.Engine.Core/Engine.cs::LinttyEngine.AnalyzeAsync`)

1. `Workspace/SolutionLoader` — `MSBuildLocator` + `MSBuildWorkspace`, captures non-fatal warnings as `WorkspaceDiagnostic`s.
2. `Tagging/LayerTagger` + `LinttyConfig` (parses `lintty.yml`) — classifies each `.csproj` as Domain/Application/Infrastructure/Presentation/DomainAbstractions. **Fail-fast:** if convention mode can't classify a project and there's no `explicit_map` entry, the scan errors with `LayerTaggingError`. **Never add a guessing fallback.**
3. `Analyzers/Lnty00*` run in fixed order against the loaded `Compilation`s. Each implements `IAnalyzer.AnalyzeAsync(AnalysisContext) → IReadOnlyList<Violation>`. **Active rules in Sales Cut: 001, 002, 003, 006, 007, 008, 009 (7 rules).** LNTY-004/005 require an LLM and are deferred V1+ — don't wire them up. LNTY-001/002/007 are **hard locks** (non-suppressible).
4. `Suppressions/LinttyIgnoreParser` — extracts `// @lintty-ignore: LNTY-XXX reason="..."` comments, validates (≥30 chars, rule exists, not a hard lock).
5. `Scoring/Scorer` — Canon formula (weights C=25/H=10/M=4/L=1, score rounded to nearest 5, clamped 0–100); any open Critical or suppression cap >10% of Med/High forces grade `F` and `seal_eligible=false`.
6. `Output/JsonReport` + `ReportSchema` — serializes the `ReportDto`. Fields are **sorted deterministically** (file, line, column, rule_id, fingerprint); culture is `Invariant`. Schema is locked at `1.0` and documented in ADR 0001 §3 — preserve every field even when unused (`inference_signature: null`, `ai_candidates: []`, etc.).
7. `inference_signature`, `audit_chain`, `sandbox_integrity`, `compile_status` and friends are **placeholders** for V1+. Don't repurpose them.

### Reporter (`Lintty.Engine.Reporter/PdfReporter.cs`, ADR 0003)

- QuestPDF (Community License — free up to US$1M ARR, set in `static PdfReporter()`).
- Reads the **compact** JSON (Cli passes `indented: false` so the hash is independent of pretty-print whitespace).
- Embedded fonts via `Theming/EmbeddedFonts` (Inter + JetBrains Mono in `Resources/`). **No system font fallback** — fail loudly if a TTF is missing.
- Footer carries `hash_content = sha256(report_json_utf8)`, **not** `hash_pdf` — option B in ADR 0003 §5.2 to avoid the circular-reference problem. Don't change to `hash_pdf` without re-reading that section.

### White-label brand PDFs (`Lintty.Docs.Pdf`)

Separate from the audit Reporter. Used for institutional documents (ADRs, briefings, manuals, anything **not** the laudo). Same QuestPDF stack and font assets (linked via `<EmbeddedResource>` from `Reporter/Resources/`), but its own palette (single accent: saint green `#0F4C3A`) and its own cover layout (low-and-left editorial). Spec in `docs/brand/pdf-identity.md`, decisions in `docs/adr/0004-brand-pdf-template.md`. CLI: `dotnet run --project src/Lintty.Docs.Pdf -- cover --title "..." --output file.pdf`. **Do not mix the two palettes** — the audit Reporter's red/green/amber are semantic (severity); brand documents stay neutral.

### Determinism is a product invariant — not a nice-to-have

`tests/Lintty.Engine.Reporter.Tests/DeterminismTests.cs` runs each fixture twice and asserts both `hash_content` and the PDF binary `sha256` match. **This test backs the entire pitch.** If anything you change makes it yellow, the change does not ship until you fix the source of nondeterminism (timestamps, random IDs, locale-sensitive formatting, font fallback, zlib version). Build props already enforce `<Deterministic>true</Deterministic>` and `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`.

### Fixtures are part of the contract

`fixtures/the-*/expected.json` is the regression baseline; `*FixtureTests.cs` (Saint A / Sinner F+3 hard locks / Ninja-01 LNTY-002 via constant folding) assert observable invariants from those snapshots. If you change an analyzer's output (snippet text, severity, field), update the affected `expected.json` and explain why in the PR — don't silently let the determinism gate drift.

## Subagents (`.claude/agents/`)

Use the right one rather than doing everything in the main thread:

- `backend-dev-dotnet` — engine/Roslyn/analyzer work, anything `.cs` in `engine/`.
- `qa-engineer` — fixtures, golden suite, calibration, false-positive hunting.
- `software-architect` — design decisions, ADRs, contract changes between components.
- `product-owner` — scope/MVP/Tier-1-vs-Tier-2 calls.
- `tech-writer-sales` — `docs/sales/`, deck, talk-track, landing copy, laudo PDF copy.
- `frontend-dev` — `landing/` and any future dashboard mockups.
- `security-compliance` — `docs/compliance/`, DPA, LGPD, signing roadmap.
- `ai-llm-engineer`, `backend-dev-cloud` — exist for V1+; **don't invoke for Sales Cut work** (no LLM, no GCP).

## Things to leave alone unless explicitly asked

- The orchestrator stub (`orchestrator/`) and the LLM specs (`docs/04-llm-ops.md`, `docs/adr/0002-llm-sprint-1.md`, `docs/llm/`, `docs/compliance/zdr-anthropic-plan.md`) — preserved as V1+ playbooks.
- The JSON `schema_version: "1.0"` and the placeholder fields it carries.
- The `--pdf` no-flag default (CLI must keep working without it).
- `hash_content` (don't switch to `hash_pdf`).
- Hard locks list (`LNTY-001`, `LNTY-002`, `LNTY-007`) and the 10% suppression cap — both are canon-defined.

## Authoritative references

- `docs/12-sales-cut.md` — current scope (read first).
- `docs/02-canon-v1.md` — the 9 rules, scoring, suppression rules.
- `docs/03-motor-roslyn.md` — engine spec.
- `docs/adr/0001-motor-skeleton.md` — JSON contract, exit codes, packages.
- `docs/adr/0003-pdf-reporter.md` — QuestPDF stack, layout, determinism strategy.
- `docs/manual-actions.md` — the running list of human-only TODOs (logo, CNPJ, lawyer, demo repos).
