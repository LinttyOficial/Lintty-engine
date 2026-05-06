# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this repo is

Lintty is a B2B architecture oracle for outsourced .NET development. It analyzes a `.sln`, applies the **Lintty Canon** (architectural rules — Hexagonal/DDD), and emits a deterministic PDF audit report (the "laudo").

**Current phase: V0 — deterministic CLI + Web Inspector (in build).** Per the 2026-04-27 Zero-IA pivot and the 2026-04-30 doc resize, the V0 product runs **100% deterministic — Roslyn engine + QuestPDF reporter, no LLM, no cloud beyond a single VM/Cloud Run service for the Web Inspector**. Two consumption paths:

1. **CLI Self-Service (default, [ADR 0005](docs/adr/0005-distribution-model.md)):** client downloads the official binary from GitHub Releases, runs `lintty-engine analyze --solution X.sln --pdf laudo.pdf` locally. Source code never leaves their machine.
2. **Web Inspector (in build, `docs/13-web-inspector.md`):** client pastes a GitHub URL on `lintty.com/inspect`, our backend shallow-clones, invokes the **same CLI**, returns the PDF, discards the clone.

Authoritative current scope is **`docs/00-onde-estamos.md`** (TL;DR), then `docs/15-roadmap-curto.md` for the 8–12 week plan and `docs/12-sales-cut.md` for the sales angle. Do not reintroduce LLM, full GCP infra, PAdES signing, multi-tenant dashboards, billing wallets, or hash-chain work without explicit "go" — those live preserved in `docs/futuro/` as V1+ playbooks. The argument of sale is "same input → same PDF, byte-for-byte; zero hallucination; code never leaves your machine."

## Repository layout

| Path | What it is | Status |
|---|---|---|
| `engine/` | .NET 8 solution `Lintty.Engine.sln` — Roslyn engine (`Core`), CLI (`Cli`), audit PDF reporter (`Reporter`), white-label brand PDF (`Lintty.Docs.Pdf`, CLI `lintty-docs`), and the **Web Inspector ASP.NET minimal API** (`Lintty.WebInspector`) that wraps the same CLI. xUnit tests for each. **The only shipping code in the V0.** | Active |
| `fixtures/the-saint/`, `the-sinner/`, `the-ninja-01/` | Demo solutions with `lintty.yml` and `expected.json`. Saint → grade A, Sinner → F + 3 hard locks, Ninja-01 → LNTY-002 via constant-folded SQL. | Active |
| `frontend/` | Next.js 15 + TypeScript + Tailwind 3, static-exported to `frontend/out/`. Replaces the legacy `landing/`. Pages: `/` (marketing), `/cli`, `/pricing`, `/privacidade`, `/inspect` (Web Inspector with 4 states + 2s polling), `/login`, `/signup`, `/dashboard`. Build with `cd frontend && npm install && npm run build`. The Web Inspector ASP.NET host serves `frontend/out/` same-origin in dev so fetches against `/api/*` skip CORS. See `frontend/README.md`. | Active |
| `docs/` | Design/spec docs aligned to V0 (`00-onde-estamos.md`, `01-product-vision.md`, `02-canon-v1.md`, `03-motor-cli.md`, `09`, `11`, `12`, `13-web-inspector.md`, `14-cli-distribution.md`, `15-roadmap-curto.md`). `adr/` holds active ADRs. `sales/`, `compliance/`, `brand/` carry artifacts. Read `docs/README.md` for the index. | Active |
| `docs/futuro/` | **V1+ blueprint preserved** — full LLM, GCP, multi-tenant, SOC 2, PAdES, hash-chain plans. **Do not use as operational reference.** Don't build from these without explicit "go". | Roadmap |
| `docs/docs/` | Stale duplicate of `docs/` from earlier copy; **prefer the top-level `docs/`**. Don't write here. | Stale |
| `orchestrator/` | Python LLM orchestrator stub. **Deferred V1+.** Don't implement against it. | Stub |
| `.claude/agents/` | Specialized subagent definitions. See "Subagents" below. | Active |

## Engine commands (run from `engine/`)

