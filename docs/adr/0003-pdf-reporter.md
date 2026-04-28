# ADR 0003 — PDF Reporter (Sprint 1, pivô Zero-IA)

- **Status:** Proposed
- **Date:** 2026-04-27
- **Author:** software-architect
- **Audience:** backend-dev-dotnet (implementador), tech-writer-sales (consumidor do PDF para deck), qa-engineer (golden tests)
- **Sprint:** 1 — Sales Cut Tier 1 (`docs/12-sales-cut.md` reformulado §2.2 e §7), semana 3
- **Canon pinado:** `1.0.0` (`docs/02-canon-v1.md`)
- **Substitui:** o slot que era do "Sprint 1 LLM" antes do pivô Zero-IA em 2026-04-27. ADR 0002 fica preservado mas em STATUS V1+ ROADMAP.
- **Pré-requisitos atendidos:** Sprint 0 verde (motor `engine/` com 7 analyzers Roslyn determinísticos, JSON output validado, 8/8 testes — `docs/adr/0001-motor-skeleton.md` mergeado).

## 1. Contexto

Com o pivô Zero-IA do Sales Cut (2026-04-27), o argumento de venda passa a ser **"100% determinístico, zero alucinação, zero custo de inferência"**. Para sustentar isso na demo, o **PDF do laudo precisa ser artefato vivo do motor** — não imagem estática exportada de Word/Figma como era no plano anterior.

Sai do PDF a `inference_signature` (não há LLM) e entram dois argumentos defensáveis:

1. **Determinismo bit-a-bit:** mesmo input JSON → mesmo PDF binário, sempre. Demonstrado em demo via `sha256sum laudo.pdf` rodado duas vezes.
2. **Geração local:** sem dependência externa (Anthropic, GCP) no Sales Cut. CTO de banco verifica o motor offline.

Esta ADR escolhe a stack do reporter, define o layout, e define a estratégia de determinismo.

## 2. Decisão de stack

**Escolha:** **QuestPDF** (`QuestPDF` no NuGet, versão 2024.x).

### 2.1 Justificativa (5 pontos)

1. **.NET-nativo:** integra como projeto novo `Lintty.Engine.Reporter` na mesma solution `Lintty.Engine.sln`. Sem necessidade de processo separado, IPC, ou linguagem extra. Mantém Sales Cut em **uma stack só**.
2. **API fluente declarativa:** layout em C# puro (`document.Page().Content().Column(...)`) é programaticamente verificável e versionável. Mudança de layout vira diff git revisável.
3. **Fontes embedded suportadas:** QuestPDF aceita carregar TTF/OTF de stream e embedar no PDF. Garante determinismo independente do leitor (sem fallback para Helvetica do sistema).
4. **Licença:** Community License (MIT-like) é **gratuita para empresas com receita anual ≤ US$ 1M**. Lintty pré-receita está coberto. Se Sales Cut converter, troca para Professional License (US$ 699/dev/ano) — custo previsível, não bloqueia validação.
5. **Manutenção solo:** founder já tem fluência em C#/.NET. Adicionar Typst, Pandoc + LaTeX, ou wkhtmltopdf significa: terceira linguagem, dependência externa, ou tooling frágil. QuestPDF zera tudo isso.

### 2.2 Alternativas avaliadas (e por que não)

| Alternativa | Por que NÃO no Sales Cut |
|-------------|--------------------------|
| **Typst** (CLI, MIT) | PDF mais bonito, mas exige binário Typst no PATH + invocação por subprocess + parsing de erros. Adiciona dependência operacional. Volta como candidato em V1+ se layout do QuestPDF ficar limitante. |
| **Pandoc + LaTeX** | Pesado (~1GB de TeX Live), lento, frágil em Windows. Vetado. |
| **wkhtmltopdf** | HTML→PDF parece ergonômico mas é deprecated upstream desde 2023; renderização inconsistente entre versões. Determinismo bit-a-bit é frágil. Vetado. |
| **iText** (.NET) | Licença AGPL ou comercial cara (US$ 5k+/ano). Vetado por custo. |
| **PdfSharp / MigraDoc** | API datada, sem suporte fluente moderno, comunidade pequena. Funciona mas é menos ergonômico que QuestPDF. Fallback se QuestPDF licensing virar problema. |
| **Word/Figma exportado manualmente** | Era o plano antes do pivô. Quebra o argumento "PDF é artefato vivo do motor". Vetado. |

