# 01 — Visão e Modelo de Produto (V0 Real)

> Versão lean alinhada ao escopo atual: **CLI determinístico + Web Inspector**. A versão completa do blueprint (com LLM, dashboard self-service, billing wallet, hash-chain, SOC 2) foi preservada em [`futuro/product-vision-blueprint.md`](futuro/product-vision-blueprint.md). **Não usar a versão de futuro como referência operacional** — é roadmap, não realidade.

## Visão

Lintty é um **árbitro técnico de arquitetura .NET**. Dado um `.sln`, a gente aplica o Canon (regras Hexagonal/DDD) e devolve um **PDF de laudo determinístico** com nota A–F e lista de violações.

A promessa central é **auditabilidade por construção**:

1. **100% determinístico** — mesmo input gera o mesmo PDF, byte por byte. Zero alucinação.
2. **Zero IA no V0** — sem chamada a LLM, sem custo de inferência, sem dado saindo do escopo do scan.
3. **Local-first** — no caminho default (CLI), o código fonte nunca sai do equipamento do cliente.

## O que Lintty é, e o que não é

**É:**
- Ferramenta de validação arquitetural automatizada para C# / .NET 6+.
- Oráculo técnico que emite um laudo PDF reproduzível, com hash do conteúdo no rodapé.
- "Selo de revisão" que o contratante usa, sob responsabilidade própria, para validar entregas.

**Não é:**
- Escrow financeiro. Não custodia dinheiro nem libera pagamento.
- Árbitro jurídico. Não é parte do contrato entre contratante e agência.
- Mediador humano. Não há equipe de revisão manual.
- ~~Plataforma com painel multi-tenant, billing wallet, GitHub App, dashboard de tendências~~ — isso é V1+, está em `futuro/`.

## Mercado-alvo

- **Contratantes:** empresas que terceirizam desenvolvimento .NET para agências externas, e querem evidência objetiva de que o que recebem segue padrão arquitetural.
- **Vertical foco:** varejo brasileiro de grande porte (decisão PO 2026-04-28).
- **Geografia inicial:** Brasil. No V0 a análise roda **local** (laptop do cliente ou backend local do Web Inspector).
- **Tamanho típico:** solutions C# / .NET 6–9, 50k–1M LoC.

## Atores

| Ator | Papel | Acesso |
|------|-------|--------|
| **Contratante** | Paga e consome o laudo | Roda CLI ou usa Web Inspector. Recebe PDF. |
| **Agência** | Entrega código sob avaliação | Sem acesso direto à plataforma no V0. Recebe o laudo via Contratante. |
| **Operador Lintty** | Quem mantém produto | No V0, opera o release do CLI e o backend do Web Inspector. Não recebe código fonte do cliente no caminho default. |

> Em V1+ entram dashboard self-service, convite de agência como `External Contributor`, painel executivo, etc. Hoje, fora de escopo.

## Os dois caminhos de consumo (V0)

A spec detalhada de cada um está nos respectivos docs. Resumo aqui:

### Caminho A — CLI Self-Service (default)

Cliente baixa o binário oficial (GitHub Releases, ver [`14-cli-distribution.md`](14-cli-distribution.md)), valida `sha256`, e roda no equipamento dele:

```bash
lintty-engine analyze --solution MeuProjeto.sln --pdf laudo.pdf
```

**Postura jurídica:** Lintty é fornecedor de software licenciado. Não há tratamento de dados pelo Lintty. Vínculo é EULA. Decisão original em [ADR 0005](adr/0005-distribution-model.md).

### Caminho B — Web Inspector

Cliente acessa `lintty.com/inspect`, cola URL do GitHub (público ou via PAT temporário), backend clona shallow, roda o **mesmo CLI**, devolve o **mesmo PDF**, descarta o clone. Spec em [`13-web-inspector.md`](13-web-inspector.md).

**Postura jurídica:** Lintty é Operador de tratamento (LGPD art. 5º VII) durante a janela do scan. DPA exigido para uso comercial recorrente. Análise efêmera, descarte imediato.

> O caminho B existe **para conveniência e demo**. Nenhum dos dois caminhos depende de IA, de cloud externa, ou de assinatura digital qualificada — todos são V1+.

## Modelo de cobrança no V0

**Ainda não cobramos via produto.** No V0:

