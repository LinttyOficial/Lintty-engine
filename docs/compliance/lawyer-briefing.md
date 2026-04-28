# Briefing para o(a) Advogado(a) — Lintty

> **Propósito deste documento:** dar ao(à) advogado(a) especialista em LGPD um panorama completo do que é o Lintty, como o software opera, e que dados pessoais podem ser tocados. A partir disto, o(a) advogado(a) tem subsídio para revisar `dpa-template.md` e `privacy-policy.md`.
>
> **Pergunta de fundo que o briefing responde:** "como redigir um DPA + Política de Privacidade adequados para um software que recebe código fonte de terceiros, processa-o localmente, e emite um laudo PDF?"
>
> **Modo atual:** projeto pessoal (não há ainda Pessoa Jurídica). O briefing inclui §10 com perguntas específicas sobre quando essa formalização passa a ser obrigatória.
>
> **Tempo estimado de leitura:** 15-20 minutos. Documento autônomo — não precisa abrir o Blueprint completo.

---

## 1. O que é o Lintty em uma frase

Lintty é um software de **análise arquitetural automatizada** para projetos escritos em .NET. Ele recebe uma `solution` (`.sln`) — o pacote de código fonte que define um sistema .NET — e produz um **laudo PDF** descrevendo se a arquitetura segue boas práticas previamente acordadas (o "Canon").

A imagem mental é a de um **revisor automatizado de arquitetura**: substitui horas de revisão manual por um relatório objetivo, baseado em regras técnicas explícitas.

---

## 2. Quem usa, e por quê

O caso de uso comercial é o seguinte:

1. **Empresa A** (chamada "**Contratante**" — ex: um grande varejista) contrata uma **agência de software** (chamada "**Agência**" — ex: uma fábrica de software terceirizada) para construir um sistema em .NET.
2. Ao final de cada **Milestone** (entrega contratual), a Contratante quer saber: *"o que recebi foi bem feito do ponto de vista arquitetural?"*.
3. A Contratante **paga ao Lintty** para rodar a análise sobre o código entregue pela Agência. Recebe um PDF com a nota.
4. Esse PDF vai junto do termo de aceite contratual entre Contratante e Agência. **O Lintty não é parte do contrato entre eles** — é apenas um insumo técnico.

**Quem é nosso cliente:** a Contratante (a empresa que paga pelo desenvolvimento). **Quem nos fornece o código:** a Contratante (que tem direito legítimo sobre o código que recebeu da Agência).

**Não somos:**
- Escrow financeiro (não custodiamos pagamento entre as partes).
- Árbitro jurídico (não decidimos disputas).
- Consultoria humana (não há revisor humano lendo o código por trás).

---

## 3. O que o software faz tecnicamente (linguagem para leigo)

### 3.1. O fluxo de uma análise

1. **Entrada:** o operador (no Sales Cut, eu mesmo, no laptop) recebe da Contratante o código fonte. Pode ser:
   - Um link de repositório `git` privado, com permissão de leitura concedida pontualmente.
   - Um arquivo `.zip` enviado por email/upload.
2. **Processamento:** o software roda **localmente, no laptop do operador**. Não há envio para a nuvem, não há terceiros envolvidos, não há provedor externo de inteligência artificial.
3. **O que o software faz com o código:**
   - Compila o código em memória (usa o compilador oficial do .NET, chamado *Roslyn*).
   - Aplica 7 regras de boas práticas arquiteturais sobre o código compilado (ex: "não pode haver consulta SQL escrita à mão dentro da camada de Domínio").
   - Calcula uma nota de A a F.
   - Gera um arquivo PDF com a nota, a lista de problemas encontrados, e o trecho de código onde cada problema está.
4. **Saída:** o operador entrega o PDF à Contratante. O código fonte original é **descartado** após a análise (vide §5).

### 3.2. O que o software **não** faz (importante)

- **Não envia código a serviços externos de IA.** O Sales Cut roda 100% local — não há OpenAI, não há Anthropic, não há provedor de modelo de linguagem na cadeia.
- **Não armazena código em nuvem.** Sales Cut não tem servidor próprio. Análise é local; código é descartado.
- **Não modifica o código.** Apenas lê, analisa e emite relatório. É uma operação read-only do ponto de vista do código fonte.
- **Não compartilha o código com terceiros.** Nenhum sub-operador tem acesso ao código fonte do cliente no Sales Cut.

