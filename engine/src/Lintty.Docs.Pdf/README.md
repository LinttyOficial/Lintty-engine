# Lintty.Docs.Pdf

White-label PDF template for Lintty institutional documents — ADRs, briefings, manuals, anything that's **not** the audit report.

For the audit report PDF, use `Lintty.Engine.Reporter` instead. They look different on purpose (see ADR 0004 §6).

## Identity at a glance

- **One accent color:** saint green `#0F4C3A` (matches favicon).
- **Type:** Inter (sans) + JetBrains Mono (mono), embedded TTFs, no system fallback.
- **Cover:** logo top-left, low-and-left title block, accent rule, meta strip footer.
- **Deterministic:** same input → same PDF, sha256 stable.

Full spec in [`docs/brand/pdf-identity.md`](../../../docs/brand/pdf-identity.md). Architectural decision in [`docs/adr/0004-brand-pdf-template.md`](../../../docs/adr/0004-brand-pdf-template.md).

## CLI usage

```bash
cd engine
dotnet run --project src/Lintty.Docs.Pdf -c Release -- cover \
  --title "Identidade Visual em PDF" \
  --subtitle "Template white-label para documentos do projeto Lintty." \
  --kicker "Brand · Documento de referência" \
  --doc-id "BRAND-COVER-SAMPLE" \
  --version "v0.1" \
  --date "2026-04-27" \
  --output ../docs/brand/samples/cover-sample.pdf
```

All flags except `--title` and `--output` are optional. Layout adapts.

## Programmatic usage

```csharp
using Lintty.Docs.Pdf;

var sha256 = new DocumentBuilder()
    .Cover("ADR 0004 — Brand PDF Template",
           subtitle: "Identidade visual consolidada.",
           kicker: "ADR · Decisão arquitetural")
    .WithDocumentId("ADR-0004").WithVersion("v0.1").WithDate("2026-04-27")
    .AddH1("Contexto")
    .AddParagraph("Antes deste ADR, cada documento institucional…")
    .AddBullets("Item 1", "Item 2", "Item 3")
    .AddCode("dotnet run --project src/Lintty.Docs.Pdf …")
    .AddCallout("Importante: regenerar a amostra ao mudar identidade.")
    .Build("adr-0004.pdf");
```

`Build()` returns the sha256 of the resulting file.

## Body primitives

`AddH1` `AddH2` `AddParagraph` `AddCode` `AddBullets` `AddCallout` `AddSpacer`. Extend only when a real document needs more — keep the surface small on purpose.

## Sample

`docs/brand/samples/cover-sample.pdf` is the canonical preview. Regenerate and commit it whenever you change anything in `Brand/` or `Layout/CoverPage.cs` — the sha256 delta is the visual changelog.

## Debug

When a layout breaks (`The provided document content contains conflicting size constraints`), set `LINTTY_DOCS_DEBUG=1` before running. QuestPDF will dump the failing element tree and point at the wrap reason.
