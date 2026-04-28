# 11 — Glossário

Termos, acrônimos e conceitos centrais do Lintty.

---

## A

**AnalysisRun** — Tabela 1:1 com `Scan` que armazena dados efêmeros e métricas brutas da execução. Separada por motivos de teardown rápido.

**Anthropic** — Provedor de LLM (Claude) usado pelo Lintty no MVP. 100% via Enterprise Agreement com Zero Data Retention.

**API Gateway** — Cloud Run Service que expõe API pública do Lintty (autenticação, autorização, rate limiting).

**Artifact Registry** — Serviço GCP usado para (a) armazenar imagens Docker do Lintty, (b) proxiar pacotes NuGet de `nuget.org` para a sandbox sem egress externo.

**AST (Abstract Syntax Tree)** — Representação em árvore do código fonte usada pelo Roslyn. Lintty trabalha em type-aware AST + grafo semântico.

**AuditEvent** — Linha em `audit_events`, parte do hash chain imutável.

**Auto-consistência** — Estratégia de chamar a LLM duas vezes com prompts diferentes (juiz + advogado de defesa) para mitigar viés.

---

## B

**Bounded Context** — Conceito DDD: porção do domínio com modelo coerente próprio. Usado pelo Lintty para grafo separado de ciclos (LNTY-007 segundo pass).

**Build Gate** — Etapa do pipeline do motor: `dotnet restore + dotnet build`. Falha aqui aborta o scan e estorna crédito.

**Bucket Lock** — Feature do GCS que torna conteúdo imutável por X anos, mesmo com IAM root. Usado em `lintty-audit-archive-*`.

---

## C

**Canon** — O conjunto opinionado de regras arquiteturais que o Lintty aplica. Versionado imutavelmente (`v1.0.0`, `v1.1.0`...).

**CanonRule** — Tabela com definição de cada regra do Canon (código, nome, severidade, motor, etc).

**Chain of Thought (CoT)** — Técnica de prompting onde a IA raciocina passo a passo antes de dar veredito. No Lintty é forçado via campos no schema JSON (`analysis.detected_*`, `counter_arguments_considered`, `conclusion`).

**Cloud NAT** — Serviço GCP de NAT gerenciado. Lintty usa para egress whitelisted da sandbox.

**Cloud Run Job** — Serviço GCP serverless para containers que rodam até o fim e terminam. Usado para sandbox de análise (gen2 com gVisor).

**Cloud Run Service** — Serviço GCP serverless para containers HTTP que escalam baseado em tráfego. Usado para API, dashboard, webhook receiver, orchestrator.

**CMEK (Customer-Managed Encryption Key)** — Encryption keys gerenciadas pelo cliente em Cloud KMS. Usadas em Cloud SQL, GCS, Secret Manager.

**Confidence Score** — Número 0..1 retornado pela LLM indicando confiança no veredito. Threshold para 2ª chamada: 0.85 (VIOLATION) / 0.80 (NO_VIOLATION).

**Constant Folding** — Recurso do Roslyn (`SemanticModel.GetConstantValue`) que resolve expressões constantes em tempo de compilação. Crítico para LNTY-002 detectar SQL via concat.

**Continuous Feedback (Subscription)** — SKU mensal do Lintty: assinatura por repositório que dá scans ilimitados em PRs (modo advisory).

**Contratante** — Empresa enterprise que contrata uma agência para desenvolver software e usa o Lintty para auditá-lo. **Quem paga** os créditos Pay-per-Scan.

**CreditWallet** — Saldo de créditos Pay-per-Scan da Organization (contratante).

**CSV / JSON / canonical_json** — Formatos de serialização. `canonical_json` é determinístico (chaves ordenadas) — usado para hash chain.

---

## D

**DAG (Directed Acyclic Graph)** — Grafo direcionado sem ciclos. Lintty usa o DAG de projetos para paralelizar análise.

**Dashboard** — Painel web do Lintty (Cloud Run Service) onde contratante e agência interagem com scans, configuração, billing.

**DDD (Domain-Driven Design)** — Abordagem de design de software focada em modelagem do domínio. Canon do Lintty é fortemente alinhado a princípios DDD táticos.

**DLQ (Dead Letter Queue)** — Subscription do Pub/Sub para mensagens que falharam após N retries.

**DPA (Data Processing Agreement)** — Acordo de processamento de dados, exigido por LGPD/GDPR. Template Lintty é assinado por todos os clientes.

**Drift Monitoring** — Detecção de mudanças no comportamento do modelo LLM ao longo do tempo. Inclui métricas semanais + Golden Suite diário em produção.

---

## E

**Egress** — Tráfego de saída da rede. Sandbox do Lintty tem egress restrito a whitelist (GitHub, Anthropic, Artifact Registry, Google APIs).

**Ephemeral Analysis** — Princípio core: código fonte do cliente é destruído após análise. Apenas metadata, métricas e (limitados) snippets persistem.

**Exception Summary** — Seção do PDF do laudo listando todas as supressões `@lintty-ignore` válidas.

**External Contributor** — Role da Agência dentro de um Project. Acesso granular ao código mas não pode mudar Canon ou comprar créditos.