### 3.3. Em qual versão estamos hoje vs. roadmap

| Componente | Hoje (Sales Cut) | Roadmap (V1+) |
|---|---|---|
| Onde roda | Laptop do operador | Cloud Run (Google Cloud Platform, EUA) |
| IA / LLM | **Não há** | Anthropic Claude, sob ZDR contratualizado |
| Armazenamento de código | **Nenhum** (descartado após análise) | Workspace efêmero (gVisor sandbox), descartado ao final do scan |
| Hash da análise | Sim, no rodapé do PDF | Sim, em audit hash-chain imutável |
| Assinatura digital do PDF | **Não** (ainda) | PAdES-B-LT + TSA RFC 3161 |
| Multi-tenant SaaS web | Não | Sim |

**Para o(a) advogado(a):** o DPA deve cobrir o **estado-alvo** (V1+) — porque é o que o cliente vai assinar pensando em médio prazo. Mas com **cláusulas de transição** explicitando que, no início, partes do tratamento (ex: ZDR Anthropic, Cloud Run) ainda não estão ativas. A versão atual do `dpa-template.md` já tenta isso; precisamos validar se está adequada juridicamente.

---

## 4. Que dados pessoais aparecem nesse fluxo

Esta é a questão central da LGPD. O Lintty **não solicita** dados pessoais como entrada do serviço. Mas dados pessoais podem aparecer **incidentalmente** no código fonte, e essa categoria precisa ser tratada com cuidado.

### 4.1. Coleta direta (landing page, canais de contato)

Pelo formulário de "Solicite uma demo" da landing page (`lintty.com`):

| Dado | Origem | Finalidade | Base legal |
|---|---|---|---|
| Nome | Formulário | Personalizar resposta comercial | Consentimento + interesse pré-contratual |
| Email empresarial | Formulário | Canal de retorno | Idem |
| Empresa do remetente | Formulário | Qualificação | Idem |
| Mensagem livre | Formulário | Compreender o interesse | Idem |

**Não coletamos:** CPF, RG, telefone, dados financeiros, dados sensíveis (LGPD art. 5º, II).

### 4.2. Coleta incidental (código fonte do cliente)

Quando a Contratante nos entrega o código fonte para análise, esse código pode conter, **incidentalmente**, dados pessoais:

| Categoria | Exemplo | Frequência típica |
|---|---|---|
| Comentários no código mencionando desenvolvedores | `// TODO: João revisar isso` | Comum |
| Strings literais com email/telefone | `const SUPPORT = "joao@empresa.com"` | Comum |
| Identificadores derivados de nomes próprios | Nome de classes, variáveis | Raro |
| **Dados de teste versionados** | Fixtures com nomes/CPFs falsos | Ocasional |

**Princípio operacional do Lintty:** o software trata o código como **opaco** do ponto de vista de dados pessoais — não os indexa, não os extrai, não os transforma. Apenas verifica regras arquiteturais sobre a estrutura do código.

**Nota crítica:** o operador (eu) **não lê o código fonte do cliente** durante operação normal. O software roda automatizado e produz um PDF. O operador apenas dispara o comando e entrega o PDF. Caso seja necessário diagnosticar um falso positivo, o operador pode olhar o trecho específico apontado pelo software — mas isso é exceção, não rotina.

**Dados sensíveis (LGPD art. 5º, II):** não esperamos que apareçam. Se a Contratante identificar presença de dado sensível no código (ex: dados de saúde de pacientes em comentários), pediremos notificação prévia para tratamento conjunto. Esta cláusula está em `dpa-template.md` §3.4.

### 4.3. Dados gerados pelo Lintty (não pessoais)

O software produz, ao final da análise:

- **Hash criptográfico (SHA-256)** do conteúdo analisado — irreversível, não expõe o código original.
- **Metadados estruturados:** identificador da regra (`rule_id`), severidade, caminho do arquivo, número de linha, métricas agregadas.
- **Snippets de violação:** trechos pequenos do código onde o problema foi detectado (ex: 1-3 linhas).

