# Landing Page — Copy PT-BR

> Versão: Tier 1 Sales Cut, semana 3-4 (2026-04-27).
> Destinatário: `frontend-dev` (este arquivo é insumo de copy, não markup).
> Referências: `tech-writer-sales.md` §4, `docs/12-sales-cut.md` §3.3, `docs/01-product-vision.md`.
> Princípio: voz curta, "arquitetura como evidência" — não opinião. Zero hype.

---

## Hero

### H1
**Arquitetura como evidência. Auditoria automatizada para entregas de software .NET.**

### Subtítulo
Lintty analisa solutions .NET com Roslyn type-aware e emite um laudo PDF assinado digitalmente que vale como evidência de aceite — sem mediação humana, sem opinião subjetiva.

### CTA primário (botão único)
**Solicite uma demo** → `[link form Tally/Formspree]` (fallback: `mailto:contato@lintty.com`)

> Visual sugerido: [INSERIR HERO LIMPO. FUNDO BRANCO OU CINZA-ESCURO. À ESQUERDA H1 + SUBTÍTULO + CTA; À DIREITA UM PRINT REAL DA CAPA DO LAUDO PDF MOCK COM 'F — SELO NÃO EMITIDO' VISÍVEL. SEM STOCK PHOTO. SEM 3D ABSTRATO. SEM 'AI BRAIN'.]

---

## Como funciona

> Subsection title: **Três passos. Sem dashboard intermediário.**

### Passo 1 — Code
A agência entrega a solution .NET via repositório GitHub. Lintty roda no Milestone solicitado pelo contratante.

> Ícone sugerido: chevron de código `</>` ou ícone de pasta de projeto. Sem ilustração decorativa.

### Passo 2 — Análise Roslyn type-aware
Motor type-aware analisa camadas, dependências, isolamento de domínio, ciclos de projeto. **100% determinístico:** mesmo código → mesmo veredito, sempre. Zero alucinação por design — nenhuma camada de LLM no pipeline V0.

> Ícone sugerido: ícone de filtro/funil ou de microscópio. Sem "cérebro" ou "IA antropomorfizada" — o produto V0 é deliberadamente sem IA.

### Passo 3 — Laudo PDF determinístico
PDF gerado pelo próprio motor via QuestPDF — fontes embedded, determinismo bit-a-bit. Contém score, lista de violações com snippet, sumário de exceções, grafo de dependências e hash sha256 do binário no rodapé. Em V1 acrescentamos assinatura PAdES-B-LT + TSA RFC 3161 + audit hash-chain — peso jurídico nacional.

> Ícone sugerido: ícone de documento com checksum. No Sales Cut o destaque é "determinístico", não "assinado" (assinatura é roadmap).

---

## Saint vs Sinner

> Subsection title: **O motor não inventa problemas. E não passa por cima dos reais.**

### Card 1 — The Saint

**Score: A (100/100)**

```csharp
// Domain/Orders/Order.cs
namespace Saint.Domain.Orders
{
    public class Order
    {
        public OrderId Id { get; }
        public Money Total => _items.Sum(i => i.Subtotal);
    }
}
```

**0 violações.** Domínio puro, sem dependência de infra, agregado coeso. Lintty não cria ruído onde não há problema.

> Visual sugerido: card com fundo verde-claro discreto, score "A" em destaque tipográfico no canto superior direito. Snippet em fonte mono. SEM checkmark verde gigante.

### Card 2 — The Sinner

**Score: F (selo não emitido)**

```csharp
// Domain/Customers/Customer.cs
public bool IsEligibleForCredit()
{
    var query = "SELECT credit_limit FROM customers WHERE id = " + this.Id;
    return ExecuteRaw(query) > 0;
}
```

**9 violações, 3 hard locks.** SQL inline na camada de domínio (LNTY-002), ciclo de dependência Application↔Infrastructure (LNTY-007). Selo não é emitido enquanto hard lock não for resolvido.

> Visual sugerido: card com fundo vermelho-escuro discreto, score "F" em destaque tipográfico. Snippet em mono com linha violadora marcada. SEM ícone de "X" genérico.

---

## CTA repetido (fim da página)

### Frase de transição
Lintty é uma ferramenta SaaS de validação arquitetural automatizada. Não somos escrow financeiro, não somos árbitro jurídico, não há revisão humana. Somos a evidência técnica que o contrato pede.

### CTA secundário
**Solicite uma demo** → `[link form Tally/Formspree]` (fallback: `mailto:contato@lintty.com`)

---

## Footer

### Links
- Privacidade → `/privacidade`
- Contato → `mailto:contato@lintty.com`

### Copyright
© 2026 Lintty. Todos os direitos reservados.

> **Não incluir:** newsletter signup, formulário de captura de email, "powered by", logos de cliente, depoimentos não autorizados, badges de "as seen in", chat widget, cookie banner desnecessário (apenas o mínimo legal de LGPD se houver tracking).

---

## Anti-patterns que o frontend-dev NÃO deve adicionar por conta própria

- "Powered by AI" como bullet ou badge.
- Animação de "código sendo digitado" no hero.
- Stock photo de equipe em reunião.
- Comparação direta com SonarQube/CodeClimate.
- "Free trial" ou "Money-back guarantee".
- Logos de clientes que ainda não fecharam contrato.
- "Revolutionary" / "next-gen" / "game-changing" em qualquer lugar.
- Pop-up de "Vamos juntos transformar..."

---

## Notas sobre tom (para revisão futura)

- Português-Brasil formal mas direto. Evitar gerúndio no marketing-speak ("estaremos analisando..."). Preferir presente do indicativo: "analisa", "emite", "assina".
- Verbos concretos em vez de verbos vazios: "emite" > "fornece"; "detecta" > "ajuda a identificar".
- Subtítulos em sentence case, não Title Case.
- Números reais quando possível: "5% de cobertura em code review manual" > "code reviews limitados".
- "Não-promessas" são parte do posicionamento e devem aparecer pelo menos uma vez na página.
