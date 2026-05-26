"use client";

import { useEffect, useRef, useState } from "react";

interface ViolationRow {
  id: string;
  body: string;
}

const VIOLATIONS: ViolationRow[] = [
  { id: "LNTY-002", body: "SQL na camada de domínio" },
  { id: "LNTY-007", body: "Ciclo Application ↔ Infrastructure" },
  { id: "LNTY-003", body: "Mutável exposto em agregado" },
];

const HASH = "9a4f3e21b9c0d72f8e58473b6a118c10d4bb86f3e0a1d2c4509a8e6dc20f0b21d";
const HASH_PREFIX = HASH.slice(0, 8);
const HASH_TAIL = HASH.slice(-5);

/** Segmenta o hash em blocos de 8 chars para leitura. */
function segmentHash(hex: string, group = 8): string[] {
  const out: string[] = [];
  for (let i = 0; i < hex.length; i += group) {
    out.push(hex.slice(i, i + group));
  }
  return out;
}

/**
 * Seção "Determinismo bit-a-bit". Dois laudos rodados em máquinas e dias
 * diferentes — mesmo hash. Selo `=` central afirma a igualdade. Embaixo,
 * um mini terminal mostra o ritual de verificação que qualquer um pode
 * reproduzir. Quando a section entra na viewport, as violações aparecem
 * com efeito typewriter — um após o outro.
 */
export function DeterminismShowcase() {
  const rootRef = useRef<HTMLDivElement | null>(null);
  const [revealed, setRevealed] = useState(0);

  useEffect(() => {
    if (typeof window === "undefined") return undefined;
    const reducedMotion = window.matchMedia(
      "(prefers-reduced-motion: reduce)",
    ).matches;
    if (reducedMotion) {
      setRevealed(VIOLATIONS.length);
      return undefined;
    }

    const el = rootRef.current;
    if (!el || typeof IntersectionObserver === "undefined") return undefined;

    let timer: number | undefined;
    const io = new IntersectionObserver(
      (entries) => {
        for (const entry of entries) {
          if (entry.isIntersecting) {
            io.disconnect();
            let idx = 0;
            const tick = () => {
              idx += 1;
              setRevealed(idx);
              if (idx < VIOLATIONS.length) {
                timer = window.setTimeout(tick, 1100);
              }
            };
            timer = window.setTimeout(tick, 500);
            break;
          }
        }
      },
      { threshold: 0.35 },
    );
    io.observe(el);
    return () => {
      io.disconnect();
      if (timer !== undefined) window.clearTimeout(timer);
    };
  }, []);

  const hashSegments = segmentHash(HASH, 8);

  return (
    <section
      ref={rootRef}
      id="determinismo"
      className="lt-dark-glow lt-noise px-6 py-24 md:py-28 text-paper"
      aria-labelledby="determinism-heading"
    >
      <div className="max-w-6xl mx-auto">
        {/* Header */}
        <header className="flex flex-col md:flex-row md:items-end md:justify-between gap-6">
          <div className="max-w-2xl">
            <p className="text-[11px] font-mono font-semibold tracking-[0.18em] text-emerald-300/80 uppercase mb-3">
              Determinismo bit-a-bit &middot; zero ruído
            </p>
            <h2
              id="determinism-heading"
              className="text-3xl md:text-5xl font-bold tracking-tight text-paper leading-[1.05]"
            >
              Mesmo código.
              <br />
              <span className="text-neutral-500">Mesmo PDF. Sempre.</span>
            </h2>
          </div>
          <p className="font-mono text-[11px] text-neutral-500 tracking-wide md:text-right max-w-[28ch]">
            sha256(report) bate em qualquer máquina, em qualquer dia
          </p>
        </header>

        {/* Comparativo dos dois laudos com selo "=" central */}
        <div className="relative mt-14 md:mt-16">
          {/* Selo "=" central — afirma igualdade entre os dois laudos */}
          <div
            aria-hidden="true"
            className="hidden md:flex absolute left-1/2 top-1/2 -translate-x-1/2 -translate-y-1/2 z-10 items-center justify-center w-12 h-12 rounded-full bg-ink border border-emerald-500/40 font-mono text-xl font-semibold text-emerald-300 shadow-[0_8px_24px_-8px_rgba(16,185,129,0.45)]"
          >
            =
          </div>

          <div className="grid md:grid-cols-2 gap-6 md:gap-8">
            <LaudoCard
              run="01"
              machine="macOS · arm64"
              timestamp="2026-05-26 14:08"
              violationsShown={revealed}
            />
            <LaudoCard
              run="02"
              machine="Linux CI · x64"
              timestamp="2026-05-27 09:21"
              violationsShown={revealed}
            />
          </div>
        </div>

        {/* Hash treatment — peça central com label, segmentos e legenda */}
        <div className="mt-12 md:mt-14 rounded-2xl bg-neutral-950/60 border border-emerald-500/20 p-6 md:p-8 shadow-[0_20px_60px_-30px_rgba(16,185,129,0.4)]">
          <div className="flex flex-col md:flex-row md:items-end md:justify-between gap-4 mb-5">
            <p className="text-[10px] font-mono uppercase tracking-[0.18em] text-emerald-300/80">
              hash_content &middot; sha256 &middot; embebido no rodapé do PDF
            </p>
            <p className="text-[10px] font-mono text-neutral-500">
              idêntico nos dois laudos acima
            </p>
          </div>

          <code
            className="block font-mono text-[13px] md:text-base text-emerald-300 break-all leading-relaxed tracking-wide"
            aria-label="Hash sha256 do report.json"
          >
            {hashSegments.map((seg, i) => (
              <span key={i} className="inline-block">
                {seg}
                {i < hashSegments.length - 1 && (
                  <span className="text-emerald-500/40 mx-1">·</span>
                )}
              </span>
            ))}
          </code>

          <div className="mt-5 grid grid-cols-3 gap-4 pt-4 border-t border-neutral-800/60">
            <div>
              <p className="text-[10px] font-mono uppercase tracking-widest text-neutral-500">
                timestamp
              </p>
              <p className="mt-1 text-sm text-neutral-300 font-mono">não embutido</p>
            </div>
            <div>
              <p className="text-[10px] font-mono uppercase tracking-widest text-neutral-500">
                machine id
              </p>
              <p className="mt-1 text-sm text-neutral-300 font-mono">não embutido</p>
            </div>
            <div>
              <p className="text-[10px] font-mono uppercase tracking-widest text-neutral-500">
                ordem de violações
              </p>
              <p className="mt-1 text-sm text-emerald-300/90 font-mono">canônica</p>
            </div>
          </div>
        </div>

        {/* Mini terminal — proof by replication */}
        <div className="mt-8 md:mt-10 rounded-xl overflow-hidden border border-neutral-800/60 bg-neutral-950/85 backdrop-blur-sm shadow-2xl shadow-black/40 max-w-3xl mx-auto">
          <div className="flex items-center gap-2 px-4 py-2.5 bg-black/40 border-b border-neutral-800/60">
            <span className="w-2.5 h-2.5 rounded-full bg-neutral-700/70" />
            <span className="w-2.5 h-2.5 rounded-full bg-neutral-700/70" />
            <span className="w-2.5 h-2.5 rounded-full bg-neutral-700/70" />
            <span className="ml-3 text-[11px] font-mono text-neutral-500 tracking-wide">
              verificar — sha256sum
            </span>
          </div>
          <pre className="px-4 py-4 font-mono text-[12px] leading-[1.7] overflow-x-auto">
            <span className="text-neutral-500">$ </span>
            <span className="text-neutral-200">sha256sum laudo-run-01.pdf laudo-run-02.pdf</span>
            {"\n"}
            <span className="text-emerald-300/90">{HASH_PREFIX}</span>
            <span className="text-emerald-500/40">…{HASH_TAIL}</span>
            <span className="text-neutral-400">  laudo-run-01.pdf</span>
            {"\n"}
            <span className="text-emerald-300/90">{HASH_PREFIX}</span>
            <span className="text-emerald-500/40">…{HASH_TAIL}</span>
            <span className="text-neutral-400">  laudo-run-02.pdf</span>
            {"\n\n"}
            <span className="text-neutral-500"># </span>
            <span className="text-neutral-400">identical · sem flag, sem heurística, sem &ldquo;quase&rdquo;</span>
          </pre>
        </div>
      </div>
    </section>
  );
}