- CLI baixado é **gratuito para download** (licença restritiva, mas binário acessível).
- Web Inspector tem **rate-limit** anônimo (ex: 3 scans/dia/IP) e **acesso pago via TED + NF-e manual** para uso recorrente, calibrado caso a caso com o primeiro piloto.
- Sem Stripe, sem wallet de créditos, sem trial — tudo isso é V1+ ([`futuro/data-and-flow.md`](futuro/data-and-flow.md)).

A regra: **a precificação se calibra com o primeiro piloto pagante**. Antes disso, foco é validar a tese, não o pricing.

## O laudo PDF (artefato central de valor)

O PDF é o que entrega valor. Características no V0:

| Atributo | V0 (real) | V1+ (em `futuro/`) |
|----------|-----------|---------------------|
| Determinismo bit-a-bit | ✅ Garantido (fontes embedded, sem timestamps no conteúdo, build determinístico) | — |
| Hash de integridade | ✅ `hash_content` = SHA-256 do JSON normalizado, no rodapé | — |
| Conteúdo (capa A–F, violações com snippet, sumário de exceções, grafo de deps) | ✅ Implementado | — |
| `canon_version` impresso | ✅ | — |
| Assinatura PAdES + TSA | ❌ | V1+ |
| Audit hash-chain imutável (Postgres + verificador externo) | ❌ | V1+ |
| `inference_signature` (model + snapshot LLM) | ❌ (campo `null` no JSON) | V1+ |

Spec do PDF em [ADR 0003](adr/0003-pdf-reporter.md).

## Mecânica de disputa (sem mediação humana)

Mesmo no V0, Lintty resolve "agência discorda do laudo" via três mecanismos automatizados:

### 1. Comentários `@lintty-ignore` no código

```csharp
// @lintty-ignore: LNTY-009 reason="Switch grande é exigência de protocol parser HL7"
```

- Justificativa obrigatória ≥ 30 caracteres.
- Justificativa é **impressa no PDF** como Sumário de Exceções, junto ao email do autor git.

### 2. Hard locks em violações Críticas

Algumas regras são **não-suprimíveis**, independente de `@lintty-ignore`. Se houver qualquer violação Crítica aberta, o selo (grade A–C) **não é emitido**:

- LNTY-001 (Domain Layer Isolation)
- LNTY-002 (Persistence Contamination)
- LNTY-007 (Dependency Cycles)

### 3. Cap de impunidade — 10% sobre Médias/Altas

Total de supressões válidas em um scan não pode exceder **10% das violações de severidade Média/Alta**. Excedeu, score cai automaticamente para **F**.

Detalhes em [`02-canon-v1.md`](02-canon-v1.md).

## Linha do tempo de uso (V0)

### Caminho CLI

1. Cliente baixa `lintty-engine` (GitHub Releases), valida sha256.
2. Roda contra a `.sln` da entrega: `lintty-engine analyze --solution X.sln --pdf laudo.pdf`.
3. Recebe o PDF localmente. Compartilha com a agência se quiser.
4. Repete por Milestone, manualmente.

### Caminho Web Inspector

1. Cliente acessa `lintty.com/inspect`.
2. Cola URL do GitHub (`https://github.com/org/repo`), opcional commit/branch.
3. Backend valida URL, clona shallow, roda o CLI, devolve PDF para download.
4. Clone descartado. Hash do PDF/JSON registrado em log de auditoria operacional (não cliente-facing).

## O que mudou em relação ao Blueprint completo

| Tema | Blueprint v1.0 (em `futuro/`) | V0 (real) |
|------|-------------------------------|-----------|
| Motor | Cloud Run sandbox + Anthropic LLM | Local: CLI .NET, sem cloud, sem LLM |
| Onboarding | GitHub App + dashboard self-service | Download direto + cola URL no site |
| Billing | Stripe + wallet de créditos | TED + NF-e manual (quando pagante) |
| Compliance | SOC 2 Type I, PAdES + TSA, hash-chain | LGPD básica + DPA simples + hash do PDF no rodapé |
| Atores | Multi-tenant (Contratante + Agência + Admin) | Single-user no V0 (Contratante roda) |

A versão completa **vive como roadmap em [`futuro/`](futuro/)**. Não é mais a referência operacional — é referência aspiracional.
