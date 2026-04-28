# Brand — Identidade visual para PDFs Lintty

> Documento de referência para qualquer PDF white-label gerado pelo projeto Lintty (documentação, ADRs, materiais internos, briefings). **Não** se aplica ao laudo técnico de auditoria — esse é um produto separado com paleta funcional própria (cores de severidade), spec em `docs/adr/0003-pdf-reporter.md`.

---

## 1. Conceito

Lintty é **árbitro técnico determinístico**. Os documentos do projeto refletem isso visualmente:

- **Clean.** Sem ornamento, sem ilustração decorativa, sem gradiente, sem sombra. Hierarquia tipográfica resolve o que figuras tentariam resolver.
- **Minimalista.** Uma cor de acento, máximo dois pesos por bloco, espaço em branco como elemento estrutural.
- **Editorial, não corporativo.** Capa low-and-left (texto ancorado no terço inferior), com o terço superior vazio. Inspiração: capas de Penguin Modern Classics, relatórios anuais de fundações, papers acadêmicos bem diagramados.
- **Confiança técnica.** Rodapé com identificadores em mono (doc-id, versão), sinaliza "isso aqui é versionado, é rastreável, não é improvisado".

---

## 2. A marca

A marca Lintty é um **hexágono entrelaçado em gradiente azul-marinho → teal**, com um "L" estilizado em motivo de circuito no centro, acompanhada da wordmark *Lintty* em navy e da tagline **ARCHITECTURE & CODE VERIFICATION** em uppercase com tracking. É a forma definitiva e substitui a versão anterior (selo preto + check verde do favicon).

Arquivo master: `engine/src/Lintty.Docs.Pdf/Resources/lintty-logo.png` (PNG embedded no projeto). Reprodução simplificada em vetor para tamanhos pequenos: `Brand/BrandMark.cs` (`SvgIconPayload`).

### 2.1. Conceito

Cada elemento carrega o produto:

- **Hexágono entrelaçado** — arquitetura hexagonal (Hexagonal/DDD). É o canon que o Lintty audita.
- **Motivo de circuito interno** — análise técnica, type-aware. Não é review humana, é máquina lendo a estrutura.
- **"L" no centro** — assinatura. Permite reconhecimento mesmo quando a marca aparece muito pequena.
- **Gradiente azul-marinho → teal** — confiabilidade institucional combinada com olhar técnico atual.

### 2.2. Variantes

| Variante                | Quando usar                                                                              | Como obter                                                              |
|-------------------------|------------------------------------------------------------------------------------------|-------------------------------------------------------------------------|
| **Logo completa (PNG)** | Capa de PDF, hero da landing, slide de abertura. Carrega ícone + wordmark + tagline.     | `BrandLogo.Compose(c, null, Variant.LogoPng)`                           |
| **Ícone vetor (SVG)**   | Headers de página, badges pequenos, favicon, qualquer contexto < 32 pt.                  | `BrandLogo.Compose(c, null, Variant.IconSvg)` ou `BrandMark.ComposeSvg` |
| **Wordmark texto**      | Ao lado do ícone em headers — "Lintty" em Inter Bold 11pt navy (mixed case, **não** all-caps). | Renderizado como `Text("Lintty")` em `BrandColors.Navy`                 |
| **Custom override**     | Quando um documento aceita logo de parceiro/cliente.                                     | `--logo path.png` no CLI ou `.WithLogo(path)` no builder                |

### 2.3. Tamanhos e área de respiro

| Contexto              | Variante     | Largura       | Notas                                                  |
|-----------------------|--------------|---------------|--------------------------------------------------------|
| Capa de PDF (header)  | Logo completa| ~280 pt       | Altura limitada a 64pt; PNG escala proporcional.       |
| Header de página      | Ícone + wordmark | Ícone 22×22 + "Lintty" 11pt navy | Sem tagline, sem ruído.       |
| Tamanho mínimo        | Ícone vetor  | **16 × 16 pt**| Abaixo disso, omitir o ícone (apenas wordmark).         |