---

## F

**Fail-Fast** — Princípio: qualquer falha fundamental aborta o scan imediatamente em vez de degradar para análise parcial. Aplicado a `WorkspaceFailed`, layer tagging, build gate.

**Few-shots** — Exemplos no prompt da LLM que ensinam o padrão de resposta esperado. No Lintty: 4-6 por regra cobrindo 4 categorias.

**Fingerprint** — SHA-256 hash usado como chave de cache. Composto de regra + slice normalizado + canon_version.

**Firewall (egress rule)** — Regra de Cloud Firewall que controla quais IPs/portas a sandbox pode acessar.

---

## G

**gVisor** — User-space kernel desenvolvido pelo Google que provê isolamento entre container e kernel host. Default em Cloud Run gen2.

**GCS (Google Cloud Storage)** — Object storage. Lintty usa para PDFs, snippets, audit archive, backups.

**GitHub App** — Integração registrada no GitHub. Lintty App é instalado pela Organization do contratante para acesso aos repos.

**GitOps** — Padrão onde estado da configuração vive no Git (não em DB). Usado para `lintty.yml` — dashboard gera PR, merge ativa.

**Golden Test Suite** — Conjunto de fixtures C# (Saint, Sinner, Ninja) que o motor deve avaliar exatamente como esperado. CI gate obrigatório.

**Grade** — Nota A-F derivada do score numérico. Aparece no PDF.

---

## H

**Hard Lock** — Regra do Canon que **não pode ser suprimida**. Se houver violação aberta, o selo de aprovação não é emitido (grade = F). Aplica-se a LNTY-001/002/007.

**Hash Chain** — Estrutura de dados onde cada linha contém hash SHA-256 da linha anterior. Lintty usa em `audit_events` para imutabilidade.

**Hexagonal Architecture (Ports & Adapters)** — Estilo arquitetural que isola domínio de detalhes técnicos via interfaces (ports). Canon do Lintty é alinhado a isso.

---

## I

**IAM (Identity and Access Management)** — Sistema de permissões do GCP. Lintty usa least-privilege em toda service account.

**Inconclusive** — Veredito quando Prompt A (juiz) e Prompt B (advogado de defesa) discordam. Não conta para o score, mas reasonings vão para o PDF como "Considerations".

**Inference Signature** — Tupla `{model, snapshot, system_prompt_hash, few_shot_set_hash, canon_version, temperature}` que identifica unicamente a configuração da LLM no momento do scan. Garante reprodutibilidade.

---

## L

**Laudo** — Documento PDF assinado emitido após Milestone Audit. Tem validade probatória via assinatura PAdES + timestamp TSA.

**Layer Tagging** — Classificação dos `.csproj` em camadas (domain, application, infrastructure, presentation). Convention + Override via `lintty.yml`. Fail-fast.

**LLM (Large Language Model)** — Modelo de linguagem usado para inferência semântica nas regras LNTY-004 e LNTY-005. No MVP: Anthropic Claude Sonnet 4.6.

**LNTY-XXX** — Código de regra do Canon Lintty. Ex: LNTY-001 = Domain Layer Isolation.

**LoC (Lines of Code)** — Linhas físicas de código. Usado para pricing tiering (até 50k = 1 crédito).

---

## M

**Milestone Audit** — Auditoria oficial solicitada explicitamente, que consome 1+ créditos Pay-per-Scan e gera PDF assinado. Contraposto a PR scan (advisory).

**MSBuildLocator** — Library do Roslyn que precisa ser inicializada antes do `MSBuildWorkspace`. Sem isso, motor não compila no Linux.

**MSBuildWorkspace** — API do Roslyn para carregar Solutions com referências e modelo semântico completo.

---

## N

**Ninja (Test Case)** — Categoria adversarial da Golden Suite. Código que tenta burlar regras do Canon. Ninja #1, #2, #3 estão no MVP; #4+ em V1.1.

**NuGet** — Package manager do .NET. Lintty proxia `nuget.org` via Artifact Registry para evitar egress externo.