**Decisão revisitável em V1** se:
- QuestPDF Professional License virar carga inviável (improvável até receita anual >US$ 1M).
- Layout exigir tipografia que QuestPDF não suporta bem (improvável — Inter + JetBrains Mono cobrem o caso).

## 3. Arquitetura

### 3.1 Novo projeto na solution

```
engine/
├── Lintty.Engine.sln
├── src/
│   ├── Lintty.Engine.Core/              # já existe (Sprint 0)
│   ├── Lintty.Engine.Reporter/          # NOVO — Sprint 1
│   │   ├── Lintty.Engine.Reporter.csproj
│   │   ├── PdfReporter.cs               # entry point: JSON → PDF
│   │   ├── Layout/
│   │   │   ├── CoverPage.cs             # capa com score F gigante
│   │   │   ├── ExecutiveSummary.cs
│   │   │   ├── ExceptionsSummary.cs     # @lintty-ignore válidos
│   │   │   ├── ViolationsList.cs
│   │   │   ├── DependencyGraph.cs       # grafo ASCII fallback no Sales Cut
│   │   │   └── TechnicalFooter.cs
│   │   ├── Theming/
│   │   │   ├── LinttyColors.cs
│   │   │   └── EmbeddedFonts.cs         # carrega Inter + JetBrains Mono de Resources
│   │   └── Resources/
│   │       ├── Inter-Regular.ttf
│   │       ├── Inter-SemiBold.ttf
│   │       ├── Inter-Bold.ttf
│   │       ├── JetBrainsMono-Regular.ttf
│   │       └── JetBrainsMono-Bold.ttf
│   └── Lintty.Engine.Cli/               # já existe — adiciona flag --pdf
└── tests/
    └── Lintty.Engine.Reporter.Tests/    # NOVO
        ├── DeterminismTests.cs          # gera 2x e compara sha256
        ├── LayoutSnapshotTests.cs       # opcional — golden PDFs em git LFS
        └── SmokeTests.cs                # gera Saint/Sinner/Ninja, valida não-vazio
```

### 3.2 Interface pública

```csharp
namespace Lintty.Engine.Reporter;

public interface IReporter
{
    /// <summary>
    /// Gera o PDF do laudo a partir do JSON do motor (schema do ADR 0001 §3).
    /// Determinístico: mesma entrada → mesmo binário, sempre.
    /// </summary>
    /// <param name="reportJson">JSON serializado do ReportSchema.</param>
    /// <param name="outputPath">Caminho do arquivo .pdf de saída.</param>
    /// <returns>Hash sha256 do PDF gerado.</returns>
    string GeneratePdf(string reportJson, string outputPath);
}

public sealed class PdfReporter : IReporter { /* QuestPDF impl */ }
```

### 3.3 Integração com CLI

`Lintty.Engine.Cli/Program.cs` ganha flag `--pdf <path>`:

```bash
lintty-engine analyze --solution Sinner.sln --pdf laudo-sinner.pdf
# Output:
#   JSON em stdout (como já era)
#   PDF gerado em laudo-sinner.pdf
#   Linha extra em stderr: "PDF generated: laudo-sinner.pdf (sha256: 7f3a...)"
```

Sem `--pdf`, comportamento atual preservado (só JSON em stdout). Não quebra usos existentes.

## 4. Layout do PDF

### 4.1 Páginas

