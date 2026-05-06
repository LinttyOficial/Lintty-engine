import type { Metadata } from "next";
import Link from "next/link";

export const metadata: Metadata = {
  title: "Lintty — Arquitetura como evidência",
  description:
    "Lintty analisa solutions .NET com Roslyn type-aware e emite um laudo PDF assinado digitalmente que vale como evidência de aceite — sem mediação humana, sem opinião subjetiva.",
  alternates: { canonical: "https://lintty.com/" },
  openGraph: {
    title: "Lintty — Arquitetura como evidência",
    description:
      "Auditoria automatizada para entregas de software .NET. Laudo PDF assinado como evidência de aceite.",
    url: "https://lintty.com/",
    type: "website",
    images: [{ url: "/assets/og-image.svg" }],
  },
  twitter: { card: "summary_large_image" },
};

export default function HomePage() {
  return (
    <main id="main">
      {/* ===== Hero ===== */}
      <section className="px-6 py-20 md:py-28">
        <div className="max-w-6xl mx-auto grid md:grid-cols-12 gap-12 items-center">
          <div className="md:col-span-7">
            <p className="text-xs font-semibold tracking-widest text-neutral-500 uppercase mb-5">
              Auditoria automatizada para entregas .NET
            </p>
            <h1 className="text-4xl md:text-5xl lg:text-6xl font-bold leading-[1.05] tracking-tight">
              Arquitetura como evidencia.
              <br />
              <span className="text-neutral-700">
                Auditoria automatizada para entregas de software .NET.
              </span>
            </h1>
            <p className="mt-6 text-lg text-neutral-700 max-w-2xl">
              Lintty analisa solutions .NET com Roslyn type-aware e emite um laudo PDF assinado
              digitalmente que vale como evidencia de aceite &mdash; sem mediacao humana,
              sem opiniao subjetiva.
            </p>
            <div className="mt-8 flex flex-wrap gap-3">
              <a
                href="mailto:contato@lintty.com?subject=Demo%20Lintty&body=Empresa:%0AContato:%0AStack:%0A"
                className="inline-flex items-center gap-2 px-6 py-3 rounded-md bg-ink text-white font-semibold hover:bg-neutral-800 transition"
              >
                Solicite uma demo
                <span aria-hidden="true">&rarr;</span>
              </a>
              <a
                href="#como-funciona"
                className="inline-flex items-center gap-2 px-6 py-3 rounded-md border border-neutral-300 text-neutral-800 font-medium hover:bg-neutral-100 transition"
              >
                Como funciona
              </a>
            </div>
            <p className="mt-6 text-sm text-neutral-500">
              Sem free trial. Sem signup self-service. Conversa comercial direta.
            </p>
          </div>

          {/* Mock visual da capa do laudo */}
          <div className="md:col-span-5">
            <div className="relative">
              <div
                className="absolute -inset-4 bg-gradient-to-br from-neutral-200 to-neutral-100 rounded-2xl rotate-1"
                aria-hidden="true"
              ></div>
              <article
                className="relative bg-white border border-neutral-300 rounded-xl shadow-sm p-7"
                aria-label="Mock da capa do laudo PDF"
              >
                <div className="flex items-center justify-between text-xs text-neutral-500 mb-6">
                  <span className="font-mono">LAUDO #2026-04-27-0001</span>
                  <span>Lintty Canon v1.0.0</span>
                </div>
                <p className="text-xs uppercase tracking-widest text-neutral-500">Score</p>
                <div className="flex items-baseline gap-3 mt-1">
                  <span className="text-7xl font-bold text-sinner leading-none">F</span>
                  <span className="text-sm text-neutral-600">
                    9 violacoes &middot; 3 hard locks
                  </span>
                </div>
                <div className="mt-5 inline-flex items-center gap-2 px-3 py-1.5 rounded-md bg-sinner-bg text-sinner text-sm font-semibold">
                  Selo NAO emitido
                </div>
                <hr className="my-6 border-neutral-200" />
                <ul className="text-sm text-neutral-700 space-y-2">
                  <li className="flex gap-2">
                    <span className="font-mono text-xs text-neutral-500 mt-0.5">LNTY-002</span>
                    SQL na camada de dominio
                  </li>
                  <li className="flex gap-2">
                    <span className="font-mono text-xs text-neutral-500 mt-0.5">LNTY-007</span>
                    Ciclo Application &harr; Infrastructure
                  </li>
                  <li className="flex gap-2">
                    <span className="font-mono text-xs text-neutral-500 mt-0.5">LNTY-003</span>
                    Mutavel exposto em agregado
                  </li>
                </ul>
                <p className="mt-6 text-xs text-neutral-500">
                  PDF deterministico &middot; sha256 no rodape &middot; PAdES-B-LT em V1
                </p>
              </article>
            </div>
          </div>
        </div>
      </section>

      {/* ===== Como funciona ===== */}
      <section
        id="como-funciona"
        className="px-6 py-20 bg-white border-y border-neutral-200"
      >
        <div className="max-w-6xl mx-auto">
          <header className="max-w-3xl">
            <p className="text-xs font-semibold tracking-widest text-neutral-500 uppercase mb-3">
              Como funciona
            </p>
            <h2 className="text-3xl md:text-4xl font-bold tracking-tight">
              Tres passos. Sem dashboard intermediario.
            </h2>
          </header>

          <div className="mt-12 grid md:grid-cols-3 gap-6">
            <article className="border border-neutral-200 rounded-xl p-7 bg-paper">
              <div className="flex items-center gap-3 mb-4">
                <span className="w-9 h-9 rounded-md bg-ink text-white flex items-center justify-center font-mono text-sm">
                  01
                </span>
                <span aria-hidden="true" className="font-mono text-neutral-400">
                  &lt;/&gt;
                </span>
              </div>
              <h3 className="font-semibold text-lg">Code</h3>
              <p className="mt-2 text-neutral-700 text-sm leading-relaxed">
                A agencia entrega a solution .NET via repositorio GitHub. Lintty roda no
                Milestone solicitado pelo contratante.
              </p>
            </article>

            <article className="border border-neutral-200 rounded-xl p-7 bg-paper">
              <div className="flex items-center gap-3 mb-4">
                <span className="w-9 h-9 rounded-md bg-ink text-white flex items-center justify-center font-mono text-sm">
                  02
                </span>
                <span aria-hidden="true" className="font-mono text-neutral-400">
                  &#x25BD;
                </span>
              </div>
              <h3 className="font-semibold text-lg">Analise Roslyn type-aware</h3>
              <p className="mt-2 text-neutral-700 text-sm leading-relaxed">
                Motor type-aware analisa camadas, dependencias, isolamento de dominio, ciclos
                de projeto. <strong>100% deterministico:</strong> mesmo codigo &rarr; mesmo
                veredito, sempre. Zero alucinacao por design &mdash; nenhuma camada de LLM no
                pipeline V0.
              </p>
            </article>

            <article className="border border-neutral-200 rounded-xl p-7 bg-paper">
              <div className="flex items-center gap-3 mb-4">
                <span className="w-9 h-9 rounded-md bg-ink text-white flex items-center justify-center font-mono text-sm">
                  03
                </span>
                <span aria-hidden="true" className="font-mono text-neutral-400">
                  &#x1F4C4;
                </span>
              </div>
              <h3 className="font-semibold text-lg">Laudo PDF deterministico</h3>
              <p className="mt-2 text-neutral-700 text-sm leading-relaxed">
                PDF gerado pelo proprio motor via QuestPDF &mdash; fontes embedded,
                determinismo bit-a-bit. Contem score, violacoes com snippet, sumario de
                excecoes, grafo de dependencias e{" "}
                <code className="font-mono text-xs">hash_pdf</code> sha256 no rodape.
                Assinatura PAdES-B-LT + TSA RFC 3161 entram no roadmap V1.
              </p>
            </article>
          </div>
        </div>
      </section>

      {/* ===== Saint vs Sinner ===== */}
      <section id="saint-vs-sinner" className="px-6 py-20">
        <div className="max-w-6xl mx-auto">
          <header className="max-w-3xl">
            <p className="text-xs font-semibold tracking-widest text-neutral-500 uppercase mb-3">
              Saint vs Sinner
            </p>
            <h2 className="text-3xl md:text-4xl font-bold tracking-tight">
              O motor nao inventa problemas. E nao passa por cima dos reais.
            </h2>
            <p className="mt-4 text-neutral-700">
              Dois fixtures publicos. Mesmo canon, decisoes opostas.
            </p>
          </header>

          <div className="mt-12 grid md:grid-cols-2 gap-6">
            <article className="rounded-xl border border-neutral-200 bg-saint-bg p-7">
              <div className="flex items-center justify-between mb-4">
                <span className="badge badge-saint">A &middot; 100/100</span>
                <span className="text-xs text-neutral-500 font-mono">the-saint</span>
              </div>
              <h3 className="font-semibold text-lg">Dominio puro, agregado coeso</h3>
              <pre
                className="snippet mt-4"
                aria-label="Snippet C# do fixture the-saint"
              >
                <code>{`// Saint.Domain/Order.cs
public sealed class Order
{
    private readonly List<OrderLine> _lines = new();
    public IReadOnlyList<OrderLine> Lines =>
        new ReadOnlyCollection<OrderLine>(_lines);
}`}</code>
              </pre>
              <p className="mt-4 text-sm text-neutral-700">
                <strong>0 violacoes.</strong> Encapsulamento correto, sem leak mutavel,
                sem dependencia de infra. Lintty nao cria ruido onde nao ha problema.
              </p>
            </article>

            <article className="rounded-xl border border-neutral-200 bg-sinner-bg p-7">
              <div className="flex items-center justify-between mb-4">
                <span className="badge badge-sinner">F &middot; Selo NAO emitido</span>
                <span className="text-xs text-neutral-500 font-mono">the-sinner</span>
              </div>
              <h3 className="font-semibold text-lg">SQL na camada de dominio</h3>
              <pre
                className="snippet mt-4"
                aria-label="Snippet C# do fixture the-sinner"
              >
                <code>{`// Sinner.Domain/Customer.cs
public string BuildLookupSql(int customerId)
{
    var sql = "SELECT * FROM Customers WHERE Id = " + customerId;
    return sql;
}`}</code>
              </pre>
              <p className="mt-4 text-sm text-neutral-700">
                <strong>9 violacoes, 3 hard locks.</strong> SQL inline na camada de dominio
                <span className="font-mono text-xs"> (LNTY-002)</span>. Selo nao e emitido
                enquanto hard lock nao for resolvido.
              </p>
            </article>
          </div>
        </div>
      </section>

      {/* ===== Defensibilidade ===== */}
      <section id="defensibilidade" className="px-6 py-20 bg-ink text-white">
        <div className="max-w-6xl mx-auto">
          <header className="max-w-3xl">
            <p className="text-xs font-semibold tracking-widest text-neutral-400 uppercase mb-3">
              Defensibilidade
            </p>
            <h2 className="text-3xl md:text-4xl font-bold tracking-tight">
              Por que Lintty nao e replicavel em um fim de semana.
            </h2>
          </header>

          <ul className="mt-12 grid md:grid-cols-2 lg:grid-cols-5 gap-6">
            <li className="rounded-xl border border-neutral-700 p-6">
              <p className="text-xs font-mono text-neutral-400 mb-2">01</p>
              <h3 className="font-semibold">Canon opinionado</h3>
              <p className="mt-2 text-sm text-neutral-300">
                Agencias aprendem a entregar &ldquo;no padrao Lintty&rdquo;. Network effect
                via padrao.
              </p>
            </li>
            <li className="rounded-xl border border-neutral-700 p-6">
              <p className="text-xs font-mono text-neutral-400 mb-2">02</p>
              <h3 className="font-semibold">Determinismo bit-a-bit</h3>
              <p className="mt-2 text-sm text-neutral-300">
                Mesmo input &rarr; mesmo PDF, sempre. Zero alucinacao, zero custo de
                inferencia. Type-aware Roslyn, sem IA no V0.
              </p>
            </li>
            <li className="rounded-xl border border-neutral-700 p-6">
              <p className="text-xs font-mono text-neutral-400 mb-2">03</p>
              <h3 className="font-semibold">Analise efemera</h3>
              <p className="mt-2 text-sm text-neutral-300">
                Codigo processado localmente, nao persiste apos a analise. Sem third-party
                na cadeia V0.
              </p>
            </li>
            <li className="rounded-xl border border-neutral-700 p-6">
              <p className="text-xs font-mono text-neutral-400 mb-2">04</p>
              <h3 className="font-semibold">Constant folding</h3>
              <p className="mt-2 text-sm text-neutral-300">
                Detecta SQL escondido por concat de constantes. Linter regex passa direto,
                Lintty pega.
              </p>
            </li>
            <li className="rounded-xl border border-neutral-700 p-6">
              <p className="text-xs font-mono text-neutral-400 mb-2">05</p>
              <h3 className="font-semibold">Roadmap V1</h3>
              <p className="mt-2 text-sm text-neutral-300">
                PDF assinado (PAdES-B-LT + TSA), audit hash-chain imutavel, e LLM advogado de
                defesa opcional sob ZDR Anthropic. Complemento, nao dependencia.
              </p>
            </li>
          </ul>
        </div>
      </section>

      {/* ===== CTA final ===== */}
      <section id="contato" className="px-6 py-24">
        <div className="max-w-3xl mx-auto text-center">
          <p className="text-xs font-semibold tracking-widest text-neutral-500 uppercase mb-3">
            Sales Cut Tier 1
          </p>
          <h2 className="text-3xl md:text-4xl font-bold tracking-tight">
            Lintty e a evidencia tecnica que o contrato pede.
          </h2>
          <p className="mt-5 text-neutral-700">
            Nao somos escrow financeiro, nao somos arbitro juridico, nao ha revisao humana.
            Somos o laudo que vai junto do aceite.
          </p>
          <div className="mt-8 flex flex-wrap justify-center gap-3">
            <a
              href="mailto:contato@lintty.com?subject=Demo%20Lintty&body=Empresa:%0AContato:%0AStack:%0A"
              className="inline-flex items-center gap-2 px-6 py-3 rounded-md bg-ink text-white font-semibold hover:bg-neutral-800 transition"
            >
              Solicite uma demo
              <span aria-hidden="true">&rarr;</span>
            </a>
            <a
              href="mailto:contato@lintty.com"
              className="inline-flex items-center gap-2 px-6 py-3 rounded-md border border-neutral-300 text-neutral-800 font-medium hover:bg-neutral-100 transition"
            >
              contato@lintty.com
            </a>
            <Link
              href="/inspect"
              className="inline-flex items-center gap-2 px-6 py-3 rounded-md border border-neutral-300 text-neutral-800 font-medium hover:bg-neutral-100 transition"
            >
              Inspecionar repo
            </Link>
          </div>
        </div>
      </section>
    </main>
  );
}
