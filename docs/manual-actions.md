# V0 — Ações Manuais Pendentes

> Tudo nesta lista **não pode** ser feito por agente automatizado. Lista enxuta — só o que está em pé **agora**, alinhado ao `docs/15-roadmap-curto.md`.
>
> Marque com `[x]` à medida que for resolvendo. Quando 100% marcado, o V0 está pronto para conversa com investidor ou piloto.
>
> **Modo atual: projeto pessoal (sem PJ).** Lintty roda hoje como projeto operado por pessoa natural. A constituição de PJ é gatilhada pelo primeiro contrato com cliente — não antes. Vide `docs/compliance/lawyer-briefing.md` §8.

---

## 🔴 Imediato — não atrase (lead time externo)

### Advogado LGPD
- [ ] Agendar advogado especialista LGPD (~R$5–15k, ~1 semana de trabalho).
  - **Material para enviar previamente:** `docs/compliance/lawyer-briefing.md` (briefing autônomo, ~15 min de leitura) + `docs/compliance/dpa-template.md` (rascunho v0.1) + `docs/compliance/privacy-policy.md` (versão Modo Projeto).
  - **Pedidos centrais:** (a) validar que a landing pode ir ao ar com Operador pessoa natural; (b) DPA ajustado para os **dois caminhos** (CLI Self-Service = fornecedor de software; Web Inspector = Operador de tratamento efêmero); (c) gatilho exato para constituição da PJ.
  - Sugestão: contatar 2–3 escritórios em paralelo na semana 1 para garantir disponibilidade.

### Identidade legal LGPD da landing (Modo Projeto)
- [ ] Preencher em `docs/compliance/privacy-policy.md` e `landing/privacidade.html` (§1):
  - `[INSERIR NOME COMPLETO DO OPERADOR]`
  - `[INSERIR CPF]`
  - `[INSERIR ENDEREÇO PARA CITAÇÃO]` (residencial ou caixa postal)
  - `[INSERIR NOME DO ENCARREGADO]` (no Modo Projeto, pode ser o próprio Operador)
  - `[INSERIR E-MAIL COMERCIAL]` (sugestão: `contato@lintty.com`)
  - `privacidade@lintty.com` deve ser caixa real recebendo (mesmo que redirecione via Cloudflare Email Routing).

> **Quando virar PJ:** `[INSERIR RAZÃO SOCIAL]` + `[INSERIR CNPJ]` + `[INSERIR ENDEREÇO FISCAL]` substituem a identificação por CPF acima. Reabra esta seção e republique a Política com numeração de versão atualizada.

---

## 🟡 Sprint 1 (semanas 1–2): publicar o que já existe

### Org GitHub e repos públicos
- [X] Criar org `lintty/` no GitHub (ou usar a existente, decidir).
- [X] Criar repo `lintty/lintty-engine` (ou similar) — canal oficial de releases do CLI.
- [X] Criar `github.com/lintty-demo/the-saint`, `lintty-demo/the-sinner`, `lintty-demo/the-ninja-01`.
- [ ] Push de cada subpasta de `fixtures/the-*/` para o repo público correspondente (incluindo `lintty.yml` e `README.md`; excluindo `bin/` e `obj/` — já no `.gitignore`).
- Justificativa: drama da demo ao vivo (clone na frente do comprador) é parte do roteiro de `docs/sales/talk-track.md` §2:30–8:30. **E** as mesmas URLs são as URLs de teste do Web Inspector (`docs/13-web-inspector.md` §10).

### Domínio + hospedagem da landing
- [ ] Registrar `lintty.com` (Cloudflare Registrar) e/ou `lintty.com.br` (Registro.br).
- [ ] **Cloudflare Pages** apontado para o branch que serve `landing/` — free tier suficiente, CDN global, HTTPS automático.
- [ ] Configurar DNS apontando o domínio para o host.
- [ ] Configurar `contato@lintty.com` e `privacidade@lintty.com` via Cloudflare Email Routing → caixa real do operador.

### Form de captação de demo
- [ ] Substituir `mailto:` em `landing/index.html` por **Tally** ou **Formspree** (instruções em comentário inline no HTML).
- [ ] Validar que e-mails do form chegam na caixa do founder/comercial.