**NuGet Bridge** — Trick onde Domain referencia NuGet privado que re-exporta tipos de Infrastructure. Detectado via symbol-level pass de LNTY-001 (Ninja #2).

---

## O

**OpenTelemetry** — Padrão para tracing/métricas. Usado pelo Lintty em todos os serviços.

**Org Policy** — Constraints do GCP impostas no nível da Organization. Lintty usa para travar configurações inseguras (ex: `sql.restrictPublicIp=true`).

**Organization (Lintty)** — Tabela representando empresa cliente (contratante ou agência). Multi-tenancy hierarchy começa aqui.

---

## P

**PAdES (PDF Advanced Electronic Signatures)** — Padrão ETSI para assinatura digital em PDFs. Lintty usa PAdES-B-LT (Long-Term).

**Pay-per-Scan** — Modelo principal de receita do Lintty. 1 crédito = 1 Milestone Audit.

**Persona (LLM)** — Definição clínica do "papel" da IA no system prompt: "Lintty Semantic Engine, clinical, factual, definitive". Reforçada por few-shots.

**Pinning (Canon)** — Cada Project trava uma versão imutável do Canon ao ser criado. Atualizações são opt-in.

**Pinning (LLM Snapshot)** — Modelo Anthropic é referenciado por snapshot exato (ex: `claude-sonnet-4-6-20260201`), não "latest", para reprodutibilidade.

**PITR (Point-In-Time Recovery)** — Feature do Cloud SQL que permite restaurar DB para qualquer momento dentro da janela de retenção (7d MVP / 30d V1).

**PR Scan** — Scan executado em Pull Request. Modo advisory, alimentado por subscription mensal. Não gera PDF.

**Project (Lintty)** — Aggregate root central. Liga contratante + agência + repositories + canon_version pinada.

**Prompt Caching** — Feature da Anthropic API que reduz custo de tokens repetidos. Lintty usa em System Prompt + Few-shots (estáticos).

**Prompt Injection** — Tentativa de fazer LLM ignorar instruções via conteúdo do código analisado. Defesa: tags `<code_under_review>` + instrução "INERT TEXT".

**Pub/Sub** — Sistema de mensageria do GCP. Lintty usa para comunicação assíncrona entre planes (Control e Data).

**PSC (Private Service Connect)** — Conexão privada para serviços managed (ex: Cloud SQL) sem expor IP público.

---

## R

**Required Status Check** — Feature do GitHub Branch Protection que pode bloquear merge se Lintty não aprovar. Configuração do contratante.

**Repository (Lintty)** — Vínculo entre Project e um repo GitHub específico. Subscription Continuous Feedback é por Repository.

**Rescan Index** — Quantas vezes um Milestone foi repetido para o mesmo commit. Impresso no PDF para transparência (revela tentativas de "tunar" até passar).

**Roslyn** — Compiler platform da Microsoft para C#/VB. Base do motor estático do Lintty.

**RPO (Recovery Point Objective)** — Quanto dado pode ser perdido em desastre. Lintty MVP: 1 hora.

**RTO (Recovery Time Objective)** — Quanto tempo até voltar online. Lintty MVP: 4 horas.

---

## S

**Sandbox** — Ambiente isolado onde scan roda. Cloud Run Job com gVisor. Destruída após cada scan.

**SCC (Strongly Connected Component)** — Em grafos: conjunto de nós onde existe caminho entre todos. SCC com >1 nó = ciclo (LNTY-007 detecta via Tarjan).

**Schema (JSON Schema)** — Validação estrutural. Lintty usa em `LinttySemanticInferenceResult` (output da LLM).

**Score Raw** — Número 0..100 antes de arredondar. Score final é arredondado em passos de 5.

**SDK (.NET)** — Software Development Kit do .NET. Lintty motor roda em `dotnet/sdk:9.0`.

**Semantic Slicing** — Etapa do motor que extrai snippet + metadados factuais para enviar à LLM.

**SLO (Service Level Objective)** — Meta de performance/disponibilidade. Lintty: scan completion ≥99%, p95 <15min, API ≥99.5%.

**SOC 2** — Padrão de compliance de SaaS. Lintty visa Type I no mês 12.

**Suppression** — Comentário `@lintty-ignore` no código que suprime uma violação. Sujeita a hard lock e cap de 10%.

**Subscription (Continuous Feedback)** — SKU mensal do Lintty para PR scans ilimitados.

---

## T

**Tarjan's Algorithm** — Algoritmo para encontrar SCCs em grafo. Usado em LNTY-007.

**Temperature (LLM)** — Parâmetro de sampling. Lintty usa `0` para máxima determinismo.

**Tier (LoC)** — Categoria de tamanho de solution para pricing. ≤50k=1, 50-200k=2, 200k-1M=4, >1M=quote.

**Toggle (Canon Rule)** — On/off por Project sobre uma regra do Canon. Hard locks (Crítica) não são toggleáveis.

**Tool Use** — Feature da Anthropic API que força output a casar com schema JSON via "function calling". Lintty usa em vez de "modo JSON" puro.

**TSA (Timestamp Authority)** — Serviço RFC 3161 que carimba hash com timestamp confiável. Lintty usa em PAdES-B-LT.

**Type-aware Analysis** — Análise que entende relações de tipo (não apenas sintaxe). Roslyn provê via `SemanticModel`.

---

## V

**VPC (Virtual Private Cloud)** — Rede privada do GCP. Lintty tem VPC por ambiente (dev, staging, prod), sem IPs públicos em compute/data.

---

## W

**Webhook** — Notificação HTTP enviada pelo GitHub quando eventos ocorrem (PR opened, push). Recebida pelo `Lintty.WebhookReceiver`.

**WIF (Workload Identity Federation)** — Feature do GCP que permite GitHub Actions assumir identidade GCP sem chaves estáticas.

**Workspace (MSBuildWorkspace)** — Container do Roslyn para Solution. Subscrever `WorkspaceFailed` é obrigatório (fail-fast).

---

## Z

**ZDR (Zero Data Retention)** — Cláusula contratual com Anthropic garantindo que dados enviados à API não são retidos para treinamento. Pré-requisito do MVP.