**Área de respiro mínima:** uma "altura-x" do wordmark em todos os lados (≈ 50% do tamanho do ícone). Nada cola.

### 2.4. Antipatterns da marca

- ❌ Recolorir o gradiente — ele é parte da marca, não decoração.
- ❌ Usar a logo completa em tamanhos pequenos onde a tagline fica ilegível (use o ícone vetor + wordmark texto).
- ❌ Esticar ou distorcer o hexágono. Mantenha proporções.
- ❌ Aplicar drop-shadow, glow, embossing ou efeito 3D.
- ❌ Trocar a wordmark "Lintty" por all-caps "LINTTY" no contexto da marca — manter mixed case como no master. (All-caps é só pra kicker/tagline curtos.)
- ❌ Substituir o ícone hexagonal por outro símbolo. A geometria *é* a marca.
- ❌ Usar a marca sobre fundo escuro sem versão inversa específica — em V1+, criar uma master alternativa.

---

## 3. Paleta consolidada

Definida em `engine/src/Lintty.Docs.Pdf/Brand/BrandColors.cs`. Cores extraídas diretamente da logo master.

| Token            | Hex       | Uso                                                                          |
|------------------|-----------|------------------------------------------------------------------------------|
| `Navy`           | `#1B3A5F` | **Cor primária da marca.** Wordmark, títulos H1/H2, kicker, headers.         |
| `Accent`         | `#2D7A8C` | Teal médio (parada central do gradiente). Fios de seção, bullets, callouts. |
| `AccentSoft`     | `#E5F0F4` | Wash claro do accent — fundo de callout.                                     |
| `AccentLight`    | `#5FB8C9` | Cyan da circuitaria interna. Reserva para detalhes/highlights, não fills.   |
| `Ink`            | `#0A0A0A` | Texto longo (parágrafos, listas, código). Quase preto puro — legibilidade.   |
| `Paper`          | `#FFFFFF` | Fundo da página (branco puro — para impressão; landing usa `#FAFAF9`).        |
| `TextSecondary`  | `#4B5563` | Subtítulo, copy de apoio.                                                    |
| `TextMuted`      | `#6B7280` | Rodapé, captions, IDs no header.                                             |
| `LineFaint`      | `#E5E7EB` | Fios de separação.                                                           |
| `Surface`        | `#F8F8F7` | Fundo de bloco de código.                                                    |

**Hierarquia simples de uso:** Navy ≠ Accent ≠ Ink. Não trocar.
- *Brand elements* (wordmark, títulos da capa, H1, H2): Navy.
- *Marcações funcionais* (fios, bullets, callouts, kicker): Accent (teal).
- *Texto longo* (parágrafos, code, listas): Ink.

**Não usar:** vermelhos, âmbar, verde-saint. Os dois primeiros ficam reservados ao reporter de laudo (semântica de severidade); o saint green saiu da identidade quando a logo nova entrou.

### Por que navy + teal

- **Coerência com a marca master.** As cores existem na arte; a paleta funcional só extrai e nomeia.
- **Lê como técnico-institucional.** Navy comunica seriedade; teal sinaliza tecnologia/análise sem virar azul-genérico-de-startup.
- **Distância semântica do laudo.** O reporter usa vermelho/verde/âmbar pra severidade. Documentos brand em azul = leitor sabe na primeira folheada que está olhando para algo institucional, não um alerta.

---

## 4. Tipografia

Fontes embedded (mesmas do reporter de laudo, linkadas via `<EmbeddedResource>` cross-project para não duplicar TTFs).

| Família        | Pesos disponíveis | Uso                                                          |
|----------------|-------------------|--------------------------------------------------------------|
| **Inter**      | Regular, SemiBold, Bold | Toda copy: títulos, parágrafos, kickers, footer       |
| **JetBrains Mono** | Regular, Bold | Doc IDs, versões, números de página, snippets de código |

