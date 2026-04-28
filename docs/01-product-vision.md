# 01 — Visão e Modelo de Produto

> **Nota Sales Cut (pivô 2026-04-27 — Zero-IA, Zero-Custo):** o V0 do produto roda **100% determinístico** (motor Roslyn type-aware + PDF gerado pelo motor via QuestPDF). A camada LLM e ZDR Anthropic ficam em **roadmap V1+**. Todas as referências a "IA / LLM / advogado de defesa / inference_signature" abaixo descrevem o **estado de produção alvo**, não o que está em campo no Sales Cut. Ver `docs/12-sales-cut.md` (reformulado).

## Visão

Lintty é um **árbitro técnico de arquitetura de software em tempo real**, operado como SaaS B2B. Sua função é resolver um problema concreto do mercado enterprise:

> Quando grandes empresas terceirizam o desenvolvimento de seus sistemas, elas não têm como auditar a qualidade estrutural do código entregue até que seja tarde demais — quando o sistema falha em escalar, em integrar, ou em sustentar mudanças.

Lintty preenche essa lacuna emitindo um **laudo técnico assinado digitalmente** que avalia a integridade arquitetural do código entregue, com base em regras explícitas e auditáveis.

## Posicionamento

Lintty **é**:
- Uma ferramenta SaaS de validação arquitetural automatizada.
- Um oráculo técnico que emite laudos com hash criptográfico e timestamp de TSA confiável.
- Um "selo de verificação" que o contratante usa, sob responsabilidade própria, para validar entregas de agências.

Lintty **não é**:
- Escrow financeiro. Não custodia dinheiro nem libera pagamento.
- Árbitro jurídico. Não é parte do contrato entre contratante e agência.
- Mediador humano. Não há equipe de revisão manual de disputas.

Esse posicionamento foi escolhido conscientemente para manter operação enxuta, evitar regulação financeira (BCB, KYC/KYB), e preservar a clareza do produto.

## Mercado-alvo

- **Compradores (contratantes):** empresas enterprise que terceirizam desenvolvimento de software .NET para agências externas.
- **Vertical-foco no MVP (Sales Cut):** **varejo brasileiro de grande porte.** *(Decisão PO 2026-04-28 — `docs/manual-actions.md`.)* Outras verticais (financeiro, seguros) viram pipeline reativo (atendemos se chegarem), não outbound ativo.
- **Geografia inicial:** **Brasil.** No Sales Cut a análise roda local; em V1+ a operação na nuvem fica em `us-east1` (GCP) com CDN para BR e expansão para data residency BR/EU.
- **Tamanho da entrega típica:** projetos C#/.NET com solutions de 50k a 1M LoC.

## Atores

| Ator | Papel | Acesso e visão |
|------|-------|----------------|
| **Contratante** | Empresa que paga e consome o laudo | Painel executivo: score, tendências, métricas de acoplamento, laudo PDF. Não vê código fonte. |
| **Agência** | Empresa que entrega código sob avaliação | Feedback granular nos PRs: linhas violadoras, sugestões de refatoração, score provisório. |
| **Admin Lintty** | Equipe interna do produto | Gestão de contas, billing, suporte L2. Sem mediação técnica de disputas. |

A Agência é convidada como `External Contributor` ao Projeto pelo Contratante. Um único Contratante pode gerenciar múltiplas agências em projetos distintos sob o mesmo painel.

## Modelo de Negócio

### Quem paga

O **Contratante** paga. A Agência usa a plataforma como ferramenta de feedback no fluxo de desenvolvimento.

### Modelos de cobrança

Dois SKUs no MVP:

1. **Pay-per-Scan (Milestone Audit)** — modelo principal de receita.
   - 1 scan = 1 auditoria oficial de Milestone que gera o PDF (assinado em V1+).
   - Tiering estrutural por LoC (valor calibrado com primeiro piloto):
     - Até 50k LoC: 1×
     - 50k–200k LoC: 2×
     - 200k–1M LoC: 4×
     - \>1M LoC: cotação enterprise
   - Sales Cut: cobrança via TED + NF-e manual. Stripe entra em V1.
   - **Pivô Zero-IA (2026-04-27):** removido o modelo de "pacotes de créditos" — vinculava-se a custo de inferência LLM, que não existe no Sales Cut. Volta como possível SKU em V1 se houver demanda.

2. **Continuous Feedback Subscription** — V1+, opcional, complementa o Pay-per-Scan.
   - Assinatura mensal por repositório.
   - Permite scans ilimitados em PRs (advisory, não geram PDF).
   - Mantém engajamento da agência durante o desenvolvimento.

### Não há (no MVP)

- Trial gratuito, free tier, ou pricing freemium.
- Split de pagamento entre contratante/agência.
- Cobrança recorrente baseada em uso (consumo medido pós-fatura).

## Milestone vs. PR — distinção crítica

