# ADR 0004 — White-label PDF template (`Lintty.Docs.Pdf`)

- **Status:** Accepted
- **Date:** 2026-04-27
- **Author:** founder + Claude Code
- **Audience:** anyone gerando documento Lintty em PDF (founder, devs, agência futura)
- **Substitui:** nada — primeiro template white-label do projeto.
- **Não confunde com:** ADR 0003 (PDF do laudo de auditoria — propósito e paleta diferentes; ver §6).

## 1. Contexto

Conforme o Sales Cut avança (Tier 1, semana 3+), surgem documentos institucionais do projeto que merecem distribuição em PDF: ADRs, briefings para piloto, anexos de proposta comercial, manuais técnicos curtos, materiais de onboarding. Antes desta ADR, cada um seria diagramado ad-hoc (Word, Google Docs, exportação) — sem identidade visual coerente, sem rastreabilidade de versão, sem garantia de que dois autores produziriam algo parecido.

O laudo de auditoria (`Lintty.Engine.Reporter`) já estabeleceu padrões válidos: QuestPDF, fontes embedded, timestamps fixos, determinismo bit-a-bit. Mas seu layout é **funcional ao laudo** (selo grande, cores de severidade, tabela de violações) — não serve para documentos genéricos.

Esta ADR cria um **segundo motor de PDF** dedicado ao white-label institucional, reaproveitando a stack mas com identidade própria.

## 2. Decisão

### 2.1. Novo projeto `engine/src/Lintty.Docs.Pdf/`

- Standalone .NET 8, output `Exe` (CLI `lintty-docs`), também consumível como library.
- Sem `ProjectReference` para `Lintty.Engine.Core` ou `Lintty.Engine.Reporter` — independente do motor de análise. Único cross-reference é `<EmbeddedResource Include="..\Lintty.Engine.Reporter\Resources\*.ttf" Link="…" />` para reaproveitar TTFs (~600 KB) sem duplicar bytes em git.
- Adicionado à `Lintty.Engine.sln`.

### 2.2. Identidade visual — três pilares

1. **Uma cor de acento.** Saint green `#0F4C3A` (mesmo verde do favicon). Tudo o mais é neutro (`Ink #0A0A0A`, `Paper #FFFFFF`, escala de cinzas). Detalhes em `docs/brand/pdf-identity.md` §2.
2. **Tipografia consolidada.** Inter (3 pesos) + JetBrains Mono (2 pesos). Mesmas TTFs do reporter de laudo. Sem fallback de sistema — falha-fast se um asset não carregar (mesma política do `EmbeddedFonts` do Reporter).
3. **Capa editorial low-and-left.** Header com logo + wordmark. Bloco central de título ancorado no terço inferior (não centralizado verticalmente). Fio accent de 56pt × 1.5pt entre título e subtítulo como única "decoração". Meta strip no rodapé com doc-id (mono), versão, data, site separados por dot.

Layout completo (ASCII + tabela de tipos) em `docs/brand/pdf-identity.md` §4.

### 2.3. API — `DocumentBuilder` fluente

```csharp
new DocumentBuilder()
    .Cover(title, subtitle?, kicker?)
    .WithLogo(path?).WithDocumentId(...).WithVersion(...).WithDate(...)
    .AddH1(...).AddParagraph(...).AddCode(...).AddBullets(...).AddCallout(...)
    .Build(outputPath)  // returns sha256 of the produced binary
```

Body é uma `IReadOnlyList<ContentBlock>` — discriminated union mínima (`H1`, `H2`, `Paragraph`, `Code`, `Bullets`, `Callout`, `Spacer`). **Não implementamos parser Markdown agora** — adiciona complexidade pra um caso que ainda não existe. Se 3+ documentos precisarem de Markdown, então vale.

Quando o body fica vazio, a página 2 renderiza um placeholder *"Conteúdo do documento — Inserido conforme o contexto de cada documento."* — comunica visualmente que o template está pronto, esperando contexto.

### 2.4. CLI standalone

```
lintty-docs cover --title "..." [--subtitle "..."] [--kicker "..."] [--logo file.png]
                  [--doc-id ...] [--version v0.1] [--date 2026-04-27] [--site lintty.com]
                  --output file.pdf
```

Único subcomando agora (`cover`). Adicionar `compose` (lê JSON com a lista de blocks e gera) ou `from-markdown` quando documento real exigir.

### 2.5. Determinismo

Mesma política do reporter de laudo (ADR 0003 §5):

- `CreationDate` / `ModifiedDate` fixados em `2000-01-01T00:00:00Z`.
- `InvariantGlobalization=true` no csproj.
- Fontes embedded, sem fallback.
- `Date` no rodapé é input do CLI (ou null) — nunca `DateTime.Now`. Quem chama controla.

