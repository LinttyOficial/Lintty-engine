# DPA — Acordo de Tratamento de Dados (Template Inicial)

> **Status:** RASCUNHO v0.1 — pendente revisão por advogado especialista LGPD.
> **Custo estimado de revisão:** R$5.000–15.000 (template reaproveitável, ~1 semana de calendário).
> **Owner:** security-compliance + jurídico externo.
> **Aplicabilidade:** primeiro piloto pagante e clientes subsequentes até que cliente enterprise demande versão custom.
> **Marcações:** trechos com `[REVISAR COM ADVOGADO ESPECIALISTA LGPD]` exigem ajuste antes de uso em contrato.
>
> **Modo Projeto (2026-04-28):** o Lintty é hoje um projeto pessoal sem Pessoa Jurídica formalizada. Este template **assume que a PJ Lintty estará constituída** no momento da primeira assinatura — pré-requisito comercial usual de cliente PJ que precisará receber NF-e/NFS-e. O briefing em `lawyer-briefing.md` §8 endereça com o(a) advogado(a) o gatilho exato para a constituição. Caso seja necessária versão "Pessoa Natural" do template (ex: piloto simbólico), o(a) advogado(a) deve produzir variante específica.

---

## Acordo de Tratamento de Dados Pessoais

Este Acordo de Tratamento de Dados Pessoais ("**Acordo**" ou "**DPA**") é parte integrante e indissociável do Contrato de Prestação de Serviços firmado entre as Partes ("**Contrato Principal**") e regula as condições de tratamento de dados pessoais nos termos da Lei nº 13.709/2018 (Lei Geral de Proteção de Dados Pessoais — "**LGPD**").

**Controlador:** [INSERIR RAZÃO SOCIAL DO CLIENTE], inscrito no CNPJ sob nº [INSERIR CNPJ], com sede em [INSERIR ENDEREÇO] ("**Controlador**").

**Operador:** [INSERIR RAZÃO SOCIAL LINTTY], inscrito no CNPJ sob nº [INSERIR CNPJ LINTTY], com sede em [INSERIR ENDEREÇO LINTTY] ("**Operador**" ou "**Lintty**").

---

### 1. Definições

Para os fins deste Acordo, aplicam-se as definições da LGPD (art. 5º), com destaque para:

1.1. **Controlador:** pessoa natural ou jurídica a quem competem as decisões referentes ao tratamento de dados pessoais (LGPD art. 5º, VI). No contexto deste Acordo, é o cliente do Lintty que fornece o código fonte para análise.

1.2. **Operador:** pessoa natural ou jurídica que realiza o tratamento de dados pessoais em nome do Controlador (LGPD art. 5º, VII). No contexto deste Acordo, é o Lintty.

1.3. **Sub-Operador:** terceiro contratado pelo Operador para auxiliar na prestação dos serviços, e que pode ter acesso a dados pessoais tratados em nome do Controlador.

1.4. **Tratamento:** toda operação realizada com dados pessoais (LGPD art. 5º, X).

1.5. **Dado Pessoal:** informação relacionada a pessoa natural identificada ou identificável (LGPD art. 5º, I).

1.6. **Incidente de Segurança:** qualquer evento adverso, confirmado ou suspeito, relacionado à violação de segurança que possa acarretar risco aos titulares de dados pessoais.

1.7. **ANPD:** Autoridade Nacional de Proteção de Dados.

---

### 2. Objeto

2.1. O Operador realizará, em nome do Controlador, **análise arquitetural automatizada do código fonte fornecido pelo Controlador**, com produção de laudo técnico estruturado (relatório de violações, score arquitetural e evidências). [REVISAR COM ADVOGADO ESPECIALISTA LGPD: confirmar se a descrição é suficientemente específica para o art. 6º, II — finalidade.]

2.2. O tratamento de dados pessoais previsto neste Acordo limita-se ao **estritamente necessário** para a execução do objeto e está adstrito às instruções documentadas do Controlador, na forma do art. 39 da LGPD.

---

### 3. Natureza dos dados tratados

3.1. O Controlador transmitirá ao Operador **código fonte de software de sua titularidade ou cuja análise tenha legitimidade para autorizar**.

3.2. O Operador reconhece que o código fonte poderá conter, de forma incidental, **dados pessoais embutidos**, incluindo, sem se limitar a:
- Comentários no código mencionando nomes ou contatos de desenvolvedores;
- Strings literais com endereços de email, telefones ou identificadores;
- Nomes de variáveis, classes ou métodos derivados de nomes próprios;
- Dados de teste (fixtures) eventualmente versionados.

