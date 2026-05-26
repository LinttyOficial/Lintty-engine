"use client";

import { useEffect, useState } from "react";

interface Line {
  text: string;
  /** "prompt" = green prompt; "cmd" = comando digitado; "out" = saída cinza claro; "ok"/"bad" = saída colorida; "blank" = linha em branco. */
  kind: "prompt" | "cmd" | "out" | "ok" | "bad" | "blank";
  /** Caracteres por segundo. Default 50. Texto curto vira rápido. */
  cps?: number;
  /** Pausa antes desta linha começar (ms). */
  pauseMs?: number;
}

const SCRIPT: Line[] = [
  { text: "$ lintty-engine analyze \\", kind: "cmd", cps: 65 },
  { text: "    --solution Sinner.sln \\", kind: "cmd", cps: 80 },
  { text: "    --pdf laudo-sinner.pdf", kind: "cmd", cps: 80, pauseMs: 200 },
  { text: "", kind: "blank", pauseMs: 400 },
  { text: "[lintty] Workspace loaded — 4 projects", kind: "out", cps: 220, pauseMs: 250 },
  { text: "[lintty] Running 7 analyzers...", kind: "out", cps: 220, pauseMs: 350 },
  { text: "[lintty] LNTY-002  Sinner.Domain/Customer.cs:14  HARD LOCK", kind: "bad", cps: 240, pauseMs: 250 },
  { text: "[lintty] LNTY-007  Sinner.App/Cycle.cs:33       HARD LOCK", kind: "bad", cps: 240 },
  { text: "[lintty] LNTY-003  Sinner.Domain/Order.cs:21    HIGH", kind: "bad", cps: 240 },
  { text: "[lintty] 9 violações, 3 hard locks", kind: "out", cps: 220, pauseMs: 200 },
  { text: "", kind: "blank", pauseMs: 200 },
  { text: "grade   : F", kind: "bad", cps: 80 },
  { text: "score   : 5 / 100", kind: "out", cps: 90 },
  { text: "selo    : NÃO emitido", kind: "bad", cps: 90 },
  { text: "hash    : 9a4f3e…0b21d  (deterministic)", kind: "ok", cps: 130 },
  { text: "laudo   : laudo-sinner.pdf  (148 KB)", kind: "ok", cps: 130, pauseMs: 300 },
];

const COLOR_BY_KIND: Record<Line["kind"], string> = {
  prompt: "text-emerald-300",
  cmd: "text-neutral-100",
  out: "text-neutral-400",
  ok: "text-emerald-300",
  bad: "text-red-300",
  blank: "text-neutral-500",
};

interface RenderedLine {
  text: string;
  kind: Line["kind"];
  done: boolean;
}

export function TerminalAnimated() {
  const [rendered, setRendered] = useState<RenderedLine[]>([
    { text: "", kind: SCRIPT[0].kind, done: false },
  ]);
  const [reducedMotion, setReducedMotion] = useState(false);

  useEffect(() => {
    const mq = window.matchMedia("(prefers-reduced-motion: reduce)");
    setReducedMotion(mq.matches);
    const onChange = (e: MediaQueryListEvent) => setReducedMotion(e.matches);
    mq.addEventListener("change", onChange);
    return () => mq.removeEventListener("change", onChange);
  }, []);

  // Static render for reduced motion: dump all lines fully on first paint.
  useEffect(() => {
    if (!reducedMotion) return;
    setRendered(SCRIPT.map((l) => ({ text: l.text, kind: l.kind, done: true })));
  }, [reducedMotion]);

  // Animated render (idx, charIdx) — drives the typewriter.
  useEffect(() => {
    if (reducedMotion) return undefined;
    let cancelled = false;
    let timer: number | undefined;

    let lineIdx = 0;
    let charIdx = 0;

    function step() {
      if (cancelled) return;
      if (lineIdx >= SCRIPT.length) {
        return;
      }
      const line = SCRIPT[lineIdx];
      const cps = line.cps ?? 50;
      const delayPerChar = Math.max(8, 1000 / cps);
      const finished = charIdx >= line.text.length;
      if (finished) {
        setRendered((prev) => {
          const next = [...prev];
          next[lineIdx] = { text: line.text, kind: line.kind, done: true };
          return next;
        });
        lineIdx += 1;
        charIdx = 0;
        if (lineIdx >= SCRIPT.length) return;
        const nextLine = SCRIPT[lineIdx];
        const pause = nextLine.pauseMs ?? 80;
        setRendered((prev) => [
          ...prev,
          { text: "", kind: nextLine.kind, done: false },
        ]);
        timer = window.setTimeout(step, pause);
        return;
      }
      charIdx += 1;
      setRendered((prev) => {
        const next = [...prev];
        next[lineIdx] = {
          text: line.text.slice(0, charIdx),
          kind: line.kind,
          done: false,
        };
        return next;
      });
      timer = window.setTimeout(step, delayPerChar);
    }

    timer = window.setTimeout(step, 600);
    return () => {
      cancelled = true;
      if (timer !== undefined) window.clearTimeout(timer);
    };
  }, [reducedMotion]);

  return (
    <div
      className="lt-card-soft relative rounded-2xl bg-neutral-950/85 backdrop-blur-sm shadow-2xl shadow-emerald-900/30 overflow-hidden"
      aria-label="Demo do CLI Lintty rodando"
    >
      <div className="flex items-center gap-2 px-4 py-3 bg-black/40">
        <span className="w-2.5 h-2.5 rounded-full bg-neutral-700/70" />
        <span className="w-2.5 h-2.5 rounded-full bg-neutral-700/70" />
        <span className="w-2.5 h-2.5 rounded-full bg-neutral-700/70" />
        <span className="ml-3 text-[11px] font-mono text-neutral-500 tracking-wide">
          lintty-engine — analyze
        </span>
      </div>

      <pre className="p-5 font-mono text-[13px] leading-relaxed min-h-[360px] md:min-h-[420px] overflow-x-auto">
        {rendered.map((line, i) => {
          const last = i === rendered.length - 1 && !line.done;
          return (
            <div key={i} className={COLOR_BY_KIND[line.kind]}>
              {line.text}
              {last && <span className="lt-caret" />}
              {!last && line.kind === "blank" && " "}
            </div>
          );
        })}
      </pre>
    </div>
  );
}