A separação entre os dois tipos de scan é fundacional. Foram desenhados como produtos distintos:

| Atributo | Scan de PR | Milestone Audit |
|----------|------------|-----------------|
| **Quando dispara** | Cada push em PR | Solicitação explícita ("Solicitar Auditoria Oficial") |
| **Custo** | Coberto pela subscription mensal por repo (ou grátis se não houver subscription) | Consome 1+ créditos Pay-per-Scan |
| **Resultado visível** | GitHub Check Status + grade provisória no painel | Laudo PDF assinado + atualização de tendências oficiais |
| **PDF gerado?** | Não | Sim (PAdES + TSA) |
| **Bloqueia merge?** | Configurável (Required Status Check via branch protection) | Não bloqueia merge — gera artefato de aceite |
| **Aparece em "rescan_index"?** | Não | Sim — quantas vezes este Milestone foi repetido aparece impresso no PDF |

A separação evita que a agência dispare 80 PRs até "encontrar" um A e se autodeclare pronta. O Milestone é evento explícito, formal, com peso probatório.

## Mecânica de Disputa (sem mediação humana)

Lintty resolve o problema de "agência discorda do laudo" via **três mecanismos automatizados** que substituem mediação:

### 1. Comentários `@lintty-ignore` no código

A agência pode suprimir uma violação reclamada como falso positivo via comentário de código:

```csharp
// @lintty-ignore: LNTY-004 reason="Método é tradução EF Core, não regra de negócio"
public async Task<Order> Save(Order o) { ... }
```

Regras da supressão:
- **Justificativa obrigatória** (>30 caracteres). Justificativas vazias ou genéricas invalidam.
- Justificativa é **impressa no PDF** como "Sumário de Exceções" na primeira página, junto ao email do autor git.

### 2. Hard Locks em violações Críticas

Algumas regras são **não-suprimíveis**, independente de `@lintty-ignore`. Se houver qualquer violação Crítica aberta, o selo de aprovação (PDF assinado) **não é emitido**. São elas:

- LNTY-001 (Domain Layer Isolation)
- LNTY-002 (Persistence Contamination)
- LNTY-007 (Dependency Cycles)

### 3. Cap de impunidade — 10% sobre Médias/Altas

Mesmo nas regras suprimíveis, o número total de supressões válidas em um scan não pode exceder **10% das violações de severidade Média/Alta**. Excedeu, o score cai automaticamente para **F**, independente das supressões.

### Resultado

A agência tem espaço para defender legítimos falsos positivos com transparência, mas não pode "limpar" uma entrega ruim só ignorando tudo. O contratante recebe um sumário de exceções claro. Lintty não precisa julgar quem está certo — os fatos e os limites são auditáveis no laudo.

## O Laudo PDF (artefato central de valor)

O Milestone Audit gera um PDF/A com as seguintes características:

| Atributo | Descrição |
|----------|-----------|
| **Assinatura** | PAdES-B-LT, certificado digital corporativo Lintty |
| **Timestamp** | RFC 3161 via TSA confiável (DigiCert, FreeTSA ou equivalente) |
| **Hash** | SHA-256 do PDF imutável, registrado no audit chain |
| **Distribuição** | URL compartilhável com token HMAC + expiração + download direto |

Conteúdo obrigatório do PDF:
- Sumário executivo com score (A-F)
- Versão do Canon utilizada (ex: "Auditado sob Lintty Canon v1.0.0")
- `inference_signature`: model + snapshot + hashes de prompt/few-shot
- `rescan_index`: quantas vezes este Milestone foi repetido pela mesma entrega
- Lista de violações com referência (arquivo, linha, evidência estruturada)
- Sumário de Exceções: todas as supressões `@lintty-ignore` com justificativa e autor
- Grafo visual de dependências (alto nível)
- Rodapé técnico com `inference_signature` para reprodutibilidade

O PDF é a peça que dá "fé pública" ao laudo sem que Lintty seja parte do contrato jurídico.

## Linha do tempo de uso (jornada do contratante)

1. Contratante cria conta no Lintty, instala o GitHub App na sua organização GitHub.
2. Cria um Projeto, vincula um Repositório, convida a Agência.
3. No momento da criação, o Projeto **pina uma versão imutável do Canon** (ex: v1.0.0). Toggles de regras são editados via dashboard, gerando PR automático no `lintty.yml` do repo.
4. Agência desenvolve normalmente. Em cada PR, Lintty roda scan advisory (se subscription ativa) e posta GitHub Check Status com grade provisória.
5. Quando a entrega está pronta, Contratante (ou Agência autorizada) clica "Solicitar Auditoria Oficial" no painel ou via comando `/lintty-audit` no PR.
6. Motor roda: Build Gate → Roslyn → IA seletiva → Score → PDF assinado.
7. Email com link do PDF é enviado para Contratante e Agência. Painel atualiza tendências.
8. Crédito Pay-per-Scan é debitado da wallet do Contratante.
