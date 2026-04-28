# ADR 0002 — LLM Sprint 1 (Phase A: design + scaffold + mock client)

- **Status:** **DEFERRED TO V1+** (pivô Zero-IA do Sales Cut em 2026-04-27 — ver `docs/12-sales-cut.md` reformulado e `docs/adr/0003-pdf-reporter.md`). Design preservado para reativação pós-validação comercial.
- **Date:** 2026-04-27
- **Author:** ai-llm-engineer
- **Audience:** backend-dev-dotnet (Phase B), software-architect, product-owner, qa-engineer
- **Sprint:** 1 — Sales Cut Tier 2 (`docs/12-sales-cut.md` §3.1), semanas 5-6
- **Canon pinado:** `1.0.0` (`docs/02-canon-v1.md`)
- **Pré-requisitos atendidos:** Sprint 0 verde (motor `engine/` com 7 analyzers Roslyn, 8/8 testes, ADR 0001 mergeado).
- **Pré-requisito EM PARALELO (não bloqueia Phase A):** ZDR Anthropic (`docs/compliance/zdr-anthropic-plan.md`, prazo 2026-05-25).

## 1. Escopo do Sprint 1

### 1.1 Entra (Phase A — este ADR)

- Design completo do contrato `ai_candidate` (motor → orquestrador) e `verdict` (LLM → motor).
- Spec do `inference_signature` definitivo (substitui o placeholder `null` do ADR 0001).
- Prompt template versionado de **LNTY-004** (advogado de defesa) + 3 few-shots calibrados.
- Stub do diretório `orchestrator/` com decisão de stack + interface `ILlmClient`.
- Mock client (verdicts canned em JSON) para demo offline sem ZDR ativo.

### 1.2 Entra (Phase B — próxima rodada, fora deste ADR)

- Pré-filtro Roslyn LNTY-004 no motor: `MethodDeclarationSyntax` em tipos `*Repository` ou que implementam `IRepository<T>` + complexidade ciclomática > 3 OU comparações fora de LINQ OU chamadas para fora de `System.*`/persistência. Emite item em `ai_candidates[]`.
- Implementação do orquestrador no stack escolhido (§2 abaixo): consome JSON do motor, itera `ai_candidates[]`, monta prompt cache-aware, chama `ILlmClient` (mock no Sprint 1), funde `verdicts[]` em violações + `evidence` no laudo final.
- Demo path: `lintty analyze --solution Sinner.sln --enable-llm --llm-mock` retorna laudo com 2-3 violações LNTY-004 detectadas em `OrderRepository.SaveAsync` e `MegaRepository.DoEverything` (mas este último é abortado por LNTY-009 antes do LLM — consistente com §9 de `04-llm-ops.md`).

### 1.3 NÃO entra (Sprint 2+)

- LNTY-005 (Anemic Domain) — fica off por default no canon; ativaremos quando primeiro contratante explicitar DDD rico.
- ZDR Anthropic ao vivo: orquestrador chama API real só após contrato ZDR assinado. Phase B usa **mock client** em código sintético (Saint/Sinner/Ninja).
- Auto-consistência (Prompt B) — canon §4 prevê threshold 0.85 para disparar segunda chamada; Phase B implementa **só Prompt A**. Phase B+ adiciona Prompt B refutador.
- Cache nativo da Anthropic (prompt-caching beta header) — design já contempla, mas só liga em produção real.
- Drift monitoring, Golden Test diário, Pub/Sub, Cloud Run — são Sprint 2+.

## 2. Decisão de stack do orquestrador

**Decisão:** **Python 3.12** com `anthropic` SDK oficial.

Justificativa (5-7 linhas, revisitável Sprint 2):