### Pipeline de release do CLI
- [ ] Criar workflow `release.yml` no repo `lintty/lintty-engine` — matriz Win/Linux/macOS-x64/macOS-arm64, `dotnet publish` self-contained, smoke test cross-platform (`hash_content` igual entre OSes), publica no GitHub Releases com `SHA256SUMS.txt`. Spec em `docs/14-cli-distribution.md` §6.
- [ ] Primeira release manual `v0.1.0` para validar o pipeline.
- [ ] Página `lintty.com/cli` (HTML estático na landing) com botões de download por OS, comando de verificação sha256, quickstart. Spec em `docs/14-cli-distribution.md` §7.

---

## 🟠 Sprint 2 (semanas 3–5): Web Inspector

### Decisão de hospedagem
- [ ] Decidir com `software-architect`: **VM única (Hetzner/DigitalOcean)** vs **Cloud Run service único**. Documentar em ADR. Custo idle alvo < US$ 30/mês.

### Domínio do Web Inspector
- [ ] Subdomínio `inspect.lintty.com` (ou path `lintty.com/inspect`) → host do backend ASP.NET. HTTPS automático.

### Smoke tests do Web Inspector
- [ ] Cola `https://github.com/lintty-demo/the-saint` no `/inspect` → recebe PDF score A em < 90s.
- [ ] Cola `https://github.com/lintty-demo/the-sinner` → recebe PDF score F em < 120s.
- [ ] Cola `https://github.com/lintty-demo/the-ninja-01` → detecta LNTY-002 via constant folding.
- [ ] **Gate cruzado de determinismo**: PDF do Web Inspector é byte-idêntico ao PDF do CLI local rodando contra o mesmo `fixtures/the-*/` na mesma versão do motor.
- [ ] Repo privado com PAT temporário funciona end-to-end.
- [ ] Validação manual: clone é apagado em ≤ 60s após `status: completed` (verificar com `ls /var/lintty/jobs/`).

---

## 🟢 Sprint 3 (semanas 6–7): material de venda

### Imagens visuais (não-codáveis)
- [x] ~~**Logo Lintty oficial**~~ — *aplicada em 2026-04-28* em `engine/src/Lintty.Docs.Pdf/Resources/lintty-logo.png` (master, usada pelos PDFs do motor) e `landing/assets/lintty-logo.png` (usada no header e footer das páginas). Reflete nos samples regenerados em `docs/sales/samples/`.
- [ ] **Favicon SVG monocromático** baseado na logo (`landing/assets/favicon.svg` ainda é placeholder).
- [ ] **OG image 1200×630** para preview social da landing baseada na logo (substitui SVG placeholder em `landing/assets/og-image.svg`).
- [ ] **Foto da capa do PDF do Sinner** — abrir `docs/sales/samples/laudo-sinner-sample.pdf` e fotografar/screenshot a página 1 em alta resolução. Vira:
  - Slide 7 do `docs/sales/pitch-deck.md`
  - Imagem hero do `landing/index.html` (substitui o mock SVG atual)