### Escala tipográfica (pt)

| Elemento          | Tamanho | Peso       | Cor              | Line height |
|-------------------|---------|------------|------------------|-------------|
| Cover title       | 38      | Bold       | Navy             | 1.10        |
| Cover subtitle    | 15      | Regular    | TextSecondary    | 1.45        |
| Cover kicker      | 10      | SemiBold   | Accent           | (uppercase, letter-spacing 0.22) |
| Wordmark "Lintty" (header) | 11 | Bold       | Navy             | mixed case  |
| H1 (corpo)        | 22      | Bold       | Navy             | default + accent rule abaixo |
| H2 (corpo)        | 15      | SemiBold   | Navy             | default     |
| Parágrafo         | 10.5    | Regular    | Ink              | 1.55        |
| Bullet item       | 10.5    | Regular    | Ink              | 1.50        |
| Code              | 9.5     | Mono Reg.  | Ink (sobre Surface) | 1.45     |
| Footer / meta     | 8.5     | Regular ou Mono | TextMuted   | default     |

**Regra:** nunca usar mais que 3 tamanhos diferentes em um bloco visual contínuo. Se precisar, é sinal de hierarquia mal pensada.

---

## 5. Layout de capa

Padrão A4, margens de 48pt em todos os lados. **A logo master (PNG) já carrega ícone + wordmark + tagline**, então o header da capa é só a imagem — sem texto adicional.

```
┌──────────────────────────────────────────────────┐
│  [LOGO MASTER PNG — ícone + Lintty + tagline]    │  <- header (~64pt altura)
│                                                  │
│                                                  │
│                                                  │  <- ~⅓ vazio (ancoragem visual)
│                                                  │
│                                                  │
│  BRAND · DOCUMENTO DE REFERÊNCIA                 │  <- kicker (opcional, Accent teal)
│                                                  │
│  Identidade Visual                               │  <- title (Bold 38, Navy)
│  em PDF                                          │
│  ──────                                          │  <- accent rule (1.5pt × 56pt, teal)
│                                                  │
│  Template white-label para documentos do         │  <- subtitle (Regular 15, TextSecondary)
│  projeto Lintty.                                 │
│                                                  │
│                                                  │
│ ─────────────────────────────────────────────── │  <- hairline
│  BRAND-SAMPLE  ·  v0.1  ·  2026-04-27  ·  …    │  <- meta strip (mono+sans, TextMuted)
└──────────────────────────────────────────────────┘
```

### Placeholders aceitos

| Placeholder | Tipo     | Comportamento se ausente                                       |
|-------------|----------|----------------------------------------------------------------|
| **Logo**    | PNG/JPG  | Renderiza monograma "L" branco em quadrado `Ink` (mesmo motif do favicon) |
| **Title**   | string   | **Obrigatório.** Build falha sem isso.                         |
| **Subtitle**| string   | Bloco simplesmente não renderiza.                              |
| **Kicker**  | string   | Bloco simplesmente não renderiza.                              |
| **Doc ID**  | string   | Não aparece na meta strip.                                     |
| **Version** | string   | Não aparece na meta strip.                                     |
| **Date**    | string (livre, ISO recomendado) | Não aparece na meta strip.                  |

**Determinismo:** o PDF é gerado com `CreationDate` e `ModifiedDate` fixados em `2000-01-01T00:00:00Z`. Mesmo input → mesmo binário (sha256 estável). Confirmado em duas execuções consecutivas (com a marca PNG embedded): `612bdaf65dd5c7bab65bcc96015f0d101daa22445bfe8fede5af88f06bec104b`.

---

## 6. Layout do "resto" do documento

A partir da página 2, o layout aceita uma sequência de `ContentBlock`s. Primitivas mínimas (extender só quando documento real exigir):