**Importante:** os snippets de violação **podem incidentalmente conter dados pessoais** se o código violador tiver, por exemplo, uma string de email hardcoded. No PDF do laudo, o snippet aparece textualmente. Isto precisa ser endereçado no DPA — defendendo a tese de que (a) o tratamento é incidental, (b) o titular indireto tem interesse legítimo na correção da violação, (c) o snippet é o mínimo necessário para evidenciar o problema.

---

## 5. Onde os dados ficam (retenção)

| Categoria | Onde fica | Por quanto tempo | O que acontece no fim |
|---|---|---|---|
| Código fonte do cliente | RAM/disco do laptop do operador, durante a análise (~minutos) | Apenas a duração do scan | **Descartado** ao final. Pasta de trabalho deletada |
| Hash da análise | No PDF emitido + no laptop do operador | Indefinido (vai junto do laudo) | Permanece com o cliente como prova de integridade |
| Snippets de violação | Dentro do PDF emitido | Igual ao prazo de retenção do PDF | Permanece com o cliente |
| Metadados estruturados (rule_id, line, etc) | Dentro do PDF emitido | Igual ao prazo de retenção do PDF | Permanece com o cliente |
| **PDF do laudo** | Cliente decide a retenção (é dele) | A definir com cliente | Cliente descarta quando achar adequado |
| Leads de contato (landing) | Email do operador (Gmail) | 12 meses sem contato → exclusão | Exclusão automática |
| Logs do laptop do operador | Disco local | 90 dias | Pseudo-anonimização e expurgo |

**Princípio:** **não persistimos código fonte de cliente em lugar nenhum**. O laptop do operador é o ambiente de execução; o código vive lá durante a análise (em torno de 1-3 minutos) e é apagado na sequência. O PDF emitido vira propriedade do cliente. O Lintty mantém apenas o hash e metadados, dentro do próprio PDF.

---

## 6. Quem mais toca os dados (sub-operadores)

### 6.1. Sales Cut (hoje)

**Nenhum sub-operador toca código fonte de cliente.** Análise é local no laptop do operador.

Sub-operadores envolvidos **apenas no fluxo da landing/email**:

| Sub-operador | Função | Localização | Salvaguarda |
|---|---|---|---|
| Provedor de email comercial (atual: Gmail) | Comunicação comercial | EUA | Política de retenção do Google + 24 meses |
| Hospedagem da landing (proposto: Cloudflare Pages) | Servir o site `lintty.com` | Edge global (CDN) | TLS 1.3, sem coleta de dados pessoais |

### 6.2. Roadmap V1+ (não está vigente hoje)

Quando o produto evoluir para SaaS web, entram:

| Sub-operador | Função | Localização | Salvaguarda |
|---|---|---|---|
| Google Cloud Platform | Hospedagem da plataforma de análise (Cloud Run, Cloud SQL, Cloud Storage) | EUA (`us-east1`) | DPA padrão GCP, criptografia em trânsito e repouso, CMEK |
| Anthropic, PBC | Inferência de modelo de linguagem para análise semântica avançada | EUA | **ZDR contratualizado:** não persiste conteúdo das requisições, não usa para treino |

**Para o(a) advogado(a):** o DPA precisa diferenciar claramente o que é "Sales Cut hoje" (sem GCP, sem Anthropic) e o que é "V1+ contratualmente possível, com aviso prévio de 30 dias" (com GCP e Anthropic). A versão atual do `dpa-template.md` §4 já tenta isso — validar se a redação cobre adequadamente os dois cenários.

---

## 7. Risco regulatório e modelo de ameaça (em linguagem natural)

### 7.1. Cenários de risco identificados

