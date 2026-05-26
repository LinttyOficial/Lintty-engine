import type { Metadata } from "next";
import Link from "next/link";
import { HeroAurora } from "@/components/HeroAurora";
import { FadeIn } from "@/components/FadeIn";
import { DeterminismShowcase } from "@/components/DeterminismShowcase";

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
      {/* ===== Hero (Aurora) ===== */}
      <HeroAurora />

      {/* ===== Como funciona ===== */}
      <section
        id="como-funciona"
        className="lt-dark-glow lt-noise px-6 py-24 md:py-28 text-paper"
      >
        <div className="max-w-6xl mx-auto">
          <FadeIn as="header" className="flex flex-col md:flex-row md:items-end md:justify-between gap-6">
            <div className="max-w-2xl">
              <p className="text-[11px] font-mono font-semibold tracking-[0.18em] text-emerald-300/80 uppercase mb-3">
                Como funciona &middot; três passos
              </p>
              <h2 className="text-3xl md:text-5xl font-bold tracking-tight text-paper leading-[1.05]">
                Sem dashboard intermediário.
                <br />
                <span className="text-neutral-500">Sem espera. Sem chat.</span>
              </h2>
            </div>
            <p className="hidden md:block font-mono text-[11px] text-neutral-500 tracking-wide max-w-[22ch] text-right">
              03 etapas · 0 modelos de linguagem · 0 humanos no loop
            </p>
          </FadeIn>

          {/* Stepper — três colunas com numeração editorial, conectadas por
              uma linha tracejada sutil no desktop. Cada step termina num
              "artefato real" mono para materializar o pitch. */}
          <div className="relative mt-16 md:mt-20">
            <div
              aria-hidden="true"
              className="hidden md:block absolute top-5 left-[12%] right-[12%] h-px"
              style={{
                backgroundImage:
                  "repeating-linear-gradient(to right, rgba(82,82,82,0.55) 0 4px, transparent 4px 10px)",
              }}
            />

            <div className="grid md:grid-cols-3 gap-x-10 gap-y-14 relative">
              {/* Step 01 — Code */}
              <FadeIn as="article" delayMs={0} className="relative">
                <div className="flex items-center gap-3 relative bg-ink z-10 pr-4 -ml-1">
                  <span className="font-mono text-[28px] font-bold text-emerald-300 tabular-nums tracking-tight leading-none">
                    01
                  </span>
                  <span className="text-[10px] font-mono uppercase tracking-[0.18em] text-neutral-500">
                    Entrada
                  </span>
                </div>
                <h3 className="mt-7 font-semibold text-xl text-paper tracking-tight">
                  Code
                </h3>
                <p className="mt-2 text-neutral-400 text-sm leading-relaxed">
                  A agência entrega a solution .NET via repositório GitHub.
                  Lintty roda no milestone solicitado pelo contratante.
                </p>
                <div className="mt-5 rounded-lg bg-neutral-950/70 border border-neutral-800/60 px-4 py-3 font-mono text-[12px] leading-[1.7]">
                  <span className="text-neutral-500">$</span>{" "}
                  <span className="text-neutral-200">lintty-engine analyze \</span>
                  <br />
                  <span className="text-neutral-600">  </span>
                  <span className="text-neutral-400">--solution</span>{" "}
                  <span className="text-emerald-300">App.sln</span>{" "}
                  <span className="text-neutral-200">\</span>
                  <br />
                  <span className="text-neutral-600">  </span>
                  <span className="text-neutral-400">--pdf</span>{" "}
                  <span className="text-emerald-300">laudo.pdf</span>
                </div>
              </FadeIn>

              {/* Step 02 — Analyze */}
              <FadeIn as="article" delayMs={140} className="relative">
                <div className="flex items-center gap-3 relative bg-ink z-10 pr-4">
                  <span className="font-mono text-[28px] font-bold text-emerald-300 tabular-nums tracking-tight leading-none">
                    02
                  </span>
                  <span className="text-[10px] font-mono uppercase tracking-[0.18em] text-neutral-500">
                    Motor
                  </span>
                </div>
                <h3 className="mt-7 font-semibold text-xl text-paper tracking-tight">
                  Roslyn type-aware
                </h3>
                <p className="mt-2 text-neutral-400 text-sm leading-relaxed">
                  Motor analisa camadas, dependências, isolamento de domínio,
                  ciclos. <strong className="text-paper">100% determinístico</strong> &mdash;
                  mesmo código, mesmo veredito. Zero LLM no pipeline V0.
                </p>
                <div className="mt-5 rounded-lg bg-neutral-950/70 border border-neutral-800/60 px-4 py-3 font-mono text-[12px] leading-[1.7] space-y-0.5">
                  <div className="flex items-baseline justify-between">
                    <span className="text-neutral-500">regras canon</span>
                    <span className="text-paper">07</span>
                  </div>
                  <div className="flex items-baseline justify-between">
                    <span className="text-neutral-500">hard locks</span>
                    <span className="text-paper">03</span>
                  </div>
                  <div className="flex items-baseline justify-between">
                    <span className="text-neutral-500">roslyn type-aware</span>
                    <span className="text-emerald-300">100%</span>
                  </div>
                  <div className="flex items-baseline justify-between">
                    <span className="text-neutral-500">modelos llm</span>
                    <span className="text-neutral-300">00</span>
                  </div>
                </div>
              </FadeIn>

              {/* Step 03 — Audit */}
              <FadeIn as="article" delayMs={280} className="relative">
                <div className="flex items-center gap-3 relative bg-ink z-10 pr-4">
                  <span className="font-mono text-[28px] font-bold text-emerald-300 tabular-nums tracking-tight leading-none">
                    03
                  </span>
                  <span className="text-[10px] font-mono uppercase tracking-[0.18em] text-neutral-500">
                    Evidência
                  </span>
                </div>
                <h3 className="mt-7 font-semibold text-xl text-paper tracking-tight">
                  Laudo PDF
                </h3>
                <p className="mt-2 text-neutral-400 text-sm leading-relaxed">
                  PDF gerado via QuestPDF &mdash; fontes embedded, determinismo
                  bit-a-bit. Score, violações com snippet,{" "}
                  <code className="font-mono text-xs text-paper">hash_content</code>{" "}
                  sha256 no rodapé. PAdES-B-LT + TSA no roadmap V1.
                </p>
                <div className="mt-5 rounded-lg bg-neutral-950/70 border border-neutral-800/60 px-4 py-3 font-mono text-[11px] leading-[1.6]">
                  <div className="text-[9px] uppercase tracking-[0.18em] text-neutral-500 mb-1">
                    hash_content
                  </div>
                  <div className="text-emerald-300/90 break-all">
                    9a4f3e21b9c0d72f8e58473b6a118c10
                    <span className="text-emerald-400/40">d4bb86f3e0a1d2c4509a8e6dc20f0b21d</span>
                  </div>
                  <div className="mt-2 text-[10px] text-neutral-500">
                    deterministic · idêntico em qualquer máquina
                  </div>
                </div>
              </FadeIn>
            </div>
          </div>
        </div>
      </section>

      {/* ===== Saint vs Sinner ===== */}
      <section id="saint-vs-sinner" className="lt-dark-glow lt-noise px-6 py-24 md:py-28 text-paper">
        <div className="max-w-6xl mx-auto">
          <FadeIn as="header" className="flex flex-col md:flex-row md:items-end md:justify-between gap-6">
            <div className="max-w-2xl">
              <p className="text-[11px] font-mono font-semibold tracking-[0.18em] text-emerald-300/80 uppercase mb-3">
                Saint vs Sinner &middot; fixtures públicos
              </p>
              <h2 className="text-3xl md:text-5xl font-bold tracking-tight text-paper leading-[1.05]">
                O motor não inventa problemas.
                <br />
                <span className="text-neutral-500">E não passa por cima dos reais.</span>
              </h2>
            </div>
            <p className="font-mono text-[11px] text-neutral-500 tracking-wide md:text-right max-w-[26ch]">
              Dois fixtures do repo · mesmo canon · vereditos opostos
            </p>
          </FadeIn>

          {/* Comparativo lado-a-lado com selo "VS" central */}
          <div className="relative mt-14 md:mt-16">
            {/* VS pill central — só aparece no desktop sobre o gutter entre cards */}
            <div
              aria-hidden="true"
              className="hidden md:flex absolute left-1/2 top-1/2 -translate-x-1/2 -translate-y-1/2 z-10 items-center justify-center w-12 h-12 rounded-full bg-ink border border-neutral-800 font-mono text-[11px] font-semibold text-neutral-300 tracking-[0.18em] shadow-[0_8px_24px_-8px_rgba(0,0,0,0.7)]"
            >
              VS
            </div>

            <div className="grid md:grid-cols-2 gap-6 md:gap-8">
              {/* Saint — A, 100/100 */}
              <FadeIn as="article" className="lt-card-saint rounded-2xl p-6 md:p-8">
                <div className="flex items-start justify-between gap-4">
                  <div>
                    <p className="text-[10px] font-mono uppercase tracking-[0.18em] text-emerald-300/80 mb-1">
                      Fixture · the-saint
                    </p>
                    <p className="text-xs text-neutral-500 font-mono">
                      fixtures/the-saint/Saint.sln
                    </p>
                  </div>
                  <span className="badge badge-saint shrink-0">selo emitido</span>
                </div>

                {/* Verdict block */}
                <div className="mt-6 flex items-end gap-4">
                  <span className="text-7xl md:text-8xl font-bold text-emerald-300 leading-none tabular-nums">
                    A
                  </span>
                  <div className="pb-2">
                    <div className="font-mono">
                      <span className="text-3xl font-bold text-paper">100</span>
                      <span className="text-neutral-500 text-base">/100</span>
                    </div>
                    <p className="text-xs text-neutral-400 mt-0.5">0 violações</p>
                  </div>
                </div>

                <h3 className="mt-6 font-semibold text-lg text-paper tracking-tight">
                  Domínio puro, agregado coeso
                </h3>

                {/* Code panel */}
                <div className="mt-4 rounded-lg overflow-hidden border border-emerald-500/15 bg-neutral-950/70">
                  <div className="flex items-center justify-between px-3 py-2 border-b border-neutral-800/60 bg-black/40">
                    <span className="font-mono text-[10px] text-neutral-500">
                      Saint.Domain/Order.cs
                    </span>
                    <span className="font-mono text-[10px] text-emerald-300/80">
                      ✓ 0 violations
                    </span>
                  </div>
                  <pre
                    className="px-4 py-3 text-[12px] leading-[1.65] overflow-x-auto font-mono text-neutral-200"
                    aria-label="Snippet C# do fixture the-saint"
                  >
                    <code>{`public sealed class Order
{
    private readonly List<OrderLine> _lines = new();
    public IReadOnlyList<OrderLine> Lines =>
        new ReadOnlyCollection<OrderLine>(_lines);
}`}</code>
                  </pre>
                </div>

                <p className="mt-5 text-sm text-neutral-300 leading-relaxed">
                  <strong className="text-paper">Encapsulamento correto</strong>, sem
                  leak mutável, sem dependência de infra. Lintty não cria ruído
                  onde não há problema.
                </p>
              </FadeIn>

              {/* Sinner — F, hard lock */}
              <FadeIn as="article" delayMs={140} className="lt-card-sinner rounded-2xl p-6 md:p-8">
                <div className="flex items-start justify-between gap-4">
                  <div>
                    <p className="text-[10px] font-mono uppercase tracking-[0.18em] text-red-300/80 mb-1">
                      Fixture · the-sinner
                    </p>
                    <p className="text-xs text-neutral-500 font-mono">
                      fixtures/the-sinner/Sinner.sln
                    </p>
                  </div>
                  <span className="badge badge-sinner shrink-0">selo bloqueado</span>
                </div>

                {/* Verdict block */}
                <div className="mt-6 flex items-end gap-4">
                  <span className="text-7xl md:text-8xl font-bold text-red-300 leading-none tabular-nums">
                    F
                  </span>
                  <div className="pb-2">
                    <div className="font-mono">
                      <span className="text-3xl font-bold text-paper">05</span>
                      <span className="text-neutral-500 text-base">/100</span>
                    </div>
                    <p className="text-xs text-red-300 mt-0.5">9 violações · 3 hard locks</p>
                  </div>
                </div>

                <h3 className="mt-6 font-semibold text-lg text-paper tracking-tight">
                  SQL inline na camada de domínio
                </h3>

                {/* Code panel com linha violadora highlighted */}
                <div className="mt-4 rounded-lg overflow-hidden border border-red-500/20 bg-neutral-950/70">
                  <div className="flex items-center justify-between px-3 py-2 border-b border-neutral-800/60 bg-black/40">
                    <span className="font-mono text-[10px] text-neutral-500">
                      Sinner.Domain/Customer.cs
                    </span>
                    <span className="font-mono text-[10px] text-red-300/80">
                      ✕ 9 violations · 3 locks
                    </span>
                  </div>
                  <div
                    className="text-[12px] leading-[1.65] overflow-x-auto font-mono text-neutral-200"
                    role="region"
                    aria-label="Snippet C# do fixture the-sinner"
                  >
                    <div className="px-4 py-0.5">
                      <span className="text-neutral-500 inline-block w-5 select-none">1</span>
                      <span>public string BuildLookupSql(int customerId)</span>
                    </div>
                    <div className="px-4 py-0.5">
                      <span className="text-neutral-500 inline-block w-5 select-none">2</span>
                      <span>{"{"}</span>
                    </div>
                    <div className="px-4 py-0.5 bg-red-500/15 border-l-2 border-red-500 -ml-0.5">
                      <span className="text-red-300/90 inline-block w-5 select-none">3</span>
                      <span>{`    var sql = "SELECT * FROM Customers WHERE Id = " + customerId;`}</span>
                    </div>
                    <div className="px-4 py-0.5">
                      <span className="text-neutral-500 inline-block w-5 select-none">4</span>
                      <span>{`    return sql;`}</span>
                    </div>
                    <div className="px-4 py-0.5">
                      <span className="text-neutral-500 inline-block w-5 select-none">5</span>
                      <span>{"}"}</span>
                    </div>
                    {/* Annotation pointing at line 3 */}
                    <div className="px-4 py-2 border-t border-red-500/20 bg-red-500/[0.06] flex items-baseline gap-2">
                      <span className="text-red-400 font-mono text-[10px]">▲ linha 3</span>
                      <span className="text-neutral-500 font-mono text-[10px]">·</span>
                      <span className="text-red-300 font-mono text-[10px] font-semibold">
                        LNTY-002
                      </span>
                      <span className="text-neutral-400 font-mono text-[10px]">
                        SQL na camada de domínio · hard lock
                      </span>
                    </div>
                  </div>
                </div>

                <p className="mt-5 text-sm text-neutral-300 leading-relaxed">
                  <strong className="text-paper">Hard lock não-suprimível.</strong>{" "}
                  Selo não é emitido enquanto a violação não for resolvida —{" "}
                  <code className="font-mono text-xs text-red-300">LNTY-002</code> não
                  aceita <code className="font-mono text-xs text-neutral-300">{"// @lintty-ignore"}</code>.
                </p>
              </FadeIn>
            </div>
          </div>

          {/* Footer */}
          <p className="mt-10 font-mono text-[11px] text-neutral-500">
            <span className="text-neutral-400">→</span> Reproduza local:{" "}
            <code className="text-neutral-300">git clone</code> o repo e rode{" "}
            <code className="text-neutral-300">dotnet test</code>. Hash do laudo
            bate em qualquer máquina.
          </p>
        </div>
      </section>

      {/* ===== Determinismo bit-a-bit (showcase com pulse + typewriter) ===== */}
      <DeterminismShowcase />

      {/* ===== Defensibilidade ===== */}
      <section id="defensibilidade" className="lt-dark-glow lt-noise px-6 py-24 md:py-28 text-paper">
        <div className="max-w-6xl mx-auto">
          <FadeIn as="header" className="flex flex-col md:flex-row md:items-end md:justify-between gap-6">
            <div className="max-w-2xl">
              <p className="text-[11px] font-mono font-semibold tracking-[0.18em] text-emerald-300/80 uppercase mb-3">
                Defensibilidade &middot; moats V0
              </p>
              <h2 className="text-3xl md:text-5xl font-bold tracking-tight text-paper leading-[1.05]">
                Por que Lintty
                <br />
                <span className="text-neutral-500">não é replicável em um fim de semana.</span>
              </h2>
            </div>
            <p className="font-mono text-[11px] text-neutral-500 tracking-wide md:text-right max-w-[26ch]">
              05 moats · 01 motor · sem dependência externa V0
            </p>
          </FadeIn>

          {/* Bento grid — 2 cards grandes em cima (Canon + Determinismo),
              3 menores embaixo (Efêmero, Constant Folding, Roadmap). */}
          <ul className="mt-14 md:mt-16 grid grid-cols-1 md:grid-cols-6 gap-4 md:gap-5">
            {/* 01 — Canon opinionado (BIG) */}
            <FadeIn as="li" delayMs={0} className="md:col-span-3 lt-card-soft rounded-2xl bg-neutral-900/40 p-6 md:p-7 flex flex-col">
              <div className="flex items-baseline gap-3">
                <span className="font-mono text-[11px] font-semibold tracking-[0.18em] text-emerald-300/80 uppercase">
                  Moat 01
                </span>
                <span aria-hidden="true" className="h-px flex-1 bg-neutral-800/60" />
              </div>
              <h3 className="mt-4 font-semibold text-xl text-paper tracking-tight">
                Canon opinionado
              </h3>
              <p className="mt-2 text-sm text-neutral-400 leading-relaxed">
                Agências aprendem a entregar &ldquo;no padrão Lintty&rdquo;.
                Network effect via padrão — adoção vira moat.
              </p>
              {/* Evidence: grid de rule IDs com os hard locks destacados */}
              <div className="mt-5 grid grid-cols-3 gap-1.5 font-mono text-[11px] flex-1">
                {(["001", "002", "003", "006", "007", "008", "009"] as const).map((id) => {
                  const isHardLock = id === "001" || id === "002" || id === "007";
                  return (
                    <span
                      key={id}
                      className={`rounded border px-2 py-1 text-center ${
                        isHardLock
                          ? "border-emerald-500/40 bg-emerald-500/10 text-emerald-300"
                          : "border-neutral-800/60 bg-white/5 text-neutral-400"
                      }`}
                    >
                      LNTY-{id}
                    </span>
                  );
                })}
                <span className="rounded border border-dashed border-neutral-800/60 bg-transparent px-2 py-1 text-center text-neutral-600">
                  004*
                </span>
                <span className="rounded border border-dashed border-neutral-800/60 bg-transparent px-2 py-1 text-center text-neutral-600">
                  005*
                </span>
              </div>
              <p className="mt-3 text-[10px] font-mono text-neutral-500">
                <span className="text-emerald-300/80">■</span> hard lock &middot;{" "}
                <span className="text-neutral-400">■</span> ativo V0 &middot;{" "}
                <span className="text-neutral-600">□</span> V1+
              </p>
            </FadeIn>

            {/* 02 — Determinismo (BIG) */}
            <FadeIn as="li" delayMs={120} className="md:col-span-3 lt-card-soft rounded-2xl bg-neutral-900/40 p-6 md:p-7 flex flex-col">
              <div className="flex items-baseline gap-3">
                <span className="font-mono text-[11px] font-semibold tracking-[0.18em] text-emerald-300/80 uppercase">
                  Moat 02
                </span>
                <span aria-hidden="true" className="h-px flex-1 bg-neutral-800/60" />
              </div>
              <h3 className="mt-4 font-semibold text-xl text-paper tracking-tight">
                Determinismo bit-a-bit
              </h3>
              <p className="mt-2 text-sm text-neutral-400 leading-relaxed">
                Mesmo input &rarr; mesmo PDF, byte-a-byte. Zero alucinação,
                zero custo de inferência. Roslyn type-aware, sem LLM no V0.
              </p>
              {/* Evidence: hash compacto em pill emerald */}
              <div className="mt-5 rounded-lg bg-neutral-950/70 border border-emerald-500/15 px-4 py-3 font-mono flex-1 flex flex-col justify-center">
                <p className="text-[9px] uppercase tracking-[0.18em] text-neutral-500 mb-1">
                  hash_content &middot; sha256
                </p>
                <p className="text-[13px] text-emerald-300 break-all leading-relaxed">
                  9a4f3e21<span className="text-emerald-500/40 mx-0.5">·</span>b9c0d72f
                  <span className="text-emerald-500/40 mx-0.5">·</span>…
                  <span className="text-emerald-500/40 mx-0.5">·</span>0b21d
                </p>
                <p className="text-[10px] text-neutral-500 mt-1.5">
                  → bate em qualquer máquina, em qualquer dia
                </p>
              </div>
            </FadeIn>

            {/* 03 — Análise efêmera */}
            <FadeIn as="li" delayMs={240} className="md:col-span-2 lt-card-soft rounded-2xl bg-neutral-900/40 p-6 flex flex-col">
              <div className="flex items-baseline gap-3">
                <span className="font-mono text-[11px] font-semibold tracking-[0.18em] text-emerald-300/80 uppercase">
                  Moat 03
                </span>
                <span aria-hidden="true" className="h-px flex-1 bg-neutral-800/60" />
              </div>
              <h3 className="mt-4 font-semibold text-base text-paper tracking-tight">
                Análise efêmera
              </h3>
              <p className="mt-2 text-sm text-neutral-400 leading-relaxed flex-1">
                Web Inspector clona shallow e descarta o repo em até 60s.
                CLI nem clona — código nunca sai da máquina do cliente.
              </p>
              <p className="mt-4 font-mono text-[10px] text-neutral-500">
                clone <span className="text-paper">→</span> analyze <span className="text-paper">→</span> discard{" "}
                <span className="text-emerald-300/80">≤ 60s</span>
              </p>
            </FadeIn>

            {/* 04 — Constant folding */}
            <FadeIn as="li" delayMs={320} className="md:col-span-2 lt-card-soft rounded-2xl bg-neutral-900/40 p-6 flex flex-col">
              <div className="flex items-baseline gap-3">
                <span className="font-mono text-[11px] font-semibold tracking-[0.18em] text-emerald-300/80 uppercase">
                  Moat 04
                </span>
                <span aria-hidden="true" className="h-px flex-1 bg-neutral-800/60" />
              </div>
              <h3 className="mt-4 font-semibold text-base text-paper tracking-tight">
                Constant folding
              </h3>
              <p className="mt-2 text-sm text-neutral-400 leading-relaxed flex-1">
                Detecta SQL escondido por concatenação de constantes.
                Linter regex passa direto, Lintty pega.
              </p>
              <pre className="mt-4 rounded bg-neutral-950/70 border border-neutral-800/60 px-2.5 py-1.5 font-mono text-[10px] text-neutral-400 overflow-x-auto">
                <code>
                  <span className="text-neutral-500">{`"SELECT * FROM "`}</span>{" "}
                  + table{" "}
                  <span className="text-red-300/80">✕</span>
                </code>
              </pre>
            </FadeIn>

            {/* 05 — Roadmap V1 */}
            <FadeIn as="li" delayMs={400} className="md:col-span-2 lt-card-soft rounded-2xl bg-neutral-900/40 p-6 flex flex-col">
              <div className="flex items-baseline gap-3">
                <span className="font-mono text-[11px] font-semibold tracking-[0.18em] text-emerald-300/80 uppercase">
                  Moat 05
                </span>
                <span aria-hidden="true" className="h-px flex-1 bg-neutral-800/60" />
              </div>
              <h3 className="mt-4 font-semibold text-base text-paper tracking-tight">
                Roadmap V1
              </h3>
              <p className="mt-2 text-sm text-neutral-400 leading-relaxed flex-1">
                Complemento, não dependência. Tudo opt-in, sem quebrar o
                pitch do determinismo.
              </p>
              <ul className="mt-4 space-y-1 font-mono text-[10px] text-neutral-400">
                <li>
                  <span className="text-neutral-600">+</span> PAdES-B-LT + TSA
                  RFC 3161
                </li>
                <li>
                  <span className="text-neutral-600">+</span> audit hash-chain
                  imutável
                </li>
                <li>
                  <span className="text-neutral-600">+</span> LLM advogado · ZDR
                  Anthropic
                </li>
              </ul>
            </FadeIn>
          </ul>
        </div>
      </section>

      {/* ===== CTA final ===== */}
      <section id="contato" className="lt-dark-glow lt-noise px-6 py-24 text-paper">
        <FadeIn className="max-w-3xl mx-auto text-center">
          <p className="text-[11px] font-mono font-semibold tracking-[0.18em] text-emerald-300/80 uppercase mb-3">
            SALES CUT TIER 1
          </p>
          <h2 className="text-3xl md:text-4xl font-bold tracking-tight text-paper">
            Lintty é a evidência técnica que o contrato pede.
          </h2>
          <p className="mt-5 text-neutral-300">
            Não somos escrow financeiro, não somos árbitro jurídico, não há revisão humana.
            Somos o laudo que vai junto do aceite.
          </p>
          <div className="mt-8 flex flex-wrap justify-center gap-3">
            <a
              href="mailto:contato@lintty.com?subject=Demo%20Lintty&body=Empresa:%0AContato:%0AStack:%0A"
              className="inline-flex items-center gap-2 px-6 py-3 rounded-md bg-saint text-white font-semibold hover:bg-emerald-700 transition shadow-lg shadow-emerald-900/40"
            >
              Solicite uma demo
              <span aria-hidden="true">&rarr;</span>
            </a>
            <a
              href="mailto:contato@lintty.com"
              className="inline-flex items-center gap-2 px-6 py-3 rounded-md bg-neutral-900/60 backdrop-blur-sm text-neutral-200 font-medium hover:bg-neutral-800/70 transition"
            >
              contato@lintty.com
            </a>
            <Link
              href="/inspect"
              className="inline-flex items-center gap-2 px-6 py-3 rounded-md bg-neutral-900/60 backdrop-blur-sm text-neutral-200 font-medium hover:bg-neutral-800/70 transition"
            >
              Inspecionar repo
            </Link>
          </div>
        </FadeIn>
      </section>
    </main>
  );
}
