"use client";

/**
 * Estado C do Web Inspector — score grande + CTAs para baixar PDF/JSON.
 *
 * `pdf_url` e `json_url` vêm relativos do backend (ex: `/api/jobs/{id}/laudo.pdf`),
 * então convertemos via `backendUrl()` para o `<a href>` apontar para
 * `localhost:5180` em dev e mesma origem em prod.
 */

import Link from "next/link";
import { backendUrl } from "@/lib/api";

export interface InspectCompletedData {
  job_id: string;
  grade: string; // "A" | "B" | "C" | "D" | "F"
  score: number;
  violation_count: number;
  hard_locks_open: number;
  canon_version: string;
  pdf_url: string;
  json_url: string;
  expires_at: string | null;
}

interface InspectCompletedProps {
  data: InspectCompletedData;
  onRestart: () => void;
}

const VALID_GRADES = new Set(["A", "B", "C", "D", "F"]);

export function InspectCompleted({ data, onRestart }: InspectCompletedProps) {
  const grade = (data.grade || "F").toUpperCase();
  const gradeClass = VALID_GRADES.has(grade) ? `grade-${grade}` : "grade-F";

  const pdfHref = data.pdf_url ? backendUrl(data.pdf_url) : "#";
  const jsonHref = data.json_url ? backendUrl(data.json_url) : "#";
  const pdfFilename = `laudo-${data.job_id || "lintty"}.pdf`;

  let expiresLabel = "24h";
  if (data.expires_at) {
    try {
      expiresLabel = new Date(data.expires_at).toLocaleString("pt-BR", {
        day: "2-digit",
        month: "2-digit",
        year: "numeric",
        hour: "2-digit",
        minute: "2-digit",
      });
    } catch {
      expiresLabel = data.expires_at;
    }
  }

  return (
    <section className="mt-10" aria-labelledby="completed-heading">
      <div className="lt-card-form p-6 md:p-8">
        <p className="text-xs font-semibold tracking-widest text-emerald-300/80 uppercase">
          Análise concluída
        </p>

        <div className="mt-4 flex items-center gap-6">
          <div className={`grade-badge ${gradeClass}`} aria-label="Grade">
            {grade}
          </div>
          <div>
            <h2 id="completed-heading" className="text-2xl font-bold tracking-tight text-paper">
              Score <span>{data.score ?? 0}</span>/100
            </h2>
            <p className="mt-1 text-sm text-neutral-300">
              <span>{data.violation_count ?? 0}</span> violações &middot;{" "}
              <span>{data.hard_locks_open ?? 0}</span> hard locks abertos
            </p>
            <p className="mt-1 text-xs text-neutral-500">
              Lintty Canon <span>v{data.canon_version || "1.0"}</span>
            </p>
          </div>
        </div>

        <div className="mt-8 flex flex-wrap gap-3">
          <a
            href={pdfHref}
            download={pdfFilename}
            className="lt-btn-primary"
          >
            Baixar laudo.pdf
            <span aria-hidden="true">↓</span>
          </a>
          <a
            href={jsonHref}
            target="_blank"
            rel="noopener"
            className="lt-btn-secondary"
          >
            Ver JSON
          </a>
        </div>

        <p className="mt-6 text-xs text-neutral-500">
          Os artefatos expiram em <span className="font-mono text-neutral-300">{expiresLabel}</span>. PDF
          gerado com <code className="font-mono text-neutral-300">hash_content</code> SHA-256 no rodapé —
          rode o mesmo commit no{" "}
          <Link href="/cli" className="underline decoration-emerald-400/50 underline-offset-2 text-emerald-200 hover:text-emerald-100">
            CLI local
          </Link>{" "}
          e o hash bate.
        </p>

        <button
          type="button"
          onClick={onRestart}
          className="mt-6 lt-btn-secondary text-sm"
        >
          Analisar outro repositório
        </button>
      </div>
    </section>
  );
}