1. **Prompt caching maduro no SDK Python**: header `anthropic-beta: prompt-caching-2024-07-31` + `cache_control: {"type":"ephemeral"}` em blocos system/user. SDK Node tem paridade, mas o Python tem mais exemplos públicos para troubleshooting.
2. **Ergonomia**: parsing JSON de 9 violações + JSON Schema validation (`jsonschema`/`pydantic v2`) + I/O síncrono é trivial em Python. .NET no orquestrador acopla LLM ao motor, o que viola a separação que ADR 0001 cravou (motor é binário standalone factual).
3. **Cloud Run friendly (Sprint 2)**: container Python com `uvicorn`/`gunicorn` ou job batch é trivial; Anthropic SDK é primeira-classe.
4. **Manutenção solo**: founder já tem fluência operacional em Python (qa-engineer scripts, futuro reporter). Adicionar Node como terceira linguagem (já temos .NET no motor + frontend) custa caro num time de 1.
5. **Custo de troca baixo**: orquestrador é um wrapper de ~300 linhas; se Sprint 2 mostrar que Cloud Run + Pub/Sub pedem Go, refazemos sem dor.

**Decisão é Sprint 1, revisitável Sprint 2 se aparecer razão estrutural forte (ex: integração com Pub/Sub via gRPC streaming).**

## 3. Arquitetura de fluxo

```
┌────────────────────────────────────────────────────────────────────┐
│ engine/ (motor .NET, ADR 0001)                                     │
│  - Roslyn type-aware: 7 analyzers determinísticos                  │
│  - Pré-filtro LNTY-004 (Phase B): emite candidatos em ai_candidates│
│  - JSON output: { violations[], ai_candidates[], inference_signature: null }
└──────────────────────────────┬─────────────────────────────────────┘
                               │ stdout JSON
                               ▼
┌────────────────────────────────────────────────────────────────────┐
│ orchestrator/ (Python 3.12, Phase B)                               │
│  - reads engine JSON (stdin or --input)                            │
│  - for each candidate in ai_candidates[]:                          │
│      verdict = llm_client.evaluate_candidate(candidate)            │
│      if verdict.verdict == "VIOLATION":                            │
│          merge into violations[] with evidence_quote               │
│  - rewrites JSON with inference_signature populated                │
│  - emits final laudo JSON to stdout                                │
└──────────────────────────────┬─────────────────────────────────────┘
                               │
              ┌────────────────┴────────────────┐
              │                                 │
              ▼                                 ▼
   ┌────────────────────┐            ┌──────────────────────────┐
   │ MockLlmClient      │            │ AnthropicLlmClient       │
   │ (dev/demo, Phase B)│            │ (prod, Sprint 2 c/ ZDR)  │
   │ - reads canned     │            │ - anthropic SDK          │
   │   verdicts JSON    │            │ - prompt caching ON      │
   │ - matches by       │            │ - Opus 4.7 (LNTY-004)    │
   │   candidate_id     │            │ - temperature=0          │
   └────────────────────┘            └──────────────────────────┘
                               │
                               ▼
                  ┌──────────────────────────┐
                  │ Final laudo JSON         │
                  │ - violations[] enriched  │
                  │ - inference_signature    │
                  │   populated              │
                  │ - inference_errors[] if  │
                  │   any LLM call failed    │
                  └──────────────────────────┘
```

Princípio: **motor sempre roda primeiro e produz JSON factual válido sem o orquestrador**. Orquestrador é puro enricher: nunca anula violação determinística do Roslyn, só adiciona violações LLM e preenche `inference_signature`.

## 4. Schema do `ai_candidate`

Refina o placeholder do ADR 0001 §3 (`ai_candidates: []`).