```bash
dotnet build Lintty.Engine.sln                     # builds all projects (Core, Cli, Reporter, Docs.Pdf, WebInspector + tests)
dotnet test  Lintty.Engine.sln                     # runs all xUnit projects
dotnet test tests/Lintty.Engine.Reporter.Tests     # one project (e.g. determinism gate only)
dotnet test --filter "FullyQualifiedName~Sinner"   # one test class

# Run the CLI against a fixture (note: --solution path is relative to cwd):
dotnet run --project src/Lintty.Engine.Cli -- analyze \
  --solution ../fixtures/the-sinner/Sinner.sln \
  --pdf laudo-sinner.pdf

# Run the Web Inspector locally (ASP.NET minimal API, wraps the same CLI):
dotnet run --project src/Lintty.WebInspector
# Jobs persist in src/Lintty.WebInspector/var/lintty/jobs.sqlite (SQLite, WAL mode).
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
3. `Analyzers/Lnty00*` run in fixed order against the loaded `Compilation`s. Each implements `IAnalyzer.AnalyzeAsync(AnalysisContext) → IReadOnlyList<Violation>`. **Active rules in V0: 001, 002, 003, 006, 007, 008, 009 (7 rules).** LNTY-004/005 require an LLM and are deferred V1+ — don't wire them up (see `docs/02-canon-v1.md`). LNTY-001/002/007 are **hard locks** (non-suppressible).
4. `Suppressions/LinttyIgnoreParser` — extracts `// @lintty-ignore: LNTY-XXX reason="..."` comments, validates (≥30 chars, rule exists, not a hard lock).
5. `Scoring/Scorer` — Canon formula (weights C=25/H=10/M=4/L=1, score rounded to nearest 5, clamped 0–100); any open Critical or suppression cap >10% of Med/High forces grade `F` and `seal_eligible=false`.
6. `Output/JsonReport` + `ReportSchema` — serializes the `ReportDto`. Fields are **sorted deterministically** (file, line, column, rule_id, fingerprint); culture is `Invariant`. Schema is locked at `1.0` and documented in ADR 0001 §3 — preserve every field even when unused (`inference_signature: null`, `ai_candidates: []`, etc.).
7. `inference_signature`, `audit_chain`, `sandbox_integrity`, `compile_status` and friends are **placeholders** for V1+. Don't repurpose them.

### Reporter (`Lintty.Engine.Reporter/PdfReporter.cs`, ADR 0003)

- QuestPDF (Community License — free up to US$1M ARR, set in `static PdfReporter()`).
- Reads the **compact** JSON (Cli passes `indented: false` so the hash is independent of pretty-print whitespace).
- Embedded fonts via `Theming/EmbeddedFonts` (Inter + JetBrains Mono in `Resources/`). **No system font fallback** — fail loudly if a TTF is missing.
- Footer carries `hash_content = sha256(report_json_utf8)`, **not** `hash_pdf` — option B in ADR 0003 §5.2 to avoid the circular-reference problem. Don't change to `hash_pdf` without re-reading that section.

### Web Inspector (`Lintty.WebInspector`)

ASP.NET minimal API that turns the CLI into a hosted flow: client posts a GitHub URL → backend shallow-clones → invokes the **same `lintty-engine`** binary → returns the laudo PDF → discards the clone (ephemeral, per `docs/13-web-inspector.md`). Job state lives in `var/lintty/jobs.sqlite` (SQLite WAL). **Cross-determinism gate:** the PDF produced by the Web Inspector must be byte-identical to one produced by the CLI on the same input — this is the QA invariant in `.claude/agents/qa-engineer.md`. Don't introduce timestamps, request IDs, host metadata, or anything else into the report path that isn't already in the CLI's output.

### White-label brand PDFs (`Lintty.Docs.Pdf`)

Separate from the audit Reporter. Used for institutional documents (ADRs, briefings, manuals, anything **not** the laudo). Same QuestPDF stack and font assets (linked via `<EmbeddedResource>` from `Reporter/Resources/`), but its own palette (single accent: saint green `#0F4C3A`) and its own cover layout (low-and-left editorial). Spec in `docs/brand/pdf-identity.md`, decisions in `docs/adr/0004-brand-pdf-template.md`. CLI: `dotnet run --project src/Lintty.Docs.Pdf -- cover --title "..." --output file.pdf`. **Do not mix the two palettes** — the audit Reporter's red/green/amber are semantic (severity); brand documents stay neutral.