3.3. O Operador **não solicita** dados pessoais como entrada do serviço. Qualquer dado pessoal presente no código é incidental e tratado sob o princípio da **minimização** (LGPD art. 6º, III).

3.4. Categorias de dados pessoais que podem ocorrer incidentalmente: dados cadastrais (nome, email, telefone) e identificadores técnicos. **Dados sensíveis** (LGPD art. 5º, II) não são esperados; caso o Controlador identifique presença de dados sensíveis no código, deve notificar o Operador previamente para avaliação conjunta. [REVISAR COM ADVOGADO ESPECIALISTA LGPD: redação sobre dados sensíveis.]

3.5. **Titulares dos dados:** colaboradores do Controlador, terceiros mencionados no código, ou usuários cujos dados de teste estejam versionados.

---

### 4. Sub-Operadores

4.1. O Controlador autoriza o Operador a contratar os Sub-Operadores listados abaixo, observadas as condições mínimas de proteção previstas neste Acordo:

| Sub-Operador | Finalidade | Localização do tratamento | Condição contratual |
|---|---|---|---|
| **Google Cloud Platform** (Google Cloud Brasil Computação e Serviços Ltda. e afiliadas) | Hospedagem da infraestrutura de análise (Cloud Run, Cloud SQL, Cloud Storage) | `us-east1` (Carolina do Sul, EUA) | DPA padrão GCP + Cloud KMS para chaves + criptografia em repouso (CMEK) |
| **Anthropic, PBC** | Inferência do modelo de linguagem para a etapa de "advogado de defesa" do laudo | EUA | **ZDR (Zero Data Retention) contratualizado como obrigatório**: o Sub-Operador não persiste código fonte transmitido, não o utiliza para treinamento de modelos, e o expurga ao final da inferência. Vide §4.3. |

4.2. **Transferência internacional de dados.** O Controlador reconhece que parte do tratamento ocorre nos Estados Unidos (GCP `us-east1` e Anthropic). O Operador adota como salvaguarda: (i) cláusulas contratuais com os Sub-Operadores, alinhadas ao art. 33 da LGPD; (ii) criptografia em trânsito (TLS 1.3) e em repouso; (iii) ZDR contratual no caso da Anthropic. [REVISAR COM ADVOGADO ESPECIALISTA LGPD: confirmar que cláusula está alinhada ao guia da ANPD sobre transferência internacional, e adicionar referência específica caso haja decisão de adequação ou cláusulas-padrão da ANPD aplicáveis na data da assinatura.]

4.3. **Cláusula ZDR Anthropic.** O Operador declara e garante que o contrato firmado com Anthropic, PBC inclui obrigação contratual de Zero Data Retention, abrangendo: (a) não persistência do conteúdo das requisições e respostas além da janela de inferência; (b) não utilização do conteúdo para treinamento de modelos; (c) restrição de acesso interno do Sub-Operador ao mínimo operacional. Cópia da cláusula ZDR pode ser exibida ao Controlador, mediante acordo de confidencialidade, a pedido formal.

4.4. **Alterações no rol de Sub-Operadores.** O Operador notificará o Controlador, com **30 dias de antecedência mínima**, sobre inclusão de novo Sub-Operador ou substituição relevante. O Controlador poderá, motivadamente, opor-se à mudança; persistindo o impasse, qualquer das Partes poderá rescindir o Contrato Principal sem multa.

---

### 5. Análise efêmera (cláusula central)

5.1. O **Operador declara expressamente que o código fonte recebido não é persistido após a análise**. A arquitetura técnica do serviço opera segundo o seguinte modelo:

- O código é clonado para um **workspace temporário em ambiente serverless (Google Cloud Run gen2 com runtime gVisor)**;
- A análise é executada em até alguns minutos por scan;
- Ao final do scan, o **container é destruído**, liberando memória e descartando o filesystem temporário sem persistência;
- Tokens de acesso ao repositório do Controlador são de curta duração e são **revogados** ao término do scan.

5.2. Após a destruição do workspace temporário, **integram a cadeia de auditoria do Lintty apenas os seguintes elementos derivados**, que **não constituem o código fonte**:

- **Hash criptográfico** (SHA-256) do conteúdo analisado (impressão digital, irreversível);
- **Metadados estruturados**: identificador da regra acionada (`rule_id`), severidade, caminho do arquivo, linha, tipo do nó AST, métricas agregadas;
- **Snippets de violação** estritamente limitados ao trecho infrator, com retenção de **90 dias** e expurgo automatizado por política de ciclo de vida.