| Risco | Probabilidade | Impacto | Mitigação atual |
|---|---|---|---|
| Vazamento de código fonte do cliente para terceiros | Baixa | Alto (perda de confiança, possível processo) | Análise local + descarte imediato + nenhum sub-operador no Sales Cut |
| Dados pessoais incidentais expostos no PDF do laudo | Média (acontece se código violador tem string de email) | Médio (titular indireto pode reclamar) | Snippet limitado ao mínimo necessário; PDF entregue só ao cliente |
| Cliente alegar que o laudo "não foi auditável" | Baixa | Médio | Hash sha256 do PDF no rodapé; em V1+ vira PAdES + audit chain |
| Operador (eu) ter o laptop comprometido | Baixa (precaução de segurança individual) | Alto | Disco criptografado, MFA, sem persistência cross-scan, isolamento por pasta |
| Pedido de exercício de direitos de titular (LGPD art. 18) | Baixa-média | Baixo (basta apagar lead/PDF) | Canal `privacidade@lintty.com`, prazo 15 dias |
| Incidente de segurança no laptop | Baixa | Médio | Notificação ao Controlador em até 24h; cooperação com ANPD |

### 7.2. O que estamos pedindo ao(à) advogado(a) para validar

1. A tese de **"análise efêmera local"** como base argumentativa contra a necessidade de retenção mínima de logs (LGPD princípio de minimização — art. 6º, III).
2. A redação do DPA sobre **dados pessoais incidentais** no código fonte (§3 do template).
3. A diferenciação entre **Sales Cut atual (sem sub-operadores)** vs **V1+ futuro (com GCP/Anthropic)**, e o regime de notificação prévia para inclusão de novo sub-operador.
4. A retenção de **7 anos** para hashes e metadados (proposta no §10.4 do DPA) como necessária para evidência probatória dos laudos emitidos — **isto é o ponto mais delicado** e precisa de validação de fundamentação legal.
5. O regime de **notificação de incidente em 24h** (§7.1 do DPA) frente à Resolução CD/ANPD nº 15/2024.
6. O regime de **auditoria** pelo Controlador (§9 do DPA) e o balanceamento com proteção de segredo de negócio.
7. A **transferência internacional** de dados (§4.2 do DPA) — alinhamento com o guia da ANPD e cláusulas-padrão aplicáveis na data da assinatura.

---

## 8. Estrutura jurídica atual do projeto (essencial)

**O Lintty é hoje um projeto pessoal, não uma Pessoa Jurídica.** Isto afeta diretamente como o DPA e a Política de Privacidade são redigidos.

### 8.1. Fatos

- O operador é **pessoa natural** (vou chamar de "Operador") — não há CNPJ ativo.
- Não há funcionários, sócios, advisors formalizados.
- Não há receita ainda. Estamos em **fase de validação comercial** (Sales Cut Tier 1).
- Não há contratos de cliente assinados.
- A landing `lintty.com` ainda não foi publicada.
- O DPA ainda não foi assinado por ninguém.

### 8.2. Implicações que precisam de orientação

Quero, do(a) advogado(a), respostas claras e proporcionais ao estágio:

1. **A LGPD permite Pessoa Natural ser Operador?** A leitura literal do art. 5º, VII (LGPD) admite "pessoa natural ou jurídica". Isto se confirma em prática? Há requisitos formais adicionais (CPF declarado, endereço fiscal, etc.)?
2. **A landing pode ser publicada sem CNPJ?** A coleta de leads (nome + email + empresa + mensagem) por uma pessoa natural, com finalidade pré-contratual, é juridicamente sustentável até a constituição da PJ?
3. **Em que momento a constituição de PJ vira obrigatória?** Hipóteses prováveis:
   - Quando assinar o primeiro contrato com cliente PJ (que vai exigir NF-e e CNPJ do prestador)?
   - Quando faturar acima de algum limite?
   - Quando abrir conta bancária empresarial?
   - Antes de assinar o primeiro DPA com Controlador, o(a) advogado(a) entende que precisamos ter PJ formalizada?
4. **Qual a forma jurídica recomendada?** MEI, EI, EIRELI (extinta?), Sociedade Limitada Unipessoal? Para um projeto que pode evoluir para SaaS B2B com investidor — o que recomenda?
5. **Posso assinar contrato/DPA como pessoa natural com cliente PJ?** Em casos onde o cliente aceitar (ex: piloto simbólico, valor baixo)?
6. **Como redigir a Política de Privacidade no estágio atual?** Identificar o "Controlador" (no sentido da landing) como o operador pessoa natural com nome+CPF? Ou como "Lintty (projeto em fase de constituição)" com endereço de contato?
7. **DPA template referencia "razão social do Operador" como placeholder.** Mantemos como placeholder a preencher na assinatura — assumindo que entre hoje e a primeira assinatura a PJ estará constituída — ou criamos versão "Pessoa Natural" do template para casos especiais?