- [ ] **Diagrama do pipeline** (slide 5 do deck): 4 caixas — `Code → Roslyn → JSON → PDF` (sem caixa de IA — pivô Zero-IA).
- [ ] **Tabela comparativa** dos slides 3/4 (Today's Approach vs Lintty Solution).
- [ ] **Timeline horizontal** do roadmap (slide 12) com 3 fases: V0 → V1 (PDF assinado + dashboard + LLM opcional) → Escala (multi-stack + SOC 2).

### Time / founder
- [ ] Nome do founder + 1 linha de background técnico — slides 1 e 13 do deck.
- [ ] Lista de advisors (ou remover o bullet) — slide 13.
- [ ] Foto do founder para slide 13 (opcional).

### Demo readiness
- [ ] **Ensaiar a demo de 12 min** com cronômetro (roteiro em `docs/sales/talk-track.md` — atualizado para 12 min sem bloco LLM).
- [ ] **Screencast gravado de backup** (~5 min) da demo rodando contra os 3 fixtures via **CLI** + uma demo curta do **Web Inspector** com URL de `lintty-demo/the-sinner`. Caso o motor falhe ao vivo, troca para vídeo sem perder o ritmo.
- [ ] Testar `lintty-engine analyze --solution fixtures/the-saint/Saint.sln --pdf laudo.pdf` no laptop em que a demo será apresentada.

---

## 🔵 Sprint 4 (semanas 8–10): primeiro piloto e jurídico

### Constituição da PJ (gatilhada aqui)
- [ ] **Constituir PJ Lintty** com base na orientação do(a) advogado(a). Forma jurídica recomendada (MEI, Sociedade Limitada Unipessoal, etc.) virá da consulta inicial.
  - Disparada pelo primeiro contrato real, não antes — princípio de proporcionalidade.
  - Pré-requisito do(s) próximos itens desta seção (conta PJ, NF-e, contratos).

### Operacional
- [ ] **Conta bancária PJ** ativa + capacidade de receber por TED. (Depende de PJ constituída.)
- [ ] **NF-e/NFS-e setup**: provedor BR (NFE.io, eNotas, ou similar). Pode resolver manualmente até receita virar recorrente.
- [ ] **MSA Lintty ↔ Cliente** redigido e revisado pelo advogado LGPD (mesmo profissional do DPA — sinergia).
- [ ] **DPA assinado** entre Lintty e o piloto (template em `docs/compliance/dpa-template.md` após revisão jurídica). DPA cobre os dois caminhos (CLI = fornecedor de software; Web Inspector = Operador de tratamento efêmero).
- [ ] **Republicar Política de Privacidade em "Modo PJ"**: substituir os placeholders de Operador pessoa natural pela identificação da PJ (razão social, CNPJ, endereço fiscal). **Mencionar explicitamente o fluxo do Web Inspector** (clone efêmero, TTL artefatos).

### Pricing decision (com o piloto)
- [ ] Conversa de pricing com o **primeiro prospect**, não antes. Modelo proposto: R$ por scan, tier por LoC (≤50k = 1×, 50–200k = 2×, 200–500k = 4×, >500k cotação). Valor de `1×` calibrado com o que o piloto está disposto a pagar pelo primeiro Milestone.

---

## ⚪ Decisões de produto

- [x] **Pivô Zero-IA.** *(Decidido em 2026-04-27.)* V0 roda 100% determinístico — argumento de venda, não falta. IA volta como roadmap V1+.
- [x] **CLI Self-Service como default.** *(Decidido em 2026-04-28 via [ADR 0005](adr/0005-distribution-model.md).)* "Código nunca sai do equipamento do cliente" vira terceira perna da defensibilidade.
- [x] **Foco vertical inicial: VAREJO.** *(Decidido em 2026-04-28.)* Outbound, ICP, casos de exemplo na demo e copy de pitch deck devem priorizar o varejo de grande porte. Outras verticais (financeiro, seguros) ficam como adjacências reativas — não como foco ativo.
- [x] **Geografia inicial: BRASIL.** *(Decidido em 2026-04-28.)* MVP atende clientes brasileiros, NF-e brasileira, foro brasileiro, LGPD. Expansão LATAM/EU vira V1+.
- [x] **Resize de docs: V0 enxuto + `docs/futuro/` para V1+.** *(Decidido em 2026-04-30.)* Documentação operacional foca CLI + Web Inspector; planejamento aspiracional fica preservado mas isolado.
- [ ] **ICP exato dentro de varejo brasileiro**: CTO/Diretor de TI de varejista de grande porte? Head de engenharia de marketplace? Líder de engenharia em rede de e-commerce? — Pendente: refinar entre os tipos para definir copy e canais de outbound.
  - **Pista útil:** começar pela rede pessoal do operador. O primeiro piloto sai de quem você consegue marcar uma reunião com 1 mensagem.

---

## 🚦 Sinais de "go" para sair do V0

Do `docs/15-roadmap-curto.md` §Sprint 4 + §Métricas. Antes de iniciar reativação de V1+ (LLM, GCP completo, dashboard), aguarde **pelo menos um**:

- [ ] 1+ **piloto pagante assinou** (mesmo simbólico — $5k é sinal real).
- [ ] **Investidor com term sheet** aceitou tese baseado nos artefatos do V0.
- [ ] **3+ prospects qualificados** em pipeline avançado (call de descoberta feita, dor confirmada, interesse explícito).

Sem nenhum desses sinais → **iterar pitch e ICP, não construir produto**.

---

## 📁 Mapa de onde está o quê

| Artefato | Caminho | Para quê |
|----------|---------|----------|
| Motor Roslyn (build OK, testes verdes) | `engine/src/Lintty.Engine.Core/` | Análise type-aware das 7 regras V0 |
| CLI standalone (`lintty-engine analyze`) | `engine/src/Lintty.Engine.Cli/` | Caminho 1 de uso (default) |
| PDF Reporter QuestPDF (gate de determinismo) | `engine/src/Lintty.Engine.Reporter/` | Geração do laudo PDF determinístico via flag `--pdf` |
| White-label brand PDF | `engine/src/Lintty.Docs.Pdf/` | PDFs institucionais (ADRs, briefings) — não é o laudo |
| Saint / Sinner / Ninja-01 | `fixtures/the-*/` | Demo ao vivo + regression suite |
| Snapshots de regressão | `fixtures/the-*/expected.json` | Garantia de determinismo |
| **Spec do V0** | `docs/00-onde-estamos.md` | TL;DR de 1 página |
| Visão de produto V0 | `docs/01-product-vision.md` | Posicionamento e dois caminhos |
| Canon (9 regras, 7 ativas no V0) | `docs/02-canon-v1.md` | Severidades, hard locks, supressões |
| Spec do motor CLI | `docs/03-motor-cli.md` | Pipeline, contrato CLI, determinismo |
| **Spec do Web Inspector** | `docs/13-web-inspector.md` | Caminho 2 de uso (a construir) |
| Plano de distribuição do CLI | `docs/14-cli-distribution.md` | Empacotamento, GitHub Releases, sha256 |
| **Roadmap real** | `docs/15-roadmap-curto.md` | 4 sprints reais nas próximas 12 semanas |
| Sales Cut (talk-track + deck) | `docs/12-sales-cut.md` | Demo de 12 min, estrutura do deck |
| Pitch deck (15 slides) | `docs/sales/pitch-deck.md` | Investidor / piloto |
| Talk track (12 min) | `docs/sales/talk-track.md` | Roteiro da demo |
| Spec do laudo | `docs/sales/laudo-mock.md` | Define o conteúdo do PDF que o motor gera |
| Copy da landing | `docs/sales/landing-copy.md` | Origem do `landing/index.html` + `/cli` + `/inspect` |
| Briefing do advogado | `docs/compliance/lawyer-briefing.md` | Material autônomo para enviar ao(à) advogado(a) antes da primeira reunião |
| DPA template | `docs/compliance/dpa-template.md` | Template para advogado revisar |
| Política de Privacidade (Modo Projeto) | `docs/compliance/privacy-policy.md` | Página `/privacidade` |
| Landing estática | `landing/index.html`, `landing/privacidade.html` | Pronta para publicar (após preencher placeholders) |
| Samples de laudo | `docs/sales/samples/laudo-{saint,sinner}-sample.pdf` | Print/foto vira slide 7 do deck |
| ADR motor | `docs/adr/0001-motor-skeleton.md` | Spec do motor |
| ADR PDF reporter | `docs/adr/0003-pdf-reporter.md` | Spec do QuestPDF, hash_content |
| ADR brand PDF | `docs/adr/0004-brand-pdf-template.md` | Padrão de PDF institucional |
| ADR distribuição | `docs/adr/0005-distribution-model.md` | CLI Self-Service como default |
| **Roadmap V1+ aspiracional** | `docs/futuro/` | Planejamento preservado (LLM, GCP, SOC 2, PAdES) — **não construir antes de sinal de "go"** |

---

**Última atualização:** 2026-04-30 (resize de docs + adição do Web Inspector como segundo caminho de uso + alinhamento dos agentes).
