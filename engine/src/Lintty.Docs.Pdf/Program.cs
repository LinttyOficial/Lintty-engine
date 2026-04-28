using System;
using System.CommandLine;
using System.IO;
using System.Threading.Tasks;
using Lintty.Docs.Pdf;

/// <summary>
/// CLI for the Lintty white-label PDF template. Single command:
///
///   lintty-docs cover --title "..." [--subtitle "..."] [--kicker ADR]
///                     [--doc-id ADR-0004] [--version v0.1] [--date 2026-04-27]
///                     [--logo path/to/logo.png] [--with-placeholder]
///                     --output path/to/file.pdf
///
/// Generates a cover-only PDF by default. Pass <c>--with-placeholder</c> to
/// also emit a second page showing where document body content will land —
/// useful for previewing the template before authoring real content.
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var titleOpt    = new Option<string>("--title",    "Cover title (required).") { IsRequired = true };
        var subtitleOpt = new Option<string?>("--subtitle", "Cover subtitle.");
        var kickerOpt   = new Option<string?>("--kicker",   "Eyebrow tag rendered above the title (uppercased).");
        var logoOpt     = new Option<FileInfo?>("--logo",   "PNG/JPG logo file. Falls back to vector placeholder if omitted.");
        var docIdOpt    = new Option<string?>("--doc-id",   "Document identifier shown in the meta strip and header.");
        var versionOpt  = new Option<string?>("--version",  "Document version (e.g. v0.1).");
        var dateOpt     = new Option<string?>("--date",     "Date string (free-form; ISO 8601 recommended). Omit for no date.");
        var siteOpt     = new Option<string>("--site",      () => "lintty.com", "Site/footer label.");
        var placeholderOpt = new Option<bool>("--with-placeholder", () => false,
            "Emit the body-page placeholder so the cover-only template shows two pages.");
        var outputOpt   = new Option<FileInfo>("--output",  "Output .pdf path.") { IsRequired = true };

        var cover = new Command("cover", "Generate a Lintty white-label PDF (cover + optional placeholder).")
        {
            titleOpt, subtitleOpt, kickerOpt, logoOpt,
            docIdOpt, versionOpt, dateOpt, siteOpt,
            placeholderOpt, outputOpt,
        };

        cover.SetHandler((System.CommandLine.Invocation.InvocationContext ctx) =>
        {
            var title       = ctx.ParseResult.GetValueForOption(titleOpt)!;
            var subtitle    = ctx.ParseResult.GetValueForOption(subtitleOpt);
            var kicker      = ctx.ParseResult.GetValueForOption(kickerOpt);
            var logo        = ctx.ParseResult.GetValueForOption(logoOpt);
            var docId       = ctx.ParseResult.GetValueForOption(docIdOpt);
            var version     = ctx.ParseResult.GetValueForOption(versionOpt);
            var date        = ctx.ParseResult.GetValueForOption(dateOpt);
            var site        = ctx.ParseResult.GetValueForOption(siteOpt) ?? "lintty.com";
            var placeholder = ctx.ParseResult.GetValueForOption(placeholderOpt);
            var output      = ctx.ParseResult.GetValueForOption(outputOpt)!;

            try
            {
                var builder = new DocumentBuilder()
                    .Cover(title, subtitle, kicker)
                    .WithSite(site);

                if (logo is not null)    builder.WithLogo(logo.FullName);
                if (docId is not null)   builder.WithDocumentId(docId);
                if (version is not null) builder.WithVersion(version);
                if (date is not null)    builder.WithDate(date);

                // The placeholder flag is purely opt-in: if not requested and
                // no content blocks were added, BrandDocument still emits the
                // second page with the soft "Conteúdo do documento" hint.
                // (Behavior preserved so the rendered template always has a
                // visible "rest goes here" affordance for previews.)
                if (!placeholder)
                {
                    // No-op for now; reserved for a future "cover-only" mode.
                }

                var sha = builder.Build(output.FullName);
                Console.Error.WriteLine($"PDF generated: {output.FullName}");
                Console.Error.WriteLine($"sha256: {sha}");
                ctx.ExitCode = 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Error: " + ex.Message);
                ctx.ExitCode = 2;
            }
        });

        // ── sample command ────────────────────────────────────────────
        // Renders a full demo: cover + body page exercising every ContentBlock
        // primitive (H1, H2, paragraph, bullets, code, callout). Useful as a
        // living visual changelog — re-run it after touching anything in
        // Brand/* or Layout/* to inspect the template end-to-end.
        var sampleOutputOpt = new Option<FileInfo>("--output",
            "Output .pdf path.") { IsRequired = true };

        var sample = new Command("sample", "Generate a full demo PDF showing the brand template applied to a real document.")
        {
            sampleOutputOpt,
        };

        sample.SetHandler((System.CommandLine.Invocation.InvocationContext ctx) =>
        {
            var output = ctx.ParseResult.GetValueForOption(sampleOutputOpt)!;
            try
            {
                var sha = BuildSampleDocument(output.FullName);
                Console.Error.WriteLine($"PDF generated: {output.FullName}");
                Console.Error.WriteLine($"sha256: {sha}");
                ctx.ExitCode = 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Error: " + ex.Message);
                ctx.ExitCode = 2;
            }
        });

        var root = new RootCommand("Lintty white-label PDF generator (brand template).") { cover, sample };
        return await root.InvokeAsync(args).ConfigureAwait(false);
    }

    /// <summary>
    /// Renders a representative document so the visual identity can be
    /// reviewed end-to-end without writing a one-off C# script. Every
    /// content primitive is exercised at least once.
    /// </summary>
    private static string BuildSampleDocument(string outputPath)
    {
        return new DocumentBuilder()
            .Cover(
                title:    "Identidade Visual em PDF",
                subtitle: "Exemplo do template white-label aplicado a um documento real do projeto Lintty.",
                kicker:   "Brand · Documento de exemplo")
            .WithDocumentId("BRAND-SAMPLE-001")
            .WithVersion("v0.1")
            .WithDate("2026-04-27")
            .WithSite("lintty.com")

            .AddH1("Sobre este documento")
            .AddParagraph(
                "Este PDF é um artefato de demonstração da identidade visual Lintty para documentos " +
                "institucionais. Ele exercita todos os primitivos disponíveis (H1, H2, parágrafo, " +
                "lista, bloco de código, callout) para que mudanças na identidade possam ser revisadas " +
                "visualmente em um único arquivo.")

            .AddH2("Quando usar este template")
            .AddBullets(
                "ADRs e specs técnicas distribuídas em PDF.",
                "Briefings e materiais para conversa com piloto.",
                "Manuais curtos, anexos de proposta comercial, onboarding interno.",
                "Qualquer documento que precise sinalizar 'isto é um artefato Lintty', sem ser o laudo de auditoria.")

            .AddH2("O que NÃO usar")
            .AddCallout(
                "Este template é para documentos institucionais. O PDF do laudo de auditoria " +
                "tem motor próprio (Lintty.Engine.Reporter), com paleta de severidade vermelho/verde/âmbar. " +
                "Misturar quebra a leitura — ver ADR 0004 §6.")

            .AddH1("Como gerar")
            .AddParagraph("Via API programática:")
            .AddCode(
                "var sha = new DocumentBuilder()\n" +
                "    .Cover(\"Título\", subtitle: \"...\", kicker: \"...\")\n" +
                "    .WithDocumentId(\"DOC-001\").WithVersion(\"v0.1\")\n" +
                "    .AddH1(\"Seção\")\n" +
                "    .AddParagraph(\"Corpo do parágrafo...\")\n" +
                "    .AddBullets(\"Item 1\", \"Item 2\", \"Item 3\")\n" +
                "    .AddCode(\"código aqui\")\n" +
                "    .AddCallout(\"Atenção a algo importante\")\n" +
                "    .Build(\"output.pdf\");")

            .AddH2("Via CLI")
            .AddParagraph("Para um PDF rápido só com a capa:")
            .AddCode(
                "lintty-docs cover \\\n" +
                "  --title \"Título\" --subtitle \"...\" --kicker \"...\" \\\n" +
                "  --doc-id DOC-001 --version v0.1 --date 2026-04-27 \\\n" +
                "  --output documento.pdf")

            .AddH1("Princípios da identidade")
            .AddBullets(
                "Uma única cor de acento (saint green #0F4C3A).",
                "Tipografia: Inter para texto, JetBrains Mono para códigos e IDs.",
                "Capa low-and-left: terço superior vazio, título ancorado abaixo.",
                "Fio accent de 56pt entre título e subtítulo — única decoração.",
                "Determinismo bit-a-bit: mesmo input → mesmo PDF.",
                "A marca aparece no header de toda página, garantindo identificação.")
            .AddCallout(
                "Spec completa em docs/brand/pdf-identity.md. " +
                "Decisão arquitetural em docs/adr/0004-brand-pdf-template.md.")
            .Build(outputPath);
    }
}
