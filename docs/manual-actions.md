# Sales Cut — Ações Manuais Pendentes

> Tudo nesta lista **não pode** ser feito por agente automatizado. Lista enxuta — só o que está em pé **agora**.
>
> Marque com `[x]` à medida que for resolvendo. Quando 100% marcado, o Tier 1 está pronto para conversa com investidor ou piloto.
>
> **Modo atual: projeto pessoal (sem PJ).** Lintty roda hoje como projeto operado por pessoa natural. A constituição de PJ é gatilhada pelo primeiro contrato com cliente — não antes. Vide `docs/compliance/lawyer-briefing.md` §8.

---

## 🔴 Imediato — não atrase (lead time externo)

### Advogado LGPD
- [ ] Agendar advogado especialista LGPD (~R$5-15k, ~1 semana de trabalho).
  - **Material para enviar previamente:** `docs/compliance/lawyer-briefing.md` (briefing autônomo, ~15 min de leitura) + `docs/compliance/dpa-template.md` (rascunho v0.1) + `docs/compliance/privacy-policy.md` (versão Modo Projeto).
  - **Pedidos centrais:** (a) validar que a landing pode ir ao ar com Operador pessoa natural; (b) DPA ajustado para evolução roadmap; (c) gatilho exato para constituição da PJ.
  - Sugestão: contatar 2-3 escritórios em paralelo na semana 1 para garantir disponibilidade.

### Identidade legal LGPD da landing (Modo Projeto)
- [ ] Preencher em `docs/compliance/privacy-policy.md` e `landing/privacidade.html` (§1):
  - `[INSERIR NOME COMPLETO DO OPERADOR]`
  - `[INSERIR CPF]`
  - `[INSERIR ENDEREÇO PARA CITAÇÃO]` (pode ser endereço residencial ou caixa postal)
  - `[INSERIR NOME DO ENCARREGADO]` (no Modo Projeto, pode ser o próprio Operador)
  - `[INSERIR E-MAIL COMERCIAL]` (sugestão: `contato@lintty.com`)
  - `privacidade@lintty.com` deve ser caixa real recebendo (mesmo que redirecione).

> **Quando virar PJ:** `[INSERIR RAZÃO SOCIAL]` + `[INSERIR CNPJ]` + `[INSERIR ENDEREÇO FISCAL]` substituem a identificação por CPF acima. Reabra esta seção e republique a Política com numeração de versão atualizada.

---

## 🟡 Antes da primeira demo

### Repos públicos no GitHub
- [ ] Criar `github.com/lintty-demo/the-saint`, `lintty-demo/the-sinner`, `lintty-demo/the-ninja-01`.
- [ ] Push de cada subpasta de `fixtures/the-*/` (incluindo `lintty.yml` e `README.md`; excluindo `bin/` e `obj/` — já no `.gitignore`).
- Justificativa: drama da demo ao vivo (clone na frente do comprador) é parte do roteiro de `docs/sales/talk-track.md` §2:00-8:00.

### Imagens visuais (não-codáveis)
- [x] ~~**Logo Lintty oficial**~~ — *aplicada em 2026-04-28* em `engine/src/Lintty.Docs.Pdf/Resources/lintty-logo.png` (master, usada pelos PDFs do motor) e `landing/assets/lintty-logo.png` (usada no header e footer das páginas). Reflete nos samples regenerados em `docs/sales/samples/`.
- [ ] **Favicon SVG monocromático** baseado na nova logo (`landing/assets/favicon.svg` ainda é placeholder).
- [ ] **OG image 1200×630** para preview social da landing baseada na nova logo (substitui SVG placeholder em `landing/assets/og-image.svg`).
- [ ] **Foto da capa do PDF do Sinner** — abrir `docs/sales/samples/laudo-sinner-sample.pdf` e fotografar/screenshot a página 1 em alta resolução. Vira:
  - Slide 7 do `docs/sales/pitch-deck.md`
  - Imagem hero do `landing/index.html` (substitui o mock SVG atual)
