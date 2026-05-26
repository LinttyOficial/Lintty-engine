import type { Metadata } from "next";
import Link from "next/link";
import { AuroraPageHeader } from "@/components/HeroAurora";

export const metadata: Metadata = {
  title: "Planos — Lintty",
  description:
    "Planos Lintty: Free para times pequenos, Team para squads, Enterprise para varejistas grandes. Preços em validação.",
  alternates: { canonical: "https://lintty.com/pricing" },
  openGraph: {
    title: "Planos — Lintty",
    description: "Free, Team, Enterprise. Preços em validação.",
    url: "https://lintty.com/pricing",
    type: "website",
    images: [{ url: "/assets/og-image.svg" }],
  },
  twitter: { card: "summary_large_image" },
};

/**
 * Pricing tiers — hardcoded constants. The /api/pricing endpoint was
 * cancelled; the legacy page already shipped a fallback table inline,
 * so this just promotes that table to first-class data. Values must
 * stay in sync with `docs/adr/0007-dashboard-multi-tenant.md`.
 */
interface Tier {
  name: "Free" | "Team" | "Enterprise";
  scansPerMonth: number; // -1 = ilimitado
  users: number; // -1 = ilimitado
  monthlyPriceBrl: number; // 0 free, -1 sob consulta
  displayPrice: string;
  featured: boolean;
}

const TIERS: Tier[] = [
  {
    name: "Free",
    scansPerMonth: 10,
    users: 3,
    monthlyPriceBrl: 0,
    displayPrice: "R$ 0",
    featured: false,
  },
  {
    name: "Team",
    scansPerMonth: 200,
    users: 15,
    monthlyPriceBrl: 1490,
    displayPrice: "R$ 1.490",
    featured: true,
  },
  {
    name: "Enterprise",
    scansPerMonth: -1,
    users: -1,
    monthlyPriceBrl: -1,
    displayPrice: "Sob consulta",
    featured: false,
  },
];

function tierCopy(tier: Tier): {
  bullets: string[];
  cta: { label: string; href: string; primary: boolean };
} {
  const bullets: string[] = [];
  if (tier.scansPerMonth === -1) {
    bullets.push("Scans ilimitados / mês");
  } else {
    bullets.push(`${tier.scansPerMonth} scans / mês`);
  }
  if (tier.users === -1) {
    bullets.push("Usuários ilimitados");
  } else if (tier.users === 1) {
    bullets.push("1 usuário");
  } else {
    bullets.push(`Até ${tier.users} usuários`);
  }

  if (tier.name === "Free") {
    bullets.push("Web Inspector e CLI sem limites adicionais");
    bullets.push("Histórico de laudos por 30 dias");
  } else if (tier.name === "Team") {
    bullets.push("Histórico de laudos por 12 meses");
    bullets.push("GitHub OAuth + scans em repos privados");
    bullets.push("Convite de membros, roles básicas");
  } else {
    bullets.push("Histórico ilimitado + retenção sob contrato");
    bullets.push("SSO (SAML/OIDC), DPA assinado, NF-e");
    bullets.push("SLA combinado, prioridade de suporte");
    bullets.push("Roadmap V1+: PAdES + TSA, hash-chain auditável");
  }

  if (tier.name === "Free") {
    return {
      bullets,
      cta: { label: "Criar conta grátis", href: "/signup", primary: true },
    };
  }

  const subject = `Lintty - Plano ${tier.name}`;
  return {
    bullets,
    cta: {
      label: "Falar com vendas",
      href: `mailto:vinicius@landtech.com.br?subject=${encodeURIComponent(subject)}`,
      primary: tier.name === "Team",
    },
  };
}

function priceSubLabel(tier: Tier): string {
  if (tier.monthlyPriceBrl === 0) return "para sempre";
  if (tier.monthlyPriceBrl === -1) return "contrato anual";
  return "/ mês · cobrança em BRL";
}

function tierLabel(tier: Tier): string {
  if (tier.featured) return "Mais escolhido";
  return tier.name === "Free" ? "Para começar" : "Para escala";
}

