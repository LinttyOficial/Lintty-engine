"use client";

import {
  useEffect,
  useState,
  type CSSProperties,
  type ReactNode,
} from "react";
import { TerminalAnimated } from "./TerminalAnimated";
import { InlineInspectInput } from "./InlineInspectInput";

interface Beam {
  id: number;
  style: CSSProperties;
}

const HERO_BEAM_COUNT = 60;
const COMPACT_BEAM_COUNT = 36;

function useBeams(count: number): Beam[] {
  const [beams, setBeams] = useState<Beam[]>([]);
  useEffect(() => {
    const generated: Beam[] = Array.from({ length: count }).map((_, i) => {
      const riseDur = Math.random() * 2 + 4;
      const fadeDur = riseDur;
      return {
        id: i,
        style: {
          left: `${Math.random() * 100}%`,
          width: `${Math.floor(Math.random() * 3) + 1}px`,
          animationDelay: `${Math.random() * 5}s`,
          animationDuration: `${riseDur}s, ${fadeDur}s`,
        },
      };
    });
    setBeams(generated);
  }, [count]);
  return beams;
}

function AuroraBackground({ beams }: { beams: Beam[] }) {
  return (
    <div className="lt-scene" aria-hidden="true">
      <div className="lt-main-column" />
      <div className="lt-beam-container">
        {beams.map((b) => (
          <div key={b.id} className="lt-beam" style={b.style} />
        ))}
      </div>
      <div className="lt-floor" />
    </div>
  );
}

/**
 * Full landing hero: copy + CTAs on the left, laudo mock card on the right,
 * aurora background. Used only on `/`.
 */
export function HeroAurora() {
  const beams = useBeams(HERO_BEAM_COUNT);

  return (
    <section className="relative overflow-hidden bg-ink text-paper">
      <AuroraBackground beams={beams} />

      <div className="relative z-10 px-6 pt-28 md:pt-36 pb-24 md:pb-28">
        <div className="max-w-6xl mx-auto grid md:grid-cols-12 gap-12 items-center">
          <div className="md:col-span-6">
            <p className="text-[11px] font-mono font-semibold tracking-[0.18em] uppercase mb-5 text-emerald-300/80">
              AUDITORIA AUTOMATIZADA .NET &middot; V0
            </p>
            <h1 className="text-4xl md:text-5xl lg:text-[3.75rem] font-bold leading-[1.05] tracking-[-0.025em] text-paper">
              Arquitetura como{" "}
              <span className="lt-grad-text">evidência</span>.
              <br />
              <span className="text-neutral-400">
                Auditoria automatizada para entregas de software .NET.
              </span>
            </h1>
            <p className="mt-6 text-lg text-neutral-300 max-w-xl">
              Lintty analisa solutions .NET com Roslyn type-aware e emite um laudo PDF
              determinístico que vale como evidência de aceite &mdash; sem mediação humana,
              sem opinião subjetiva.
            </p>

            <InlineInspectInput />

            <div className="mt-6 flex flex-wrap gap-3">
              <a
                href="mailto:contato@lintty.com?subject=Demo%20Lintty&body=Empresa:%0AContato:%0AStack:%0A"
                className="inline-flex items-center gap-2 px-5 py-2.5 rounded-md bg-emerald-500/10 text-emerald-200 font-medium text-sm hover:bg-emerald-500/20 transition backdrop-blur-sm"
              >
                Falar com vendas
              </a>
              <a
                href="#como-funciona"
                className="inline-flex items-center gap-2 px-5 py-2.5 rounded-md bg-neutral-900/50 text-neutral-300 font-medium text-sm hover:bg-neutral-800/70 transition backdrop-blur-sm"
              >
                Como funciona
              </a>
            </div>
          </div>

          <div className="md:col-span-6">
            <div className="relative">
              <div
                className="absolute -inset-8 bg-gradient-to-br from-emerald-500/15 via-saint/10 to-transparent rounded-3xl rotate-1 blur-3xl"
                aria-hidden="true"
              />
              <div className="relative">
                <TerminalAnimated />
              </div>
            </div>
          </div>
        </div>

        {/* Trust strip — mono, fina, abaixo do hero content. Sem linha
            divisória — só espaço + fade do gradient já separa. */}
        <div className="max-w-6xl mx-auto mt-20 md:mt-24">
          <ul className="flex flex-wrap items-center justify-center gap-x-8 gap-y-3 text-[11px] font-mono uppercase tracking-[0.15em] text-neutral-500">
            <li>
              <span className="text-emerald-300/80 mr-2">7</span>regras canon ativas
            </li>
            <li>
              <span className="text-emerald-300/80 mr-2">3</span>hard locks
            </li>
            <li>100% roslyn type-aware</li>
            <li>zero llm no v0</li>
            <li>byte-determinístico</li>
          </ul>
        </div>
      </div>
    </section>
  );
}

/**
 * Slim aurora header reused on every non-landing page. Same dark-ink + saint
 * beam aesthetic as the landing hero, but tighter padding and no right slot.
 */
interface AuroraPageHeaderProps {
  eyebrow?: string;
  title: ReactNode;
  subtitle?: ReactNode;
  align?: "left" | "center";
  maxWidthClass?: string;
  extra?: ReactNode;
}

export function AuroraPageHeader({
  eyebrow,
  title,
  subtitle,
  align = "left",
  maxWidthClass = "max-w-3xl",
  extra,
}: AuroraPageHeaderProps) {
  const beams = useBeams(COMPACT_BEAM_COUNT);
  const isCenter = align === "center";

  return (
    <section className="relative overflow-hidden bg-ink text-paper">
      <AuroraBackground beams={beams} />

      <div className="relative z-10 px-6 pt-28 md:pt-32 pb-16 md:pb-20">
        <div
          className={`${maxWidthClass} mx-auto${isCenter ? " text-center" : ""}`}
        >
          {eyebrow && (
            <p className="text-[11px] font-mono font-semibold tracking-[0.18em] uppercase mb-3 text-emerald-300/80">
              {eyebrow}
            </p>
          )}
          <h1 className="text-3xl md:text-4xl lg:text-5xl font-bold leading-tight tracking-tight text-paper">
            {title}
          </h1>
          {subtitle && (
            <p
              className={`mt-4 text-base md:text-lg text-neutral-300 ${
                isCenter ? "max-w-2xl mx-auto" : "max-w-2xl"
              }`}
            >
              {subtitle}
            </p>
          )}
          {extra && <div className="mt-6">{extra}</div>}
        </div>
      </div>
    </section>
  );
}
