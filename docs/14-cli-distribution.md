# 14 — Distribuição do CLI

> Plano de distribuição do `lintty-engine` no V0. Decisão de arquitetura está em [ADR 0005](adr/0005-distribution-model.md) (CLI Self-Service como default). Este doc é o **plano executivo** — o que tem que rolar para o cliente conseguir baixar e rodar.

## 1. O que precisamos entregar

Um cliente novo, sem nenhum contato prévio com o produto, conseguir em **menos de 5 minutos**:

1. Achar o link de download.
2. Baixar o binário do sistema operacional dele.
3. Validar que o binário é o oficial (sha256).
4. Rodar `lintty-engine analyze --solution X.sln --pdf laudo.pdf` e ver o PDF abrir.

Se isso quebra em qualquer passo, o caminho self-service não funciona — e sobra só Web Inspector e Concierge.

## 2. Targets suportados no V0

| RID | Sistema | Prioridade |
|-----|---------|-----------|
| `win-x64` | Windows 10/11 64-bit | **P0** — mercado .NET é majoritariamente Windows |
| `linux-x64` | Linux glibc x86_64 | **P0** — runners de CI |
| `osx-x64` | macOS Intel | P1 |
| `osx-arm64` | macOS Apple Silicon | P1 |

> Não suportamos Linux musl, FreeBSD, Windows ARM no V0. Quando alguém pedir, avalia.

## 3. Empacotamento

`dotnet publish` self-contained, single-file:

```bash
dotnet publish src/Lintty.Engine.Cli \
  -c Release \
  -r win-x64 \
  --self-contained \
  -p:PublishSingleFile=true \
  -p:PublishTrimmed=false \
  -o publish/win-x64
```

**Decisões de empacotamento:**

- `--self-contained`: cliente não precisa instalar SDK / runtime do .NET. Simplifica radicalmente o onboarding.
- `PublishSingleFile=true`: um único `.exe` (Windows) ou ELF (Linux/macOS).
- `PublishTrimmed=false`: trimming quebra Roslyn (reflection-heavy). Aceitamos binário maior (~80–100 MB) em troca de funcionar.
- **Sem ReadyToRun** no V0 — startup mais lento mas binário menor e mais simples de assinar.

**Saída esperada por target:**

| RID | Arquivo | Tamanho aprox. |
|-----|---------|----------------|
| `win-x64` | `lintty-engine.exe` | 90–110 MB |
| `linux-x64` | `lintty-engine` | 90–110 MB |
| `osx-x64` | `lintty-engine` | 90–110 MB |
| `osx-arm64` | `lintty-engine` | 90–110 MB |

## 4. Canal de distribuição

**GitHub Releases** no repo público `lintty/lintty-engine` (a criar).

- Cada release tem tag SemVer (ex: `v0.1.0`).
- Anexa os 4 binários + `SHA256SUMS.txt` + `SHA256SUMS.txt.asc` (assinatura GPG do operador, V0).
- **Sem auto-update no V0.** Cliente baixa explicitamente quando quiser nova versão.

> Em V1, considerar:
> - **winget** / **scoop** / **chocolatey** (Windows)
> - **homebrew** (macOS)
> - **apt** / **deb** (Ubuntu)
> - **dotnet tool install -g** (todos)
>
> Tudo isso **adiciona complexidade de manutenção sem agregar valor antes do primeiro pagante**. GitHub Releases basta no V0.

## 5. Verificação de integridade

Cliente sério vai querer verificar. Documentamos no README do repo:

### Windows (PowerShell)

```powershell
# Baixar
Invoke-WebRequest -Uri "https://github.com/lintty/lintty-engine/releases/download/v0.1.0/lintty-engine-win-x64.exe" -OutFile lintty-engine.exe
Invoke-WebRequest -Uri "https://github.com/lintty/lintty-engine/releases/download/v0.1.0/SHA256SUMS.txt" -OutFile SHA256SUMS.txt

# Verificar
$expected = (Get-Content SHA256SUMS.txt | Select-String "lintty-engine-win-x64.exe").ToString().Split(" ")[0]
$actual = (Get-FileHash lintty-engine.exe -Algorithm SHA256).Hash.ToLower()
if ($expected -eq $actual) { "OK" } else { "FAIL" }
```

### Linux / macOS

```bash
curl -LO https://github.com/lintty/lintty-engine/releases/download/v0.1.0/lintty-engine-linux-x64
curl -LO https://github.com/lintty/lintty-engine/releases/download/v0.1.0/SHA256SUMS.txt
sha256sum -c SHA256SUMS.txt --ignore-missing
chmod +x lintty-engine-linux-x64
```

### Assinatura de código