interface LaudoCardProps {
  run: string;
  machine: string;
  timestamp: string;
  violationsShown: number;
}

function LaudoCard({ run, machine, timestamp, violationsShown }: LaudoCardProps) {
  return (
    <article className="lt-card-soft relative rounded-2xl bg-neutral-900/40 p-6 md:p-7 backdrop-blur-sm">
      {/* Header — run id + machine context */}
      <div className="flex items-start justify-between gap-3">
        <div>
          <p className="text-[10px] font-mono uppercase tracking-[0.18em] text-emerald-300/80">
            Run {run}
          </p>
          <p className="mt-1 font-mono text-xs text-neutral-300">{machine}</p>
        </div>
        <p className="font-mono text-[10px] text-neutral-500 text-right leading-tight">
          {timestamp}
        </p>
      </div>

      {/* Score */}
      <div className="mt-5 flex items-end gap-4">
        <div className="lt-pulse-ring inline-flex items-center justify-center w-20 h-20 rounded-2xl bg-red-950/40">
          <span className="text-5xl font-bold text-red-300 leading-none">F</span>
        </div>
        <div className="pb-1.5">
          <div className="font-mono">
            <span className="text-2xl font-bold text-paper">05</span>
            <span className="text-neutral-500 text-sm">/100</span>
          </div>
          <p className="text-[11px] text-red-300 mt-0.5">9 violações · 3 hard locks</p>
        </div>
      </div>

      {/* Selo bloqueado */}
      <div className="mt-4 inline-flex items-center gap-2 px-3 py-1 rounded-full bg-red-950/50 border border-red-500/30 text-red-300 text-[11px] font-semibold">
        <span aria-hidden="true">✕</span>
        Selo NÃO emitido
      </div>

      <div className="my-5 h-px bg-gradient-to-r from-transparent via-neutral-800/60 to-transparent" />

      {/* Violations list — typewriter reveal */}
      <ul className="text-[12px] space-y-2 font-mono">
        {VIOLATIONS.map((v, i) => {
          const visible = i < violationsShown;
          return (
            <li
              key={v.id}
              className={`flex gap-3 transition-opacity duration-500 ${
                visible ? "opacity-100" : "opacity-0"
              }`}
              aria-hidden={!visible}
            >
              <span className="text-emerald-300/70 shrink-0">{v.id}</span>
              <span className="text-neutral-300 font-sans text-[13px]">{v.body}</span>
            </li>
          );
        })}
        {violationsShown < VIOLATIONS.length && (
          <li className="flex gap-3 text-neutral-600 text-[11px] font-mono">
            <span className="lt-caret">·</span>
          </li>
        )}
      </ul>
    </article>
  );
}