```jsonc
{
  "candidate_id": "sha256:7b2c4f...",        // sha256 truncado a 32 hex chars; estável
  "rule_id": "LNTY-004",                       // pattern ^LNTY-\d{3}$
  "file": "Sinner.Infrastructure/OrderRepository.cs",  // relativo à solution root
  "line": 31,
  "column": 9,
  "symbol_fqn": "Sinner.Infrastructure.OrderRepository.SaveAsync",
  "code_snippet": "public Task SaveAsync(Order order, ...) {\n    if (order.Total > 1000m) {\n        order.Total *= 0.90m;\n    }\n    ...\n}",
  "ast_kind": "MethodDeclarationSyntax",
  "layer": "Infrastructure",                   // enum: Domain | Application | Infrastructure | Presentation
  "referenced_symbols": [
    "Sinner.Domain.Order",
    "System.Threading.Tasks.Task"
  ],
  "additional_context": {
    "containing_type": "Sinner.Infrastructure.OrderRepository",
    "implements": ["Sinner.Application.IOrderRepository"],
    "cyclomatic_complexity": 6,
    "comparison_operators_outside_linq": 3,
    "calls_outside_persistence_namespaces": [
      "Sinner.Domain.Order.set_Total"
    ],
    "method_loc": 22,
    "is_repository_by_naming": true,
    "is_repository_by_interface": true
  }
}
```

### Algoritmo do `candidate_id`

```
input  = rule_id + "|" + file + "|" + symbol_fqn + "|" + canon_version + "|" + code_slice_normalized
hash   = sha256(input encoded UTF-8 NFC)
candidate_id = "sha256:" + hex(hash)[:32]
```

`code_slice_normalized` = trim + line endings → `\n` + tabs → 4 espaços (mesma normalização do `fingerprint` em ADR 0001 §5.7).

### Por que `referenced_symbols[]` é obrigatório

Sem isso o LLM precisa adivinhar se `order.ApplyDiscount()` é método de Domain (regra de negócio) ou de Infrastructure (idiom de ORM). Roslyn já sabe — a gente passa como **fato pré-resolvido**, alinhado com princípio "Roslyn factual antes de LLM" (`agents/ai-llm-engineer.md`).

## 5. Schema do `verdict` retornado pelo LLM

JSON estrito, compatível com Tool Use forçado da Anthropic API (`04-llm-ops.md` §3).

```jsonc
{
  "candidate_id": "sha256:7b2c4f...",         // eco — orquestrador valida match
  "rule_id": "LNTY-004",
  "verdict": "VIOLATION",                      // enum: VIOLATION | NO_VIOLATION
  "defenses_considered": [
    {
      "defense_summary": "Threshold check could be input validation, not business rule",
      "assessment": "Rejected — discount tiers (0.90/0.95/0.98) are pricing policy, not validation",
      "accepted": false
    },
    {
      "defense_summary": "Loyalty discount could be encoded as ORM column default",
      "assessment": "Rejected — code applies discount imperatively before persisting; not declarative",
      "accepted": false
    },
    {
      "defense_summary": "Could be CQRS write-side denormalization",
      "assessment": "Rejected — no command bus or event involved; method is invoked directly from Application",
      "accepted": false
    }
  ],
  "rejection_reasons": [
    "Threshold-based pricing policy in repository couples persistence to domain rules",
    "Branching on Order.Total > {1000, 500, 100} encodes business tiers, not query filters"
  ],
  "evidence_quote": "Method 'SaveAsync' branches on Order.Total to apply tiered pricing discounts before persisting; this is domain pricing policy and belongs in the Order aggregate or a dedicated PricingService, not in the repository.",
  "severity_proposed": "high",                 // enum: critical | high | medium | low. Motor decide se aceita; canon trava high para LNTY-004.
  "confidence": "high"                         // enum: high | medium | low. Mapeia para canon §4 thresholds: high>=0.85, medium 0.70-0.85, low<0.70.
}
```

### Decisões de design