**Verificação:** rodar duas vezes com mesmos args e comparar sha256. Confirmado em 2026-04-27 (`282b6c40…ae46b` em ambas as execuções).

Este ADR **não** adiciona um `DeterminismTests.cs` por enquanto (escopo é template, não suite de regressão); se 2+ documentos institucionais reais forem distribuídos, vale criar o gate como existe no Reporter.

## 3. Arquivos criados

```
engine/src/Lintty.Docs.Pdf/
  Lintty.Docs.Pdf.csproj          # Exe, Inter+Mono linked from Reporter
  Program.cs                       # CLI entrypoint (System.CommandLine)
  DocumentBuilder.cs               # Fluent API, returns sha256
  Brand/
    BrandColors.cs                 # palette constants
    BrandFonts.cs                  # font registration + embedded loader
    BrandLogo.cs                   # vector "L" placeholder OR PNG/JPG image
  Layout/
    CoverContent.cs                # record (placeholders)
    ContentBlock.cs                # discriminated union (body primitives)
    BrandDocument.cs               # IDocument (cover + content page)
    CoverPage.cs                   # header / content / footer slots
    ContentPage.cs                 # body composer (handles empty placeholder)
    FooterStrip.cs                 # cover meta strip + per-page footer

docs/brand/
  pdf-identity.md                 # this identity spec
  samples/
    cover-sample.pdf              # rendered sample (regenerate on changes)

docs/adr/
  0004-brand-pdf-template.md      # this ADR
```

## 4. Não-objetivos (rejeitados deliberadamente)

| Item                                    | Por quê                                                                 |
|-----------------------------------------|-------------------------------------------------------------------------|
| Parser Markdown → blocks                | Sem usuário concreto. Adiciona dep externa (Markdig). Aguarda sinal.    |
| Tema configurável (paleta secundária)    | Quebra a coerência. White-label aqui significa "logo opcional", não "marca substituível". |
| PAdES / TSA signing                     | É problema do laudo (V1+). Documentos institucionais não precisam.      |
| Determinism gate em CI                  | Aguarda primeiro documento real. Por ora valida-se com `dotnet run` 2x. |
| Logo SVG nativo (parsing)               | QuestPDF não suporta SVG sem dep extra. PNG cobre 99% dos casos.        |
| Multi-idioma (PT/EN)                    | PT-BR only no Sales Cut, igual à landing.                               |

## 5. Como evoluir

Mudanças na identidade visual seguem este fluxo:

1. PR altera `Brand/*` ou `Layout/CoverPage.cs`.
2. **Regerar `docs/brand/samples/cover-sample.pdf`** com o comando da `pdf-identity.md` §7.
3. Commitar a amostra junto. O sha256 mudará — isso documenta a mudança visual no histórico do git.
4. Se a mudança afeta documentos já distribuídos, mencionar no PR (não é um produto sob contrato; lifecycle é light).

Adicionar nova primitiva ao body (ex: `Image`, `Table`) é OK quando documento real precisar. Manter cada novo tipo coberto pela paleta única — sem introduzir nova cor.

## 6. Por que não estender o Reporter de laudo

Pareceria mais simples reutilizar `Lintty.Engine.Reporter` adicionando "tema light" ou "modo documento". Rejeitado:

- O Reporter é **product-coupled**: layouts foram desenhados para o conteúdo do laudo (selo grande, score colorido, tabela de violações). Genericizá-lo introduziria abstração e bagunçaria a leitura do código que tem responsabilidade clara.
- Paletas conflitam por design: o laudo usa vermelho/verde semanticamente (`GradeFRed`, `GradeAGreen`, `WarnAmber`). Documento institucional usa só accent green. Misturar quebra a leitura para o leitor.
- Templates separados permitem evoluir cada um independentemente. ADR 0003 já prevê assinatura PAdES e TSA no laudo (V1+) — features que não fazem sentido em documento institucional.

A duplicação é mínima (BrandColors ≠ LinttyColors, BrandFonts ≠ EmbeddedFonts — mesmo padrão, namespaces e propósitos diferentes) e o reuso do que importa (TTFs) está garantido via `<EmbeddedResource Link>`.

## 7. Pontos abertos

- [ ] Substituir o monograma "L" placeholder pelo logo definitivo quando estiver pronto (`docs/manual-actions.md` 🟡 já lista isso).
- [ ] Decidir se vale criar `lintty-docs compose --from blocks.json` para documentos sem código C# — depende do primeiro consumidor real.
- [ ] Avaliar se o PR de algum documento institucional concreto pede primitiva nova (ex: `Image` para diagrama, `Table` para comparativo) — extender só quando.