| Bloco       | Renderização                                                              |
|-------------|---------------------------------------------------------------------------|
| `H1`        | Inter Bold 22pt, Navy, com fio Accent (40×1.5pt, teal) abaixo             |
| `H2`        | Inter SemiBold 15pt, Navy                                                 |
| `Paragraph` | Inter Regular 10.5pt, Ink, line-height 1.55                               |
| `Code`      | JetBrains Mono 9.5pt sobre fundo `Surface`, padding 12pt                  |
| `Bullets`   | Lista com marcador `•` em Accent (teal)                                   |
| `Callout`   | Barra Accent (3pt) + bloco `AccentSoft` com texto Ink                     |
| `Spacer`    | Espaço vertical configurável                                              |

**Header da página de corpo:** ícone vetor (22×22) + "Lintty" (Bold 11pt, Navy, mixed case) à esquerda, doc-id (mono, TextMuted) à direita. Footer: title do documento à esquerda, "p. N" à direita.

Quando nenhum bloco é fornecido, a página 2 mostra um placeholder cinza centralizado: *"Conteúdo do documento — Inserido conforme o contexto de cada documento."* Isso comunica visualmente que o template existe e está esperando conteúdo.

---

## 7. Antipatterns

Evite, mesmo se "ficar bonito":

- ❌ Adicionar uma segunda cor de acento ("só dessa vez").
- ❌ Gradiente, sombra, glow, blur, drop-shadow no título ou em qualquer elemento que não seja a marca master (que **tem** gradiente — é parte dela).
- ❌ Ilustração decorativa (ícones flat coloridos, mascotes, padrões geométricos no fundo).
- ❌ Tipografia que não seja Inter ou JetBrains Mono. Se precisar de display, é sinal de que o documento perdeu o tom Lintty.
- ❌ Centralizar o título no centro vertical da capa. **Sempre low-and-left** (centro do terço inferior).
- ❌ Logo master gigante na capa (>120pt altura). Ela é a chancela, não a manchete.
- ❌ Datas auto-geradas em runtime sem flag `--date` explícita. Quebra determinismo.
- ❌ Misturar a paleta deste documento com a paleta de severidade do laudo (LinttyColors no Reporter — vermelho/verde/âmbar).

---

## 8. Como gerar

CLI standalone na solution `engine/Lintty.Engine.sln`:

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

Para gerar a amostra completa (capa + corpo com todos os primitivos):

```bash
dotnet run --project src/Lintty.Docs.Pdf -c Release -- sample \
  --output ../docs/brand/samples/cover-sample.pdf
```

API programática (para documentos reais com corpo customizado):

```csharp
using Lintty.Docs.Pdf;

var sha = new DocumentBuilder()
    .Cover("ADR 0004 — Brand PDF Template",
           subtitle: "Identidade visual consolidada e CLI white-label.",
           kicker: "ADR · Decisão arquitetural")
    .WithDocumentId("ADR-0004").WithVersion("v0.1").WithDate("2026-04-27")
    .AddH1("Contexto")
    .AddParagraph("Antes deste ADR, cada documento institucional do Lintty era criado…")
    .AddH2("Decisão")
    .AddBullets(
        "Projeto novo Lintty.Docs.Pdf, isolado do reporter de laudo.",
        "Paleta navy + teal extraída da logo master.",
        "Determinismo bit-a-bit garantido por timestamps fixos.")
    .AddCode("dotnet run --project src/Lintty.Docs.Pdf -- cover --title …")
    .AddCallout("Não confundir com a paleta do laudo — propósitos diferentes.")
    .Build("adr-0004.pdf");
```

Spec arquitetural completa em `docs/adr/0004-brand-pdf-template.md`.

---

## 9. Amostra renderizada

`docs/brand/samples/cover-sample.pdf` — gerado pelo subcomando `sample`. Use como referência visual ao discutir mudanças de identidade.

Se mudar qualquer coisa em `engine/src/Lintty.Docs.Pdf/Brand/` ou `Layout/CoverPage.cs`, **regenere a amostra** e commite junto. O hash sha256 muda, e isso é a evidência de que a identidade evoluiu intencionalmente.
