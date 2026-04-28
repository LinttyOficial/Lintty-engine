# Lintty Landing

Landing page estatica do Lintty (Sales Cut Tier 1, abril 2026).

Stack: HTML estatico + Tailwind CDN. Sem build step, sem Node, sem React.

## Estrutura

```
landing/
  index.html          Homepage (hero, como funciona, Saint vs Sinner, defensibilidade, CTA)
  privacidade.html    Politica de Privacidade (LGPD)
  assets/
    favicon.svg       Favicon SVG (zero peso, qualquer DPI)
    og-image.svg      Social preview placeholder
  README.md           Este arquivo
```

## Rodar local

Nao precisa de servidor. Abra `index.html` direto no browser:

- macOS: `open index.html`
- Linux: `xdg-open index.html`
- Windows: duplo-clique no `index.html`

Para hot-reload em dev (opcional):

```bash
npx serve landing
# ou
python3 -m http.server -d landing 8080
```

Tailwind CDN faz JIT no carregamento da pagina. Em conexao lenta o primeiro paint
pode ter um flash de estilo nao aplicado. Aceitavel no Sales Cut; substituir por
build dedicado em V1.

## Deploy

**Cloudflare Pages** (recomendado: TLS gratis, CDN global, zero cold start):

1. `git push` para o repo conectado.
2. No painel Cloudflare Pages: **Create project** &rarr; **Connect to Git**.
3. Configuracao de build:
   - Build command: *(em branco)*
   - Build output directory: `landing`
4. Configurar dominio custom `lintty.com` em **Custom domains**.

DNS: apontar `lintty.com` (ou `www.lintty.com`) para o `*.pages.dev` gerado.

## CTA / formulario

O botao **Solicite uma demo** usa `mailto:contato@lintty.com` com subject e body
pre-preenchidos. Fallback do Sales Cut.

Para trocar por Tally/Formspree:

1. Criar form em [tally.so](https://tally.so) ou [formspree.io](https://formspree.io).
2. No `index.html`, trocar todos os `href="mailto:contato@lintty.com?..."` pelo URL
   do form (ex: `https://tally.so/r/XXXX`).
3. Remover os parametros `?subject=&body=` (Tally/Formspree ignoram).

## Performance e privacidade

- Sem analytics de terceiros (sem GA, sem Pixel). Sem cookie banner necessario.
- Inter via Google Fonts com `display=swap` e `preconnect`. Substituivel por
  `system-ui` puro se Lighthouse cair abaixo de 95.
- Sem imagens raster. Hero usa SVG inline e CSS.
- Tailwind CDN e ~80kb gzip. Aceitavel para Sales Cut; mover para build dedicado
  quando o trafego justificar.

## Placeholders ainda abertos

Hoje a landing roda em **Modo Projeto** (operador pessoa natural, sem CNPJ).
Os placeholders abaixo precisam ser preenchidos antes de publicar:

- `[INSERIR NOME COMPLETO DO OPERADOR]`, `[INSERIR CPF]`, `[INSERIR ENDERECO
  PARA CITACAO]` na privacidade (§1).
- `[INSERIR NOME DO ENCARREGADO]` na privacidade (§9).
- `[INSERIR EMAIL COMERCIAL]` na privacidade (§11).
- `og-image.svg` e placeholder; substituir por PNG 1200x630 com a logo Lintty
  oficial quando a identidade visual final estiver pronta.

Quando a PJ for constituida (vide `docs/compliance/lawyer-briefing.md` §8),
a privacidade ganha uma nova versao com razao social/CNPJ/endereco da PJ.

## i18n

Hoje so PT-BR. Estrutura preparada para EN-US no futuro: copy isolada na arvore
de secoes, lang attribute no `<html>`. Quando ativar EN-US, criar `index.en.html`
e adicionar `<link rel="alternate" hreflang="en" href="...">` em ambas.