| Página | Conteúdo | Origem dos dados |
|--------|----------|------------------|
| 1 (capa) | Score gigante (A-F), "SELO NÃO EMITIDO" se F, projeto, milestone, run_id | `report.score`, `report.grade`, `report.run_id` |
| 1 (continuação) | Sumário executivo (parágrafo curto auto-gerado a partir das contagens) | derivado de `report.violations[]` + `report.layer_summary` |
| 1 (continuação) | Sumário de Exceções (tabela: rule_id, file:line, autor git, justificativa) | `report.exceptions[]` |
| 2+ | Lista de Violações detectadas (1 bloco por violação, com snippet de código) | `report.violations[]` |
| N-1 | Grafo de dependências (alto nível, ASCII fallback no Sales Cut) | derivado pelo motor a partir do tagging de layers |
| Todas (rodapé) | `canon_version`, `run_id`, `hash_pdf` (gerado em segunda passagem), `inference_signature: null`, `generated_by: lintty-engine X.Y.Z` | `report.*` |

Conteúdo detalhado em `docs/sales/laudo-mock.md` (reformulado para ser spec, não mock).

### 4.2 Tipografia

- **Sans-serif:** Inter (Regular, SemiBold, Bold) — TTF embedded como Resource embedded no .csproj.
- **Mono:** JetBrains Mono (Regular, Bold) — para snippets de código e para campos técnicos no rodapé (`run_id`, `hash_pdf`).
- Tamanho base 10pt, headings 16/14/12pt, footer 8pt.
- **Sem fallback para fonte do sistema.** Se a fonte falhar ao carregar, motor aborta com erro explícito (não mascara substituição silenciosa).

### 4.3 Paleta