### Determinism is a product invariant — not a nice-to-have

`tests/Lintty.Engine.Reporter.Tests/DeterminismTests.cs` runs each fixture twice and asserts both `hash_content` and the PDF binary `sha256` match. **This test backs the entire pitch.** If anything you change makes it yellow, the change does not ship until you fix the source of nondeterminism (timestamps, random IDs, locale-sensitive formatting, font fallback, zlib version). Build props already enforce `<Deterministic>true</Deterministic>` and `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`.

### Fixtures are part of the contract

`fixtures/the-*/expected.json` is the regression baseline; `*FixtureTests.cs` (Saint A / Sinner F+3 hard locks / Ninja-01 LNTY-002 via constant folding) assert observable invariants from those snapshots. If you change an analyzer's output (snippet text, severity, field), update the affected `expected.json` and explain why in the PR — don't silently let the determinism gate drift.

## Subagents (`.claude/agents/`)

Use the right one rather than doing everything in the main thread:

- `backend-dev-dotnet` — engine/Roslyn/analyzer work and the Web Inspector ASP.NET host. Anything `.cs` in `engine/`.
- `qa-engineer` — fixtures, golden suite, calibration, false-positive hunting, **cross-determinism gate (CLI vs Web Inspector PDFs)**.
- `software-architect` — design decisions, ADRs, contract changes between components.
- `product-owner` — scope/MVP/Tier-1-vs-Tier-2 calls.
- `tech-writer-sales` — `docs/sales/`, deck, talk-track, landing copy, laudo PDF copy.
- `frontend-dev` — `landing/` and any future dashboard mockups.
- `security-compliance` — `docs/compliance/`, DPA, LGPD, signing roadmap.
- `ai-llm-engineer`, `backend-dev-cloud` — V1+ only; **don't invoke for V0 work** (no LLM, no full GCP).

## Things to leave alone unless explicitly asked

- The orchestrator stub (`orchestrator/`) and the LLM specs in `docs/futuro/` (`llm-ops.md`, `adr-0002-llm-sprint-1.md`, `llm/`, `compliance-zdr-anthropic-plan.md`) — preserved as V1+ playbooks.
- The whole `docs/futuro/` tree — historical / aspirational. Read it for context but don't operate against it.
- The JSON `schema_version: "1.0"` and the placeholder fields it carries (`inference_signature: null`, `audit_chain: null`, etc).
- The `--pdf` no-flag default (CLI must keep working without it).
- `hash_content` (don't switch to `hash_pdf`).
- Hard locks list (`LNTY-001`, `LNTY-002`, `LNTY-007`) and the 10% suppression cap — both are canon-defined.

## Authoritative references (V0)

- `docs/00-onde-estamos.md` — TL;DR, read first.
- `docs/01-product-vision.md` — V0 product model (lean, no LLM/PAdES/SOC 2).
- `docs/02-canon-v1.md` — the 9 rules, scoring, suppression rules.
- `docs/03-motor-cli.md` — engine spec for V0 (no Cloud Run / no LLM).
- `docs/09-golden-tests.md` — golden suite & determinism contract.
- `docs/12-sales-cut.md` — sales angle and demo script.
- `docs/13-web-inspector.md` — spec for the GitHub-URL → PDF flow.
- `docs/14-cli-distribution.md` — CLI release pipeline and download UX.
- `docs/15-roadmap-curto.md` — realistic 8–12 week plan.
- `docs/adr/0001-motor-skeleton.md` — JSON contract, exit codes, packages.
- `docs/adr/0003-pdf-reporter.md` — QuestPDF stack, layout, determinism strategy.
- `docs/adr/0005-distribution-model.md` — CLI Self-Service as default; Concierge as fallback.
- `docs/manual-actions.md` — the running list of human-only TODOs (logo, CNPJ, lawyer, demo repos).