5.3. **Laudos PDF assinados** são retidos pelo prazo necessário à finalidade probatória contratual entre Controlador e seus terceiros. O laudo contém metadados e snippets, **não o código integral**.

5.4. **Logs de aplicação** identificam scans por `customer_id` e `scan_id` pseudo-anonimizados; **não registram conteúdo de código fonte nem prompts/respostas de inferência LLM**. Retenção: 90 dias.

5.5. [REVISAR COM ADVOGADO ESPECIALISTA LGPD: validar que a redação acima caracteriza adequadamente a "minimização" e a "necessidade" do art. 6º da LGPD, e que a retenção de 90 dias para snippets é defensável como legítimo interesse do Controlador para correção das violações.]

---

### 6. Medidas técnicas e organizacionais

6.1. O Operador adota e mantém as seguintes medidas, observadas as melhores práticas de mercado e em proporção aos riscos:

| Camada | Medida |
|---|---|
| **Trânsito** | TLS 1.3 obrigatório em todas as comunicações externas |
| **Repouso** | Criptografia AES-256 com chaves gerenciadas pelo Cloud KMS (CMEK), com rotação documentada |
| **Chaves de assinatura** | Cloud KMS, com restrições de acesso via IAM mínimo necessário |
| **Identidade** | Princípio do menor privilégio em IAM; service accounts dedicados por serviço; autenticação multifator obrigatória para administradores |
| **Rede** | Arquitetura zero-trust entre serviços; egress restrito por allow-list; VPC privada para componentes críticos |
| **Logs** | Pseudo-anonimização (uso de `customer_id` e `scan_id` ao invés de identificadores diretos); retenção limitada |
| **Sandbox** | Execução em runtime gVisor com isolamento adicional; sem persistência cross-execution |
| **Audit** | Cadeia imutável de eventos de auditoria com encadeamento por hash (hash-chain) |
| **Backup** | Backups criptografados; retenção e expurgo conforme tabela de retenção |
| **Vulnerabilidades** | Análise contínua de dependências (Container Analysis, scanners equivalentes) |

6.2. O Operador compromete-se a manter equipe interna treinada em segurança da informação e LGPD, com revisão periódica de políticas internas.

---

### 7. Notificação de Incidente de Segurança

7.1. O Operador notificará o Controlador sobre Incidente de Segurança envolvendo dados pessoais tratados em seu nome em prazo **não superior a 24 (vinte e quatro) horas** contadas da **detecção** do incidente. [REVISAR COM ADVOGADO ESPECIALISTA LGPD: confirmar compatibilidade com o art. 48 da LGPD e Resolução CD/ANPD nº 15/2024 sobre comunicação de incidentes.]

7.2. **Canal de notificação:** `seguranca@lintty.com`, com cópia ao representante designado pelo Controlador.

7.3. **Conteúdo mínimo da notificação:**
- Natureza do incidente (acesso não autorizado, vazamento, alteração, destruição etc.);
- Categorias e número aproximado de titulares afetados;
- Categorias e número aproximado de registros de dados pessoais afetados;
- Consequências prováveis;
- Medidas adotadas ou a adotar para conter o incidente e mitigar efeitos;
- Pessoa de contato para esclarecimentos adicionais.

7.4. O Operador cooperará com o Controlador para o cumprimento das obrigações deste último perante a ANPD e os titulares dos dados, incluindo a comunicação prevista no art. 48 da LGPD.

---

### 8. Direitos do titular (LGPD art. 18)

8.1. O Operador cooperará com o Controlador para viabilizar o atendimento dos direitos dos titulares, em especial:

- **Acesso** aos dados (art. 18, II);
- **Correção** de dados incompletos, inexatos ou desatualizados (art. 18, III);
- **Anonimização, bloqueio ou eliminação** de dados desnecessários, excessivos ou tratados em desconformidade (art. 18, IV);
- **Eliminação** dos dados pessoais (art. 18, VI);
- **Portabilidade** (art. 18, V);
- **Revogação do consentimento** (art. 18, IX), quando aplicável.

8.2. **Prazo de resposta do Operador ao Controlador:** até **5 (cinco) dias úteis** contados da requisição formal, sem prejuízo do prazo do Controlador perante o titular.

8.3. **Canal de requisição:** `privacidade@lintty.com`.

8.4. [REVISAR COM ADVOGADO ESPECIALISTA LGPD: avaliar se cabe diferenciar prazos por tipo de direito; confirmar redação sobre cooperação para portabilidade no caso de dados que são metadados derivados, não o código original.]

---

### 9. Auditoria