- Cinza neutro: `#1F2937` (texto), `#6B7280` (secondary), `#E5E7EB` (linhas finas).
- Vermelho: `#B91C1C` (F, hard locks, snippets de linha violadora).
- Verde: `#15803D` (apenas para A em laudos Saint).
- Fundo: branco (#FFFFFF). Sem gradientes.

### 4.4 Brand frame (logo + tokens institucionais)

Decisão posterior ao corpo original do ADR: a capa do laudo e os headers internos carregam a **marca master Lintty** (hexágono entrelaçado + wordmark + tagline) como chancela institucional. Isso resolve duas tensões:

1. CTO/comprador precisa identificar **quem emitiu** o laudo na primeira folheada.
2. A paleta de severidade (vermelho/verde/âmbar) é semântica e não pode ser substituída por brand colors — caso contrário, a leitura do veredito perde força.

**Resolução:**

- **Logo PNG é o único elemento brand permitido na capa.** Renderizada no canto superior esquerdo a 44pt de altura. *Não centralizar, não amplificar*; ela é assinatura, não manchete (anti-pattern §2.4 de `docs/brand/pdf-identity.md`).
- **Asset compartilhado**, não duplicado: `engine/src/Lintty.Engine.Reporter/Lintty.Engine.Reporter.csproj` linka `..\Lintty.Docs.Pdf\Resources\lintty-logo.png` via `<EmbeddedResource Link="..." LogicalName="Lintty.Engine.Reporter.Resources.lintty-logo.png" />` — mesmo padrão que ADR 0004 §2.1 usa para as TTFs. Carregamento via `Theming/EmbeddedLogo.LoadPngBytes()` com fail-fast (sem fallback silencioso).
- **Brand-frame tokens** acrescentados a `Theming/LinttyColors`:
  - `BrandNavy = #1B3A5F` — kicker da capa, wordmark do header de página interna.
  - `BrandAccent = #2D7A8C` — fio decorativo único (56pt × 1.5pt) entre kicker e grade gigante na capa.
  Constraint codificado em comentário: **estes tokens NÃO são usados em grade letter, seal, severity badges, snippets, ou qualquer elemento semântico.** Severidade fica no `GradeFRed` / `GradeAGreen` / `WarnAmber`.
- **Header das páginas 2+** (`Layout/PageHeader.cs`): wordmark "Lintty" (Inter Bold 11pt BrandNavy) à esquerda, `run_id` em mono `TextSecondary` à direita, hairline `LineFaint` abaixo. **Não** renderiza a logo PNG nas páginas internas — a 22pt o conjunto ícone+wordmark+tagline ficaria ilegível (anti-pattern §2.4 da brand book). Quando houver um asset SVG icon-only no Reporter, isso pode evoluir.
- **Determinismo preservado:** PNG embedado é byte-estável; render do QuestPDF a partir de uma stream de manifest é determinístico. `DeterminismTests` ficou verde após a mudança (sha256 do PDF é diferente da versão pré-logo, mas estável entre execuções com o mesmo input — que é a invariante).

Anti-patterns (vetados):

- ❌ Logo gigante centralizada na capa.
- ❌ Recolorir wordmark ou hexágono.
- ❌ Drop shadow, glow, embossing na logo.
- ❌ Misturar `BrandNavy` / `BrandAccent` com elementos de severidade — quebra a leitura semântica do laudo.
- ❌ Carregar PNG do filesystem em runtime — embedded only.

## 5. Determinismo bit-a-bit — exigência crítica

O argumento de venda do pivô é "mesmo input → mesmo PDF, sempre". Implementação precisa garantir isso ou perdemos o pitch.

### 5.1 Fontes da não-determinismo a eliminar

| Fonte | Mitigação |
|-------|-----------|
| Timestamps no metadata do PDF | QuestPDF expõe `Settings.DocumentInfo`. Definir `CreationDate` e `ModificationDate` para data fixa (ex: `2000-01-01T00:00:00Z`) ou derivar de `report.run_id` para que JSON com mesmo `run_id` produza mesmo PDF. |
| Ordem de iteração em coleções | Iterar sempre por chave ordenada (já feito no JSON do motor). Reporter não introduz nova ordenação. |
| Fontes do sistema | Sempre usar fonte embedded — falha-fast se TTF de Resource não carregar. |
| Random IDs em PDF (object stream IDs) | QuestPDF gera IDs sequenciais determinísticos por padrão. Validar com snapshot test (§7.2). |
| Compressão | QuestPDF usa zlib determinístico. Se versão do .NET runtime mudar a impl de zlib, sha256 muda. **Pinar `<TargetFramework>net8.0</TargetFramework>` no .csproj** + documentar versão exata em CI. |
| Locale (formatação de números) | Usar `CultureInfo.InvariantCulture` em todo string formatting do reporter. |

### 5.2 Hash sha256 do PDF (rodapé)

O rodapé exibe `hash_pdf: sha256:...` — mas o hash depende do PDF inteiro, criando referência circular. Solução em duas passagens:

1. **Passagem 1:** gera PDF com placeholder `hash_pdf: sha256:[PENDING]`.
2. **Passagem 2:** calcula sha256 do binário da passagem 1, e regenera o PDF substituindo o placeholder pelo hash real.
3. **Auto-verificação:** após passagem 2, recalcula o hash do PDF final. **Não fechará bit-a-bit** (o conteúdo do rodapé mudou). Solução aceita:
   - Opção A (simples): rodapé carrega `hash_pdf: pre-finalize` da passagem 1; o hash "verdadeiro" é distribuído junto em arquivo `.sha256` separado.
   - **Opção B escolhida:** o rodapé carrega `hash_content: sha256(report_json)` em vez de `hash_pdf`. Hash do conteúdo factual, não do binário. Reproduzível trivialmente: `sha256sum report.json`. PDF binário continua determinístico, mas o hash impresso refere-se ao **input**, não ao **output**.

### 5.3 Teste de determinismo (gate de CI)

```csharp
[Fact]
public void Pdf_Generation_Is_Deterministic()
{
    var json = File.ReadAllText("fixtures/the-sinner/expected.json");
    var path1 = Path.GetTempFileName() + ".pdf";
    var path2 = Path.GetTempFileName() + ".pdf";

    new PdfReporter().GeneratePdf(json, path1);
    new PdfReporter().GeneratePdf(json, path2);

    var hash1 = ComputeSha256(path1);
    var hash2 = ComputeSha256(path2);

    Assert.Equal(hash1, hash2); // BIT-A-BIT
}
```

CI fail-fast se quebrar. **Esse teste é a defesa do argumento de venda — não pode ficar amarelo.**

## 6. CLI usage e backward compat

```bash
# Comportamento atual preservado (sem --pdf)
lintty-engine analyze --solution Saint.sln
# → JSON em stdout

# NOVO: gera PDF junto
lintty-engine analyze --solution Sinner.sln --pdf laudo.pdf
# → JSON em stdout
# → PDF em laudo.pdf
# → stderr: "PDF generated: laudo.pdf (hash_content: sha256:7f3a...)"

# Com --output suprimindo JSON em stdout (também já existente)
lintty-engine analyze --solution Sinner.sln --output silent --pdf laudo.pdf
# → só PDF gerado; sem ruído em stdout
```

Exit codes preservados (ADR 0001 §6).

## 7. Testing

### 7.1 Smoke tests (todos passam em <30s)

- `Saint → PDF gerado, tamanho >5KB, sha256 estável entre runs.`
- `Sinner → PDF gerado, contém string "F" na primeira página, contém "LNTY-002" no corpo.`
- `Ninja-01 → PDF gerado, contém 3 ocorrências de "LNTY-002".`

### 7.2 Determinism gate (já em §5.3)

Roda 2× cada fixture, compara sha256. Falha CI se diferir.

### 7.3 Snapshot test (opcional, pós-Sprint 1)

Commitar PDFs golden de Saint/Sinner/Ninja em `engine/tests/golden-pdfs/` (git LFS se >100KB). Diff binário no CI. Atualização de golden é PR explícito (review humano obrigatório).

## 8. O que NÃO entra no Sprint 1

| Item | Quando |
|------|--------|
| Assinatura digital PAdES-B-LT | V1+ (Production MVP) — exige certificado digital corporativo Lintty (~R$2-5k/ano via Certisign/Soluti) |
| Timestamp TSA RFC 3161 | V1+ — fornecedor a definir (DigiCert, FreeTSA, ICP-Brasil) |
| Audit hash-chain (registro do `hash_pdf` em ledger imutável) | V1+ — exige Cloud SQL + stored procedure de inserção |
| URL compartilhável com token HMAC | V1+ — exige Cloud Run + Cloud Storage |
| Visualização gráfica do grafo de dependências | V1+ — Sales Cut usa ASCII art ou tabela; PNG/SVG embedded é polish |
| Internacionalização (PT/EN) | V1+ — Sales Cut é PT-BR only |

## 9. Custo de implementação

**Estimativa:** 1-2 semanas (Sprint 1 inteiro).

| Etapa | Esforço |
|-------|---------|
| Setup do projeto + NuGet QuestPDF + fonte embedded | 0,5 dia |
| Layout das páginas (capa, sumário, violações, exceções, grafo, rodapé) | 3-4 dias |
| Determinism gate + smoke tests | 1 dia |
| Integração CLI `--pdf` | 0,5 dia |
| Smoke contra Saint/Sinner/Ninja + ajustes finais | 1 dia |
| Buffer (descobertas de inconsistência QuestPDF/zlib) | 1-2 dias |

Total: ~7-9 dias úteis. Cabe na semana 3 do Sales Cut.

## 10. Parking lot (V1+)

| # | Item | Quando |
|---|------|--------|
| 1 | PAdES-B-LT signing via certificado Lintty | V1+ (após escolher provedor cripto) |
| 2 | TSA RFC 3161 timestamping | V1+ |
| 3 | Audit hash-chain insertion (registra `hash_content` no ledger Cloud SQL) | V1+ |
| 4 | Render do grafo de dependências como SVG/PNG embedado | V1+ |
| 5 | Versão EN do PDF | V2 |
| 6 | Customização de layout via `lintty.yml` (logo do contratante etc.) | V2 |
| 7 | Reativação do `inference_signature` no rodapé quando LLM voltar | V1+ (ver ADR 0002) |
| 8 | Snapshot tests com golden PDFs em git LFS | Sprint 1 buffer ou pós-Sprint 1 |

---

**Próxima decisão (após este ADR aceito):** `backend-dev-dotnet` abre PR no `engine/` adicionando o projeto `Lintty.Engine.Reporter` conforme §3.1, implementa `PdfReporter.GeneratePdf()` com a layout em §4, ativa flag `--pdf` no CLI conforme §6, e garante o gate de determinismo de §5.3 verde antes de mergear. `qa-engineer` valida os 3 PDFs gerados (Saint A / Sinner F / Ninja F) contra o roteiro do `docs/sales/talk-track.md`.