- **`defenses_considered[]` mínimo de 1, máximo de 3**: força "advogado de defesa" antes de condenar (canon §4, agent §"O modelo advogado de defesa"). Schema description trava isso para o LLM.
- **`verdict` binário**: `VIOLATION` ou `NO_VIOLATION`. Estado intermediário é responsabilidade do **motor** (canon §4 — Inconclusive vem de Prompt A vs Prompt B em divergência), não do LLM.
- **`confidence` enum, não float**: simplifica auditoria humana. Motor mapeia para float quando precisar gravar (`high=0.90`, `medium=0.78`, `low=0.55`) mas a saída do LLM é categórica — float gera falsa precisão.
- **`evidence_quote` é o que vai LITERAL no PDF**: tom forçado por few-shots ("clínico, factual, definitivo"). Schema `minLength: 30, maxLength: 500`.
- **`severity_proposed`**: o LLM **sugere** mas o motor **decide** com base no canon. LNTY-004 está travado em `high` no canon v1.0; se LLM propuser `critical`, motor ignora a proposta e mantém `high`. Campo serve para drift monitoring (Sprint 2+).
- **Sem `confidence_score` float**: não vamos calibrar 500 scans para floats no MVP. A versão float canônica (`04-llm-ops.md` §3) entra em Sprint 2 quando virar batch real.

## 6. Spec do `inference_signature` definitivo

Substitui o `null` do ADR 0001 §3 quando o orquestrador rodou.

### 6.1 Campos obrigatórios

```jsonc
{
  "model": "claude-opus-4-7",
  "model_snapshot_id": "claude-opus-4-7-20260315",   // pinado, sem "latest"
  "prompt_hash": "a3f2c1d8b9e4076f",                  // sha256(user_template) truncado
  "few_shot_hash": "1c5e8d9a2b3f4071",                // sha256(concat(few-shots ordenados))
  "system_prompt_hash": "9e2a4f1d8c3b5076",           // sha256(system block)
  "tools_hash": "",                                   // vazio no Sprint 1 (sem tool_use forçado em Phase B)
  "temperature": 0,
  "max_tokens": 1024
}
```

### 6.2 Algoritmo de hash

```
function compute_hash(content: str) -> str:
    normalized = content.encode("utf-8", errors="strict")
                       .decode("utf-8")
                       .replace("\r\n", "\n")
                       .replace("\r", "\n")
    nfc = unicodedata.normalize("NFC", normalized)
    digest = hashlib.sha256(nfc.encode("utf-8")).hexdigest()
    return digest[:16]   # truncado a 16 hex chars
```

- **Encoding:** UTF-8 NFC (Normalization Form C). Garante que `é` (precomposto) e `e + ́` (decomposto) hashem igual.
- **Line endings:** sempre `\n`. Hash em Windows (CRLF) e Linux (LF) deve ser idêntico.
- **Truncamento:** 16 hex chars (64 bits) — colisão prática zero para ~10⁹ prompts; cabe no rodapé do PDF.

### 6.3 Ordem dos campos no `inference_signature`

Lexicográfica nas chaves do objeto serializado para JSON. Garante determinismo:

```
few_shot_hash, max_tokens, model, model_snapshot_id, prompt_hash,
system_prompt_hash, temperature, tools_hash
```

Implementação: `json.dumps(signature, sort_keys=True, separators=(",", ":"))`.

### 6.4 Quando muda

Qualquer alteração em qualquer campo da tupla = signature diferente = cache invalidado = laudo antigo continua reproduzível com signature antiga arquivada.

## 7. Política de falha do LLM