### 8.3. O que vai mudar quando virarmos PJ

Quando a PJ for constituída (após orientação do(a) advogado(a)):

- Política de Privacidade ganha razão social, CNPJ, endereço, foro de eleição.
- DPA template idem.
- Surgem obrigações de NF-e/NFS-e, contabilidade mensal, etc. (parte fiscal — fora do escopo LGPD).
- Pode ser que algumas cláusulas do DPA atual precisem ser ajustadas (ex: foro, regime tributário implícito).

**Sugestão operacional:** o(a) advogado(a) entrega **dois pacotes**:
- (a) **Pacote "modo projeto":** versão da Política de Privacidade que pode ir ao ar **agora**, com a landing, identificando o Operador como pessoa natural responsável.
- (b) **Pacote "modo PJ":** DPA template + revisão da Política de Privacidade para entrar em vigor **a partir da constituição da PJ** (provavelmente em paralelo, prazo curto).

---

## 9. Documentos anexos que o(a) advogado(a) deve revisar

| Arquivo | O que é | Status |
|---|---|---|
| `docs/compliance/dpa-template.md` | Rascunho v0.1 do DPA, com pontos marcados `[REVISAR COM ADVOGADO]` | Aguarda revisão |
| `docs/compliance/privacy-policy.md` | Política de Privacidade v1.0, com placeholders `[INSERIR ...]` | Aguarda revisão + ajuste para "modo projeto" |
| `landing/privacidade.html` | Versão HTML da Política, espelho do markdown acima | Sincronizada |
| `docs/12-sales-cut.md` | Documento de escopo de produto (informação contextual — leitura opcional) | Para referência |
| Este briefing | Resumo operacional (você está lendo) | Documento autônomo |

---

## 10. Perguntas-piloto para a primeira reunião

Sugiro pautar a reunião inicial com o(a) advogado(a) por estas perguntas:

1. **(Bloqueio imediato)** Posso publicar a landing `lintty.com` com a Política de Privacidade no formato "Operador pessoa natural", sem CNPJ?
2. **(Bloqueio antes do primeiro piloto)** Em que momento da minha jornada comercial a constituição de PJ vira obrigatória? Antes do primeiro DPA assinado, antes do primeiro pagamento, antes do primeiro contrato?
3. **(Validação técnica)** A tese de "análise efêmera local com descarte imediato do código" é adequada para suportar minimização do art. 6º LGPD? Existe risco regulatório que estou subestimando?
4. **(Validação técnica)** Snippets de código violador no PDF do laudo, que podem incidentalmente conter dados pessoais (ex: email hardcoded), são tratáveis sob qual base legal? Legítimo interesse do Controlador para correção da violação?
5. **(Estratégia de longo prazo)** O DPA template está adequado para a evolução roadmap → V1+ (com GCP e Anthropic)? Como redigir cláusulas de transição limpas?
6. **(Custo e prazo)** Qual o prazo realista para revisão do DPA + Política de Privacidade nos dois pacotes ("modo projeto" + "modo PJ")? E qual o orçamento esperado?
7. **(Operacional)** Você tem experiência com contratos B2B de software/SaaS no Brasil? Indica algum modelo de MSA (Master Services Agreement) compatível com este DPA?

---

## 11. Como me contatar para tirar dúvidas

- **Email:** `[INSERIR EMAIL DO OPERADOR]`
- **Forma preferida de comunicação:** email para registro escrito; chamadas pontuais para validação.
- **Material de referência adicional disponível mediante pedido:**
  - Blueprint completo do produto (`docs/01-product-vision.md` até `docs/11-glossary.md`).
  - Plano de ZDR Anthropic preservado para V1+ (`docs/compliance/zdr-anthropic-plan.md`).
  - Spec do produto Sales Cut (`docs/12-sales-cut.md`).

---

**Última atualização:** 2026-04-28.
**Autor:** Operador do Lintty.
**Versão:** 1.0 (modo projeto pessoal).
