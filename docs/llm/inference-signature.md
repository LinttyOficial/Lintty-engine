# `inference_signature` — referência de implementação

> **STATUS: V1+ ROADMAP — DESATIVADO NO SALES CUT** (pivô 2026-04-27). No Sales Cut, o JSON do laudo carrega `inference_signature: null` permanentemente, porque não há LLM no pipeline. Documento preservado para Production MVP.

Documento curto para o backend-dev-dotnet e o orquestrador (Phase B). Substitui o placeholder `null` do ADR 0001 §3 quando o orquestrador rodou. Spec normativa em ADR 0002 §6.

## Forma final no JSON do laudo

```json
{
  "inference_signature": {
    "few_shot_hash": "1c5e8d9a2b3f4071",
    "max_tokens": 1024,
    "model": "claude-opus-4-7",
    "model_snapshot_id": "claude-opus-4-7-20260315",
    "prompt_hash": "a3f2c1d8b9e4076f",
    "system_prompt_hash": "9e2a4f1d8c3b5076",
    "temperature": 0,
    "tools_hash": ""
  }
}
```

Chaves serializadas em ordem **lexicográfica** (`json.dumps(..., sort_keys=True, separators=(",", ":"))`). Cada hash é `sha256` do conteúdo normalizado, truncado a **16 hex chars**.

## Algoritmo (referência Python — orquestrador Phase B)

```python
import hashlib, unicodedata

def lintty_hash(content: str) -> str:
    # 1. Normalize line endings to \n (Windows CRLF and old Mac CR collapse)
    s = content.replace("\r\n", "\n").replace("\r", "\n")
    # 2. UTF-8 NFC (precomposed Unicode forms)
    s = unicodedata.normalize("NFC", s)
    # 3. SHA-256 over UTF-8 bytes, truncate to 16 hex chars
    return hashlib.sha256(s.encode("utf-8")).hexdigest()[:16]
```

## Equivalente .NET (motor reproduz a mesma assinatura para `fingerprint` em ADR 0001 §5.7)

```csharp
public static string LinttyHash(string content)
{
    var s = content.Replace("\r\n", "\n").Replace("\r", "\n");
    s = s.Normalize(NormalizationForm.FormC);
    using var sha = SHA256.Create();
    var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(s));
    return Convert.ToHexString(bytes).ToLowerInvariant().Substring(0, 16);
}
```

## Exemplo concreto (caso fictício, demo Sinner)

### Input string-to-hash do `system_prompt_hash`

Conteúdo do bloco SYSTEM de `docs/llm/prompts/lnty-004.md` (sem o frontmatter YAML, só do "You are the Lintty Semantic Engine..." até o fim do JSON schema), normalizado:

```
You are the Lintty Semantic Engine, a static architecture analyzer for C#/.NET
codebases operating under strict compliance. Your tone is clinical, factual,
impessoal, definitivo. ...
[... ~1200 tokens ...]
... No prose outside the JSON. No markdown. No code fences around the JSON.
```

### Hashes resultantes (placeholder ilustrativo)

```
system_prompt_hash = lintty_hash(SYSTEM_BLOCK)        = "9e2a4f1d8c3b5076"
few_shot_hash      = lintty_hash(
                       open("01-clear-violation.md").read() +
                       open("02-clear-no-violation.md").read() +
                       open("03-boundary-case.md").read()
                     )                                = "1c5e8d9a2b3f4071"
prompt_hash        = lintty_hash(USER_TEMPLATE)       = "a3f2c1d8b9e4076f"
tools_hash         = ""                                # Sprint 1 não usa tool_use forçado
```

> **Importante:** `prompt_hash` é hash do **template** (com placeholders `{{candidate_id}}` etc.), **não** do template renderizado para um candidato específico. Renderizar muda a cada chamada; o template é o que precisa ser reproduzível.

### `inference_signature` final no laudo

```json
{
  "few_shot_hash": "1c5e8d9a2b3f4071",
  "max_tokens": 1024,
  "model": "claude-opus-4-7",
  "model_snapshot_id": "claude-opus-4-7-20260315",
  "prompt_hash": "a3f2c1d8b9e4076f",
  "system_prompt_hash": "9e2a4f1d8c3b5076",
  "temperature": 0,
  "tools_hash": ""
}
```

Aparece no rodapé técnico do PDF como string compacta:
`Lintty/canon-1.0.0 · opus-4-7@20260315 · sys:9e2a4f1d8c3b5076 · fs:1c5e8d9a2b3f4071 · t=0`

## Quando muda

Qualquer alteração em qualquer campo invalida cache de inferência (canon §5 e `04-llm-ops.md` §5). Side effect: laudos antigos continuam reproduzíveis bit-a-bit porque arquivam a `inference_signature` antiga junto.

## Modo mock (Sprint 1 Phase B sem ZDR)

Quando `--llm-mock` está ativo, orquestrador emite:

```json
{
  "few_shot_hash": "mock-fs-1c5e8d9a",
  "max_tokens": 0,
  "model": "mock",
  "model_snapshot_id": "mock-2026-04-27",
  "prompt_hash": "mock-pr-a3f2c1d8",
  "system_prompt_hash": "mock-sys-9e2a4f1d",
  "temperature": 0,
  "tools_hash": ""
}
```

Prefixo `mock-` é a forma padronizada de sinalizar no PDF "este laudo NÃO foi auditado por LLM real". Demo mostra essa string visivelmente.