Princípio canônico (`agents/ai-llm-engineer.md` §"Princípios não-negociáveis" #6): **falha do LLM ≠ aprovação**.

| Falha | Comportamento orquestrador | Campo no laudo final |
|-------|---------------------------|----------------------|
| Timeout (>30s na chamada) | Retry 1× com backoff 2s. Se falhar de novo, marca candidato como `inference_error: "timeout"`. | `inference_errors[].kind = "timeout"` |
| Rate limit (HTTP 429) | Backoff exponencial até 3 tentativas; falha → `inference_error: "rate_limit"`. | `inference_errors[].kind = "rate_limit"` |
| ZDR off + ambiente prod | **Aborta o scan inteiro** com exit code 2 do orquestrador. Mensagem clara: "ZDR contractual signature missing — refusing to send code to LLM". | scan abortado, sem laudo emitido |
| ZDR off + flag `--llm-mock` | OK, usa MockLlmClient. Não é falha. | `inference_signature.model = "mock"` |
| Parse error (JSON inválido do LLM) | Retry 1× com `temperature=0` (se já estava em 0, falha). | `inference_errors[].kind = "parse_error"` |
| Schema validation fail (campo obrigatório ausente) | Tratamento idêntico a parse error. | `inference_errors[].kind = "schema_error"` |
| Anthropic API down (5xx) | 3 retries; falha → `inference_error: "api_unavailable"`. | `inference_errors[].kind = "api_unavailable"` |
| Resposta indica injection attempt detectado | Adiciona violação automática `flagged_for_review` (deferred V1.1, ver `04-llm-ops.md` §6). Sprint 1 só registra no log. | log interno; sem violação no laudo |

### Estrutura de `inference_errors[]` no laudo final

```jsonc
{
  "candidate_id": "sha256:7b2c4f...",
  "kind": "timeout",
  "attempts": 2,
  "message": "Anthropic API timeout after 30s"
}
```

Aparece como **bloco visível** no PDF (não escondido em logs): "N candidatos não puderam ser avaliados — confidence do laudo reduzida". Princípio: o cliente **vê** que algo falhou, nunca interpreta como aprovação tácita.

## 8. Política de cache (prompt caching Anthropic)

### 8.1 Estrutura cache-aware

Conforme `04-llm-ops.md` §2 e skill `claude-api`:

```python
messages = [
    {
        "role": "system",
        "content": [
            {
                "type": "text",
                "text": SYSTEM_PROMPT_LNTY_004,        # ~1.2K tokens, fixo
                "cache_control": {"type": "ephemeral"}
            },
            {
                "type": "text",
                "text": FEW_SHOTS_LNTY_004,            # ~2.5K tokens, fixo
                "cache_control": {"type": "ephemeral"}
            }
        ]
    },
    {
        "role": "user",
        "content": render_user_template(candidate)    # ~500 tokens, variável
    }
]
```

### 8.2 Breakpoints

Anthropic permite até 4 cache breakpoints por request. Usamos **2**:
1. Após system prompt (LNTY-004 rule definition + persona).
2. Após few-shots (3 exemplos calibrados).

User block (com o `code_snippet` específico) é sempre dinâmico.

### 8.3 TTL esperado

- **Default:** 5 minutos. Em scan típico do Sinner com ~3-5 candidatos LNTY-004 sequenciais (~30s entre chamadas), TTL de 5 min cobre tudo: **primeiro candidato escreve cache, próximos 4 leem cache.**
- **Beta extension de 1h:** quando ZDR Enterprise estiver assinado, ligar header `anthropic-beta: prompt-caching-2024-07-31` + flag de extensão. Útil para batch de múltiplos clientes em sequência.

### 8.4 Estimativa de cache hit rate (Sprint 1, Sinner ~3-5 candidatos)

- Candidato 1: cache **MISS** (write). Custo full input.
- Candidatos 2-5: cache **HIT** read (~10% do custo de input do bloco cacheado).
- **Hit rate batch típico:** ~80% (4/5 chamadas).

## 9. Estimativa de custo (1 scan típico do Sinner com 3 candidatos LNTY-004)

Modelo: **Claude Opus 4.7** (`claude-opus-4-7-20260315`, snapshot pinado). Pricing referência (snapshot mental de Apr/2026, validar via console Anthropic antes de Phase B):

- Input: $15 / 1M tokens
- Cache write: $18.75 / 1M tokens (1.25× input)
- Cache read: $1.50 / 1M tokens (0.10× input)
- Output: $75 / 1M tokens

### Cálculo

| Item | Tokens | Chamada 1 (MISS) | Chamadas 2-3 (HIT) |
|------|--------|------------------|----------------------|
| System prompt cacheado | ~1.200 | $0.0225 (write) | $0.0018 (read) |
| Few-shots cacheados | ~2.500 | $0.0469 (write) | $0.00375 (read) |
| User dinâmico | ~500 | $0.0075 (input) | $0.0075 (input) |
| Output (verdict JSON) | ~400 | $0.030 (output) | $0.030 (output) |
| **Total por chamada** | | **$0.107** | **$0.043** |

**Custo total por scan (3 candidatos):** $0.107 + 2 × $0.043 = **~$0.19**.

**Latência total estimada:** ~5-8s por chamada × 3 = **~20-25s**. Cabe no orçamento de < 30s da demo path.

**Fallback Sonnet 4.6** (~5× mais barato): ~$0.04 por scan, ~3-5s por chamada. Decisão de fallback fica no orquestrador: se Opus falha 2× ou rate-limit, troca para Sonnet com flag explícita no `inference_signature` (`model: "claude-sonnet-4-6"`).

## 10. Demo path (15 min)

```bash
# Pré-condição: ZDR ainda não assinado, então rodamos com mock
$ ./engine/lintty-engine analyze \
    --solution fixtures/the-sinner/Sinner.sln \
    --output engine-output.json

$ python orchestrator/cli.py \
    --input engine-output.json \
    --llm-mock \
    --mock-data docs/llm/mock-verdicts.json \
    > final-laudo.json

$ jq '.violations[] | select(.rule_id == "LNTY-004")' final-laudo.json
# 2 violações detectadas em OrderRepository.SaveAsync e MegaRepository.DoEverything
# (esta última na verdade é abortada por LNTY-009 antes do LLM — mostrar isso ao vivo)

$ jq '.inference_signature' final-laudo.json
# {model: "mock", model_snapshot_id: "mock-2026-04-27", prompt_hash: "...", ...}
```

Tempo total real: < 3s (mock client é instantâneo). Em demo, narrar: "em produção com Anthropic, isso leva ~25s; aqui usamos verdicts canned para rodar sem custo de API."

Mostrar no terminal o **bloco `defenses_considered[]`** sendo lido em voz alta — esse é o pitch do "advogado de defesa".

## 11. Parking lot (Sprint 2+)

| # | Item | Quando |
|---|------|--------|
| 1 | ZDR Anthropic ao vivo + AnthropicLlmClient real | Sprint 2 (após contrato 2026-05-25) |
| 2 | Auto-consistência (Prompt B advogado-de-defesa refutador) | Sprint 2, após primeiro batch de scans reais para calibrar threshold |
| 3 | Anthropic Batch API (50% desconto) | Sprint 3+, quando volume justificar |
| 4 | Citations API (linkar evidência a linha exata) | Sprint 3+ |
| 5 | LNTY-005 (Anemic Domain) — prompt + few-shots dedicados | Quando primeiro contratante explicitar DDD rico |
| 6 | Drift monitoring (avg_confidence, violation_rate semanal) | Sprint 2 quando entrar Pub/Sub |
| 7 | Golden Test diário rodando contra Anthropic prod | Sprint 2 |
| 8 | Cache native Anthropic (1h extension) | Sprint 2, quando volume justificar |
| 9 | Tokenizer Anthropic real (atualmente LNTY-009 usa LoC × fator) | Sprint 1 Phase B se sobrar tempo, senão Sprint 2 |

---

**Próxima decisão (após este ADR aceito):** backend-dev-dotnet abre PR no `engine/` adicionando o pré-filtro Roslyn LNTY-004 (visitor `MethodDeclarationSyntax`) que emite `ai_candidates[]` conforme schema §4. Em paralelo, ai-llm-engineer (eu) abre o stub `orchestrator/` em Python conforme §3 com `MockLlmClient` consumindo `docs/llm/mock-verdicts.json`. Phase B roda Saint (espera 0 candidatos), Sinner (espera 1-2 candidatos com verdict VIOLATION), Ninja-01 (não dispara LNTY-004).