export default function PricingPage() {
  return (
    <>
      <AuroraPageHeader
        align="center"
        eyebrow="Planos Lintty · Beta"
        title="Mesmo motor. Três tamanhos."
        subtitle={
          <>
            Roslyn type-aware, determinismo bit-a-bit, código nunca sai do seu equipamento
            (CLI) ou descartado em até 60 segundos (Web Inspector). O preço cobre o time, não
            o motor.
          </>
        }
        extra={
          <span className="inline-flex items-center gap-2 bg-emerald-500/10 border border-emerald-400/30 text-emerald-200 text-sm rounded-md px-4 py-2">
            <svg
              width="16"
              height="16"
              viewBox="0 0 20 20"
              fill="none"
              aria-hidden="true"
            >
              <path
                d="M10 2a8 8 0 100 16 8 8 0 000-16zM10 7v4M10 14h.01"
                stroke="currentColor"
                strokeWidth="1.6"
                strokeLinecap="round"
              />
            </svg>
            <strong>Preços em validação.</strong> Entre em contato para fechar.
          </span>
        }
      />
      <main id="main" className="lt-dark-glow lt-noise px-6 py-12 md:py-20 text-paper">
      {/* Cards */}
      <div className="mt-14 max-w-6xl mx-auto grid md:grid-cols-3 gap-6">
        {TIERS.map((tier) => {
          const copy = tierCopy(tier);
          const isFeatured = tier.featured;
          const ctaClass = copy.cta.primary
            ? "mt-6 inline-flex items-center justify-center gap-2 px-6 py-3 rounded-md bg-saint text-white font-semibold hover:bg-emerald-600 transition shadow-lg shadow-emerald-900/40"
            : "mt-6 lt-btn-secondary";

          // Internal link uses Next <Link>, mailto: stays as plain anchor.
          const Cta = copy.cta.href.startsWith("/") ? (
            <Link href={copy.cta.href} className={ctaClass}>
              {copy.cta.label}
              <span aria-hidden="true">→</span>
            </Link>
          ) : (
            <a href={copy.cta.href} className={ctaClass}>
              {copy.cta.label}
              <span aria-hidden="true">→</span>
            </a>
          );

          return (
            <article
              key={tier.name}
              className={`tier-card ${
                isFeatured ? "is-featured" : "lt-card-soft bg-neutral-900/40"
              } rounded-xl p-7`}
            >
              <p
                className={`text-xs font-semibold tracking-widest uppercase mb-3 ${
                  isFeatured ? "text-emerald-300" : "text-neutral-400"
                }`}
              >
                {tierLabel(tier)}
              </p>
              <h2 className="text-2xl font-bold tracking-tight text-paper">{tier.name}</h2>
              <div className="mt-4">
                <span className="text-4xl font-bold tracking-tight text-paper">
                  {tier.displayPrice}
                </span>
                <p className="mt-1 text-xs text-neutral-500">
                  {priceSubLabel(tier)}
                </p>
              </div>
              <ul className="mt-6 space-y-3 flex-1">
                {copy.bullets.map((b) => (
                  <li
                    key={b}
                    className="flex gap-3 text-sm text-neutral-300"
                  >
                    <span className="text-emerald-300 mt-0.5" aria-hidden="true">
                      ✓
                    </span>
                    <span>{b}</span>
                  </li>
                ))}
              </ul>
              {Cta}
            </article>
          );
        })}
      </div>

      {/* FAQ */}
      <section className="mt-20 max-w-3xl mx-auto">
        <h2 className="text-2xl md:text-3xl font-bold tracking-tight text-paper">
          Perguntas frequentes
        </h2>
        <div className="mt-8 space-y-4">
          <details className="lt-card-soft bg-neutral-900/40 rounded-xl p-5 group">
            <summary className="cursor-pointer font-semibold text-paper list-none flex items-center justify-between">
              <span>O CLI é grátis mesmo sem conta?</span>
              <span
                aria-hidden="true"
                className="text-neutral-400 group-open:rotate-180 transition"
              >
                ↓
              </span>
            </summary>
            <p className="mt-3 text-sm text-neutral-300">
              Sim. O <Link href="/cli" className="underline decoration-emerald-400/50 underline-offset-2 text-emerald-200 hover:text-emerald-100">CLI Lintty</Link> é distribuído
              via GitHub Releases e roda 100% no seu equipamento, com o mesmo motor que
              alimenta o dashboard. Os planos pagos cobrem o uso do dashboard multi-tenant
              (histórico compartilhado, gestão de membros, scans org-bound) — não o motor em
              si.
            </p>
          </details>
          <details className="lt-card-soft bg-neutral-900/40 rounded-xl p-5 group">
            <summary className="cursor-pointer font-semibold text-paper list-none flex items-center justify-between">
              <span>Os preços são definitivos?</span>
              <span
                aria-hidden="true"
                className="text-neutral-400 group-open:rotate-180 transition"
              >
                ↓
              </span>
            </summary>
            <p className="mt-3 text-sm text-neutral-300">
              Não. Os valores em destaque são uma referência inicial e estão sendo validados
              com os primeiros clientes do varejo enterprise brasileiro. Se algum tier faz
              sentido para você, fale com a gente — fechamos contrato direto e respeitamos a
              referência.
            </p>
          </details>
          <details className="lt-card-soft bg-neutral-900/40 rounded-xl p-5 group">
            <summary className="cursor-pointer font-semibold text-paper list-none flex items-center justify-between">
              <span>O que conta como um &ldquo;scan/mês&rdquo;?</span>
              <span
                aria-hidden="true"
                className="text-neutral-400 group-open:rotate-180 transition"
              >
                ↓
              </span>
            </summary>
            <p className="mt-3 text-sm text-neutral-300">
              Cada execução do motor que gera um laudo PDF — seja via Web Inspector ou via
              API — conta como um scan. Re-runs do mesmo commit dentro de 5 minutos não
              contam (cache de idempotência). Uso pelo CLI local não conta para a quota do
              plano.
            </p>
          </details>
          <details className="lt-card-soft bg-neutral-900/40 rounded-xl p-5 group">
            <summary className="cursor-pointer font-semibold text-paper list-none flex items-center justify-between">
              <span>Tem acordo anual / desconto?</span>
              <span
                aria-hidden="true"
                className="text-neutral-400 group-open:rotate-180 transition"
              >
                ↓
              </span>
            </summary>
            <p className="mt-3 text-sm text-neutral-300">
              Para Enterprise, sim — fechamos contrato anual com NF-e, DPA assinado e SLA
              combinado no escopo. Mande email para{" "}
              <a
                href="mailto:vinicius@landtech.com.br"
                className="underline decoration-emerald-400/50 underline-offset-2 text-emerald-200 hover:text-emerald-100"
              >
                vinicius@landtech.com.br
              </a>{" "}
              que respondemos em até 1 dia útil.
            </p>
          </details>
        </div>
      </section>

      {/* CTA final */}
      <section className="mt-20 max-w-3xl mx-auto text-center">
        <h2 className="text-2xl md:text-3xl font-bold tracking-tight text-paper">
          Quer testar o motor antes?
        </h2>
        <p className="mt-4 text-neutral-300">
          Sem signup. Sem cartão. Cole a URL de um repo público no Web Inspector ou baixe o
          CLI.
        </p>
        <div className="mt-6 flex flex-wrap justify-center gap-3">
          <Link
            href="/inspect"
            className="inline-flex items-center gap-2 px-6 py-3 rounded-md bg-saint text-white font-semibold hover:bg-emerald-600 transition shadow-lg shadow-emerald-900/40"
          >
            Web Inspector →
          </Link>
          <Link
            href="/cli"
            className="lt-btn-secondary px-6 py-3"
          >
            Baixar CLI
          </Link>
        </div>
      </section>
      </main>
    </>
  );
}