- [ ] **Diagrama do pipeline** (slide 5 do deck): 4 caixas — `Code → Roslyn → JSON → PDF`.
- [ ] **Tabela comparativa** dos slides 3/4 (Today's Approach vs Lintty Solution).
- [ ] **Timeline horizontal** do roadmap (slide 12).

### Time / founder
- [ ] Nome do founder + 1 linha de background técnico — slides 1 e 13 do deck.
- [ ] Lista de advisors (ou remover o bullet) — slide 13.
- [ ] Foto do founder para slide 13 (opcional).

---

## 🟢 Antes de publicar a landing

### Domínio + hospedagem
- [ ] Registrar `lintty.com` (e `lintty.com.br` se não conflitar).
- [ ] Escolher host: **Cloudflare Pages recomendado** (free tier suficiente, CDN global). Alternativas: Vercel, GitHub Pages. Detalhes em `landing/README.md`.
- [ ] Configurar DNS apontando o domínio para o host.
- [ ] HTTPS automático via Cloudflare/Vercel (não precisa configurar manualmente).

### Form de captação de demo
- [ ] Substituir `mailto:` em `landing/index.html` por **Tally** ou **Formspree** (instruções em comentário inline no HTML).
- [ ] Validar que e-mails do form chegam na caixa do founder/comercial.

---

## 🔵 Antes do primeiro piloto pagante

### Constituição da PJ (gatilhada aqui)
- [ ] **Constituir PJ Lintty** com base na orientação do(a) advogado(a). Forma jurídica recomendada (MEI, Sociedade Limitada Unipessoal, etc.) virá da consulta inicial.
  - Disparada pelo primeiro contrato real, não antes — princípio de proporcionalidade.
  - Pré-requisito do(s) próximos itens desta seção (conta PJ, NF-e, contratos).

### Operacional
- [ ] **Conta bancária PJ** ativa + capacidade de receber por TED. (Depende de PJ constituída.)
- [ ] **NF-e/NFS-e setup**: provedor BR (NFE.io, eNotas, ou similar). Pode resolver manualmente até receita virar recorrente.
- [ ] **MSA Lintty ↔ Cliente** redigido e revisado pelo advogado LGPD (mesmo profissional do DPA — sinergia).
- [ ] **DPA assinado** entre Lintty e o piloto (template em `docs/compliance/dpa-template.md` após revisão jurídica).
- [ ] **Republicar Política de Privacidade em "Modo PJ"**: substituir os placeholders de Operador pessoa natural pela identificação da PJ (razão social, CNPJ, endereço fiscal).

### Demo readiness
- [ ] **Ensaiar a demo de 12 min** com cronômetro (roteiro em `docs/sales/talk-track.md`).
- [ ] **Screencast gravado de backup** da demo rodando contra os 3 fixtures e gerando 3 PDFs — caso o motor falhe ao vivo, troca para vídeo sem perder o ritmo.
- [ ] Testar `lintty-engine analyze --solution fixtures/the-saint/Saint.sln --pdf laudo.pdf` no laptop em que a demo será apresentada.

### Pricing decision (com o piloto)
- [ ] Conversa de pricing com o **primeiro prospect**, não antes. Modelo proposto: R$ por scan, tier por LoC (≤50k = 1×, 50-200k = 2×, 200-500k = 4×, >500k cotação). Valor de `1×` calibrado com o que o piloto está disposto a pagar pelo primeiro Milestone.

---

## ⚪ Decisões de produto

- [x] **Foco vertical inicial: VAREJO.** *(Decidido em 2026-04-28.)* Outbound, ICP, casos de exemplo na demo e copy de pitch deck devem priorizar o varejo de grande porte. Outras verticais (financeiro, seguros) ficam como adjacências reativas — não como foco ativo.
- [x] **Geografia inicial: BRASIL.** *(Decidido em 2026-04-28.)* MVP atende clientes brasileiros, NF-e brasileira, foro brasileiro, LGPD. Expansão LATAM/EU vira V1+.
- [ ] **ICP exato dentro de varejo brasileiro**: CTO/Diretor de TI de varejista de grande porte? Head de engenharia de marketplace? Líder de engenharia em rede de e-commerce? — Pendente: refinar entre os tipos para definir copy e canais de outbound.
  - **Pista útil:** começar pela rede pessoal do operador. O primeiro piloto sai de quem você consegue marcar uma reunião com 1 mensagem.

---

## 🚦 Sinais de "go" para sair do Sales Cut

Do `docs/12-sales-cut.md` §8. Antes de iniciar Sprints seguintes, aguarde **pelo menos um**:

- [ ] 1+ **piloto pagante assinou** (mesmo simbólico — $5k é sinal real).
- [ ] **Investidor com term sheet** aceitou tese baseado nos artefatos do Tier 1.
- [ ] **3+ prospects qualificados** em pipeline avançado (call de descoberta feita, dor confirmada, interesse explícito).

Sem nenhum desses sinais → **iterar pitch e ICP, não construir produto**.

---

## 📁 Mapa de onde está o quê

| Artefato | Caminho | Para quê |
|----------|---------|----------|
| Motor Roslyn (build OK, 8/8 testes verdes) | `engine/` | Demo CLI + análise type-aware |
| PDF Reporter QuestPDF (15/15 testes) | `engine/src/Lintty.Engine.Reporter/` | Geração do laudo PDF determinístico via flag `--pdf` |
| Saint / Sinner / Ninja-01 | `fixtures/the-*/` | Demo ao vivo + regression suite |
| Snapshots de regressão | `fixtures/the-*/expected.json` | Garantia de determinismo |
| ADR motor | `docs/adr/0001-motor-skeleton.md` | Spec do motor |
| ADR PDF reporter | `docs/adr/0003-pdf-reporter.md` | Spec do QuestPDF integration |
| Pitch deck (15 slides) | `docs/sales/pitch-deck.md` | Investidor / piloto |
| Talk track (12 min) | `docs/sales/talk-track.md` | Roteiro da demo |
| Spec do laudo | `docs/sales/laudo-mock.md` | Define o conteúdo do PDF que o motor gera |
| Copy da landing | `docs/sales/landing-copy.md` | Origem do `landing/index.html` |
| Briefing do advogado | `docs/compliance/lawyer-briefing.md` | Material autônomo para enviar ao(à) advogado(a) antes da primeira reunião |
| DPA template | `docs/compliance/dpa-template.md` | Template para advogado revisar |
| Política de Privacidade (Modo Projeto) | `docs/compliance/privacy-policy.md` | Página `/privacidade` |
| Landing estática | `landing/index.html`, `landing/privacidade.html` | Pronta para publicar (após preencher placeholders) |
| Samples de laudo (logo nova) | `docs/sales/samples/laudo-{saint,sinner}-sample.pdf` | Print/foto vira slide 7 do deck |

---

**Última atualização:** 2026-04-28 (logo aplicada, Modo Projeto, vertical=varejo, geo=Brasil).
