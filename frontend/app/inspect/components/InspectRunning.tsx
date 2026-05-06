"use client";

/**
 * Estado B do Web Inspector — exibe spinner indeterminado, label do estágio
 * atual e contador de tempo. Recebe stage do polling do pai e calcula
 * elapsed via useEffect interval.
 *
 * Botão "Voltar ao formulário" só interrompe o polling local — o job
 * segue rodando no backend (mesma cópia do legacy).
 */

import { useEffect, useState } from "react";

const STAGE_LABELS: Record<string, string> = {
  queued: "Na fila...",
  cloning: "Clonando o repositório...",
  restoring: "Restaurando pacotes NuGet...",
  analyzing: "Analisando arquitetura com o motor Roslyn...",
  rendering: "Gerando laudo PDF...",
};

interface InspectRunningProps {
  jobId: string;
  pollPath: string;
  /** "queued" | "cloning" | "restoring" | "analyzing" | "rendering" */
  stage: string;
  /** Timestamp do início do polling (Date.now()). */
  startedAt: number;
  onCancel: () => void;
}

export function InspectRunning({
  jobId,
  pollPath,
  stage,
  startedAt,
  onCancel,
}: InspectRunningProps) {
  const [elapsed, setElapsed] = useState(0);

  useEffect(() => {
    const id = window.setInterval(() => {
      setElapsed(Math.floor((Date.now() - startedAt) / 1000));
    }, 1000);
    return () => window.clearInterval(id);
  }, [startedAt]);

  const mm = String(Math.floor(elapsed / 60)).padStart(2, "0");
  const ss = String(elapsed % 60).padStart(2, "0");
  const stageLabel = STAGE_LABELS[stage] ?? STAGE_LABELS.analyzing;

  return (
    <section className="mt-10" aria-labelledby="running-heading" aria-live="polite">
      <div className="bg-white border border-neutral-200 rounded-xl p-6 md:p-8 shadow-sm">
        <p className="text-xs font-mono text-neutral-500">job_id: {jobId}</p>
        <h2 id="running-heading" className="mt-2 text-2xl font-bold tracking-tight">
          <span>{stageLabel}</span>
        </h2>

        <div className="mt-6 flex items-center gap-4">
          <div className="lt-spinner" aria-hidden="true" />
          <div className="flex-1">
            <div
              className="lt-progress"
              role="progressbar"
              aria-label="Análise em progresso"
            />
            <p className="mt-3 text-sm text-neutral-600">
              Tempo decorrido:{" "}
              <span className="font-mono">
                {mm}:{ss}
              </span>
              <span className="text-neutral-400"> &middot; </span>
              limite: 15 min
            </p>
          </div>
        </div>

        <p className="mt-6 text-sm text-neutral-600">
          Pipeline determinístico em execução. Você pode fechar a aba — os artefatos ficam
          disponíveis por 24h via{" "}
          <code className="font-mono text-xs">{pollPath}</code>.
        </p>

        <button
          type="button"
          onClick={onCancel}
          className="mt-6 inline-flex items-center gap-2 px-4 py-2 rounded-md border border-neutral-300 text-sm text-neutral-700 hover:bg-neutral-100 transition"
        >
          Voltar ao formulário
        </button>
        <p className="mt-2 text-xs text-neutral-500">
          O job continua rodando no backend; voltar só fecha esta aba do polling.
        </p>
      </div>
    </section>
  );
}