| Plataforma | V0 | V1+ |
|------------|----|-----|
| Windows (Authenticode) | **Não assinado** — Windows mostra SmartScreen warning na primeira execução | Cert EV de Code Signing (~US$ 300/ano) |
| macOS (notarização) | **Não assinado** — Gatekeeper bloqueia, cliente precisa abrir nas Preferências | Apple Developer ID (~US$ 99/ano) + notarytool |
| Linux | sha256 + GPG é suficiente | Mantém |

V0 deixa explícito no README: "binário não-assinado, valide pelo sha256". Risco aceitável para piloto técnico — não para enterprise self-service.

## 6. Pipeline de release

CI: GitHub Actions no repo `lintty/lintty-engine`. Workflow `release.yml` dispara em push de tag `v*`:

```yaml
# Pseudocódigo — refinar no .github/workflows/release.yml
on:
  push:
    tags: ['v*']

jobs:
  build:
    strategy:
      matrix:
        target:
          - { rid: win-x64,   os: windows-latest, ext: .exe }
          - { rid: linux-x64, os: ubuntu-latest,  ext: ''   }
          - { rid: osx-x64,   os: macos-13,       ext: ''   }
          - { rid: osx-arm64, os: macos-14,       ext: ''   }
    runs-on: ${{ matrix.target.os }}
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: 9.0.300 }
      - run: dotnet test engine/Lintty.Engine.sln
      - run: dotnet publish engine/src/Lintty.Engine.Cli ...
      - uses: actions/upload-artifact@v4

  release:
    needs: build
    runs-on: ubuntu-latest
    steps:
      - uses: actions/download-artifact@v4
      - run: sha256sum lintty-engine-* > SHA256SUMS.txt
      - uses: softprops/action-gh-release@v2
        with:
          files: |
            lintty-engine-*
            SHA256SUMS.txt
```

**Gates antes do release:**

1. `dotnet test` passa em todas as plataformas (especialmente o `DeterminismTests`).
2. **Smoke test cross-platform**: cada binário rodado contra os 3 fixtures (`the-saint`, `the-sinner`, `the-ninja-01`) e o PDF resultante tem `hash_content` igual entre OSes. Se Windows e Linux geram PDFs diferentes, é bug de locale/font/MSBuild — não sobe.

## 7. Documentação cliente-facing

Mínimo viável para o V0:

1. **README do repo `lintty-engine`** com:
   - O que o produto faz (3 frases + link para `lintty.com`).
   - Tabela de download por plataforma.
   - Comando de verificação sha256.
   - Quickstart: `lintty-engine analyze --solution X.sln --pdf laudo.pdf`.
   - Troubleshooting comum (SmartScreen, Gatekeeper, faltando .NET system libs em Linux mínimo).

2. **Página `lintty.com/cli`** com:
   - Botões grandes "Baixar para Windows / Linux / macOS" (detecta OS e destaca um).
   - Mesmo conteúdo de quickstart do README, com cópia adaptada para venda.

3. **Vídeo screencast** (~3 min) mostrando: baixar → validar → rodar contra Sinner → abrir PDF. Hospedado no YouTube (não-listado) ou Loom. Backup para a demo.

## 8. Versionamento

SemVer:
- `v0.x` — V0 (escopo deste doc): breaking changes possíveis em qualquer minor.
- `v1.0` — quando o primeiro piloto pagante validar. Estabiliza CLI contract.
- `v1.x` — features aditivas (mais rules, mais formatos de output).
- `v2.x` — quando entrar LLM, multi-stack, etc.

**Compatibilidade do JSON contract** ([ADR 0001](adr/0001-motor-skeleton.md) §3):
- `schema_version: "1.0"` é **LOCKED** mesmo em `v0.x`. Mudanças no schema exigem `1.1` ou `2.0`.
- Campos placeholder (`inference_signature`, `audit_chain`) ficam como `null` mas existem.

## 9. Esforço estimado

| Etapa | Esforço |
|-------|---------|
| Workflow `release.yml` + smoke tests cross-platform | 2–3 dias |
| README do repo + página `lintty.com/cli` | 1–2 dias |
| Screencast de 3 min | 0.5 dia |
| Primeira release `v0.1.0` (manual no início) | 0.5 dia |
| **Total** | **~1 semana** |

## 10. O que NÃO está no V0

- Auto-update via channel update server.
- Pacotes em winget / scoop / homebrew / apt.
- `dotnet tool install -g lintty-engine` (cliente precisa de SDK — fricção).
- Code signing (Authenticode + notarização macOS).
- Telemetria opcional opt-in (uso, latência, OS).
- Containers Docker pré-buildados (cliente que quiser docker, builda do source).

Tudo isso é V1+, decidir quando houver tração.