9.1. O Controlador poderá auditar o cumprimento deste Acordo pelo Operador **uma vez por ano civil**, mediante notificação prévia de **30 (trinta) dias**.

9.2. O escopo da auditoria limita-se aos **controles relevantes ao tratamento de dados pessoais realizado em nome do Controlador**, sendo expressamente excluídos:
- Tratamentos realizados em nome de outros clientes do Operador;
- Informações de propriedade intelectual ou segredo de negócio do Operador não relacionadas ao tratamento;
- Dados de outros Controladores.

9.3. **Forma da auditoria.** O Controlador poderá optar entre:
- (a) Aceite de **relatório SOC 2** ou equivalente emitido por auditor independente, quando disponível;
- (b) Questionário de due diligence respondido pelo Operador;
- (c) Auditoria presencial ou remota, custeada pelo Controlador, conduzida por auditor qualificado e sujeito a NDA.

9.4. Eventuais não-conformidades identificadas serão objeto de **plano de remediação acordado** entre as Partes em até 30 dias da entrega do relatório.

9.5. [REVISAR COM ADVOGADO ESPECIALISTA LGPD: balanceamento entre direito de auditoria do Controlador e proteção de segredo de negócio do Operador. Verificar redação sobre custeio.]

---

### 10. Vigência, rescisão e devolução/destruição de dados

10.1. **Vigência.** Este Acordo vige enquanto o Contrato Principal estiver em vigor.

10.2. **Rescisão.** Encerrado o Contrato Principal, por qualquer motivo, aplica-se o disposto no item 10.3.

10.3. **Devolução ou destruição.** Em até **30 (trinta) dias** do término do Contrato Principal, o Operador, conforme instrução escrita do Controlador:

- (a) **Eliminará** todos os dados pessoais e suas cópias, exceto quando obrigação legal exigir conservação (LGPD art. 16); **ou**
- (b) **Devolverá** os dados pessoais ao Controlador em formato estruturado e legível por máquina.

10.4. **Permanência por obrigação legal.** Permanecerão arquivados, em formato pseudo-anonimizado e protegido, exclusivamente:
- Hashes e metadados estruturados constantes da cadeia de auditoria, pelo prazo de **7 (sete) anos**, em razão de obrigação legal de manutenção de evidência probatória dos laudos emitidos.
- [REVISAR COM ADVOGADO ESPECIALISTA LGPD: validar fundamentação legal da retenção de 7 anos para audit chain — base: legítimo interesse + cumprimento de obrigação legal/regulatória.]

10.5. **Atestado de eliminação.** Ao término da operação prevista em 10.3 (a), o Operador entregará ao Controlador atestado formal de eliminação de dados.

---

### 11. Disposições gerais

11.1. **Encarregado pelo Tratamento de Dados Pessoais (DPO).** O Operador designa, para os fins do art. 41 da LGPD: [INSERIR NOME E CONTATO DO DPO LINTTY — pode ser DPO terceirizado no MVP/Sales Cut]. O Controlador comunicará ao Operador a designação do seu próprio Encarregado.

11.2. **Confidencialidade.** As Partes manterão sigilo sobre as informações trocadas para execução deste Acordo, inclusive após seu término.

11.3. **Lei aplicável e foro.** Este Acordo rege-se pelas leis da República Federativa do Brasil. Fica eleito o foro da comarca de [INSERIR FORO], com renúncia expressa a qualquer outro, por mais privilegiado que seja.

11.4. **Integralidade.** Este Acordo prevalece sobre qualquer disposição conflitante do Contrato Principal no tocante ao tratamento de dados pessoais.

11.5. [REVISAR COM ADVOGADO ESPECIALISTA LGPD: avaliar inclusão de cláusula de limitação de responsabilidade específica para LGPD, multa contratual em caso de descumprimento, e regime de indenização.]

---

**Local e data:** [LOCAL], [DATA].

**Controlador:** ___________________________

**Operador (Lintty):** ___________________________

---

> **Nota de manutenção do template:**
>
> 1. Toda inserção de novo Sub-Operador requer revisão da §4.1.
> 2. Mudança de região de tratamento (ex: GCP `us-east1` → `southamerica-east1`) requer revisão de §4.2.
> 3. Alteração na política de retenção (`docs/futuro/security-compliance.md` §8 — preservada como roadmap V1+) requer revisão de §5 e §10. No V0 a retenção é simples: clone descartado em ≤ 60s no Web Inspector; sem persistência de código no caminho CLI.
> 4. Após primeira assinatura por cliente, usar como baseline e versionar. Cliente enterprise pode demandar customizações específicas — manter este template como fallback.
