# orchestrator/

> **STATUS: V1+ ROADMAP — DESATIVADO NO SALES CUT (pivô 2026-04-27).**
> Sales Cut roda Zero-IA, Zero-Custo: motor Roslyn → JSON → PDF (QuestPDF). Sem orquestrador, sem LLM, sem ZDR. Este diretório fica preservado como stub para reativação pós-validação comercial. Ver `docs/12-sales-cut.md` reformulado e `docs/adr/0003-pdf-reporter.md`.

Orquestrador LLM do Lintty. Consome JSON factual do motor `engine/` e enriquece com vereditos LLM para regras semânticas (LNTY-004 no Sprint 1; LNTY-005 quando ativada por contratante).

**Status:** Phase A do Sprint 1 = stub apenas. Phase B implementa. **DEFERRED V1+** desde 2026-04-27.

Spec normativa: [`docs/adr/0002-llm-sprint-1.md`](../docs/adr/0002-llm-sprint-1.md).

## Stack

- **Linguagem:** Python 3.12
- **SDK Anthropic:** `anthropic` (oficial, instala via `pip install anthropic`)
- **Validação JSON:** `pydantic` v2 (modelo `AiCandidate` e `Verdict`)
- **CLI:** `argparse` (stdlib; sem dependência extra)
- **Testes:** `pytest`

Decisão de stack (revisitável Sprint 2): ADR 0002 §2.

## Estrutura de pastas planejada (Phase B)

```
orchestrator/
├── README.md                       # este arquivo
├── pyproject.toml                  # Phase B
├── orchestrator/
│   ├── __init__.py
│   ├── cli.py                      # entrypoint: lê JSON do motor, escreve JSON enriquecido
│   ├── models.py                   # pydantic: AiCandidate, Verdict, InferenceSignature
│   ├── prompts/
│   │   └── lnty_004.py             # carrega docs/llm/prompts/lnty-004.md + few-shots
│   ├── llm_client/
│   │   ├── __init__.py
│   │   ├── base.py                 # ILlmClient (Protocol)
│   │   ├── mock.py                 # MockLlmClient (canned verdicts)
│   │   └── anthropic_client.py     # AnthropicLlmClient (Sprint 2 com ZDR)
│   ├── signature.py                # compute_inference_signature, lintty_hash
│   └── merge.py                    # merge verdicts[] → violations[]
└── tests/
    ├── test_mock_client.py
    ├── test_signature.py
    └── test_merge.py
```

## Interface `ILlmClient` (Phase B pseudocódigo)

```python
from typing import Protocol
from .models import AiCandidate, Verdict, InferenceError

class ILlmClient(Protocol):
    """Contrato do cliente LLM. Mock e Anthropic real implementam."""

    def evaluate_candidate(
        self,
        candidate: AiCandidate,
    ) -> Verdict | InferenceError:
        """
        Avalia UM candidato contra a regra (LNTY-004 no Sprint 1).
        Retorna Verdict (com candidate_id ecoado) ou InferenceError em caso
        de falha. NUNCA levanta exceção para falhas esperadas — falhas
        viram InferenceError no laudo final (ADR 0002 §7).
        """
        ...
```

### Implementações Phase B

- **`MockLlmClient`**: lê `docs/llm/mock-verdicts.json`, faz match por `candidate_id` ou por padrão de `symbol_fqn`. Retorna verdict canned. Latência fake de 0ms.
- **`AnthropicLlmClient`**: monta prompt cache-aware (system + few-shots + user), chama `messages.create` com `model="claude-opus-4-7-20260315"`, `temperature=0`, `max_tokens=1024`, header de prompt caching ativo. Valida resposta contra schema pydantic. Retry policy conforme ADR 0002 §7.

## Phase B implementará

1. `orchestrator/models.py` — pydantic v2 dos schemas em ADR 0002 §4 e §5.
2. `orchestrator/signature.py` — `lintty_hash()` + `compute_inference_signature()` conforme `docs/llm/inference-signature.md`.
3. `orchestrator/llm_client/mock.py` — consome `docs/llm/mock-verdicts.json`.
4. `orchestrator/cli.py` — `python -m orchestrator --input engine.json --llm-mock --output laudo.json`.
5. `orchestrator/merge.py` — para cada verdict com `verdict == "VIOLATION"`, cria `Violation` no laudo final preservando ordenação determinística (`(file, line, column, rule_id, fingerprint)` conforme ADR 0001 §5.1).
6. Testes em `tests/` rodam contra fixtures de `fixtures/the-saint`, `fixtures/the-sinner` e ADR 0002 §10.

## Não-objetivos Phase A

- ❌ Não implementa `AnthropicLlmClient` real (ZDR pending, ADR 0002 §1.3).
- ❌ Não implementa Prompt B refutador (canon §LNTY-004 auto-consistência) — Sprint 2+.
- ❌ Não implementa Anthropic Batch API — Sprint 3+.
- ❌ Não implementa drift monitoring nem Golden Test diário — Sprint 2+.

## Como rodará na demo (Phase B)

```bash
$ ./engine/lintty-engine analyze \
    --solution fixtures/the-sinner/Sinner.sln \
    --output engine.json

$ python -m orchestrator \
    --input engine.json \
    --llm-mock \
    --mock-data docs/llm/mock-verdicts.json \
    --output laudo.json

$ jq '.violations[] | select(.rule_id == "LNTY-004") | .evidence' laudo.json
```

Tempo total real: < 3s com mock. Em produção real (Sprint 2 com ZDR), ~25s para 3 candidatos via Opus 4.7 (ADR 0002 §9).
