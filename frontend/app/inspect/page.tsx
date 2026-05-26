"use client";

/**
 * Web Inspector — fluxo completo de 4 estados (form / running / completed
 * / failed) com polling de 2s e timeout de 16 min. Replica fielmente o
 * legacy `landing/inspect.html`.
 *
 * Fluxo:
 *   1. Estado "form": usuário cola URL do GitHub → POST /api/jobs
 *   2. Resposta 202 → estado "running" + começa polling de
 *      GET /api/jobs/{id} a cada POLL_INTERVAL_MS.
 *   3. Polling vê status === "completed" → estado "completed" com PDF/JSON.
 *      Polling vê status === "failed" → estado "failed" com error_code.
 *   4. Botões "Voltar" / "Tentar novamente" / "Analisar outro" voltam para
 *      "form" preservando os campos digitados.
 *
 * QA helper: `?state=form|running|completed|failed` força o estado em
 * hostnames locais para inspeção visual sem precisar do backend rodando.
 */

import Link from "next/link";
import { useEffect, useRef, useState } from "react";
import { Header } from "@/components/Header";
import { Footer } from "@/components/Footer";
import { SkipLink } from "@/components/SkipLink";
import { AuroraPageHeader } from "@/components/HeroAurora";
import { apiFetch } from "@/lib/api";
import { InspectForm, type InspectFormPayload } from "./components/InspectForm";
import { InspectRunning } from "./components/InspectRunning";
import {
  InspectCompleted,
  type InspectCompletedData,
} from "./components/InspectCompleted";
import { InspectFailed } from "./components/InspectFailed";

type InspectState = "form" | "running" | "completed" | "failed";

const POLL_INTERVAL_MS = 2000;
const POLL_TIMEOUT_MS = 16 * 60 * 1000; // 16 min — worker é 15 min

interface JobPostResponse {
  job_id: string;
  poll_url?: string;
  error?: string;
  message?: string;
}

interface JobPollResponse {
  job_id?: string;
  status?: string; // "queued" | "running" | "completed" | "failed"
  stage?: string;
  // completed
  grade?: string;
  score?: number;
  violation_count?: number;
  hard_locks_open?: number;
  canon_version?: string;
  pdf_url?: string;
  json_url?: string;
  expires_at?: string | null;
  // failed
  error_code?: string;
  error_message?: string;
}

function humanizeRetryAfter(headerVal: string | null): string {
  if (!headerVal) return "24h";
  const seconds = parseInt(headerVal, 10);
  if (Number.isNaN(seconds) || seconds <= 0) return "24h";
  if (seconds < 60) return `${seconds}s`;
  if (seconds < 3600) return `${Math.ceil(seconds / 60)} min`;
  return `${Math.ceil(seconds / 3600)}h`;
}

export default function InspectPage() {
  const [state, setState] = useState<InspectState>("form");
  const [submitting, setSubmitting] = useState(false);
  const [globalError, setGlobalError] = useState<string | null>(null);
  const [urlError, setUrlError] = useState<string | null>(null);

  // URL pré-preenchida vinda do InlineInspectInput do hero (`?url=...`).
  // Lemos via window.location no mount pra evitar Suspense boundary do
  // useSearchParams sob static export.
  const [prefilledUrl, setPrefilledUrl] = useState<string>("");
  useEffect(() => {
    if (typeof window === "undefined") return;
    const sp = new URLSearchParams(window.location.search);
    const u = sp.get("url");
    if (u) setPrefilledUrl(u);
  }, []);

  // Polling state
  const [jobId, setJobId] = useState<string>("");
  const [pollPath, setPollPath] = useState<string>("");
  const [stage, setStage] = useState<string>("queued");
  const [startedAt, setStartedAt] = useState<number>(Date.now());

  // Result state
  const [completedData, setCompletedData] = useState<InspectCompletedData | null>(null);
  const [errorCode, setErrorCode] = useState<string>("internal_error");
  const [errorDetail, setErrorDetail] = useState<string | null>(null);

  // Timers — refs para sobreviver re-renders sem virar dependency.
  const pollTimer = useRef<number | null>(null);
  const pollActive = useRef<boolean>(false);
  const pollStartRef = useRef<number>(0);
  const currentJobRef = useRef<string>("");

  function stopPolling() {
    pollActive.current = false;
    if (pollTimer.current !== null) {
      window.clearTimeout(pollTimer.current);
      pollTimer.current = null;
    }
  }

  // Garante limpeza dos timers no unmount da página.
  useEffect(() => {
    return () => {
      stopPolling();
    };
  }, []);

  // Foca no primeiro h2 da nova seção quando o estado muda — replica o
  // showState() do legacy.
  useEffect(() => {
    if (typeof window === "undefined") return;
    window.scrollTo({ top: 0, behavior: "smooth" });
    const heading = document.querySelector<HTMLHeadingElement>(
      "main#main h2[id]",
    );
    if (heading) {
      heading.setAttribute("tabindex", "-1");
      try {
        heading.focus({ preventScroll: false });
      } catch {
        heading.focus();
      }
    }
  }, [state]);

  // ============================================================
  // QA dev-only: ?state=running|completed|failed força o estado.
  // ============================================================
  useEffect(() => {
    if (typeof window === "undefined") return;

    const isLocal =
      window.location.hostname === "localhost" ||
      window.location.hostname === "127.0.0.1" ||
      window.location.protocol === "file:";
    if (!isLocal) return;

    const params = new URLSearchParams(window.location.search);
    const forced = params.get("state");
    if (!forced) return;
    if (!["form", "running", "completed", "failed"].includes(forced)) return;

    if (forced === "running") {
      setJobId("01K7QA0DEV0SAMPLEJOBIDXYZAB");
      setPollPath("/api/jobs/01K7QA0DEV0SAMPLEJOBIDXYZAB");
      setStage("analyzing");
      setStartedAt(Date.now() - 83 * 1000);
      setState("running");
    } else if (forced === "completed") {
      setCompletedData({
        job_id: "01K7QA0DEV0SAMPLEJOBIDXYZAB",
        grade: "A",
        score: 100,
        violation_count: 0,
        hard_locks_open: 0,
        canon_version: "1.0",
        pdf_url: "/api/jobs/01K7QA0DEV0SAMPLEJOBIDXYZAB/laudo.pdf",
        json_url: "/api/jobs/01K7QA0DEV0SAMPLEJOBIDXYZAB/report.json",
        expires_at: new Date(Date.now() + 24 * 3600 * 1000).toISOString(),
      });
      setState("completed");
    } else if (forced === "failed") {
      setErrorCode("compile_failed");
      setErrorDetail('CS0246: o nome do tipo "Foo" não pôde ser localizado.');
      setState("failed");
    }
  }, []);

  // ============================================================
  // POST /api/jobs
  // ============================================================
  async function handleSubmit(payload: InspectFormPayload) {
    setSubmitting(true);
    setGlobalError(null);
    setUrlError(null);

    try {
      const res = await apiFetch<JobPostResponse>("/api/jobs", {
        method: "POST",
        body: JSON.stringify(payload),
      });
      setSubmitting(false);

      if (res.status === 202 && res.body && res.body.job_id) {
        const newJobId = res.body.job_id;
        const newPollPath = res.body.poll_url ?? `/api/jobs/${newJobId}`;
        startPolling(newJobId, newPollPath);
        return;
      }

      handlePostError(res.status, res.body, res.headers);
    } catch (err) {
      console.error("POST /api/jobs falhou", err);
      setSubmitting(false);
      setGlobalError(
        "Não foi possível contatar o servidor. Verifique sua conexão e tente novamente. Se persistir, use o CLI local.",
      );
    }
  }

  function handlePostError(
    status: number,
    body: JobPostResponse | null,
    headers: Headers,
  ) {
    const errorCodeFromBody = body?.error ?? "";
    const serverMsg = body?.message ?? "";

    if (status === 400) {
      if (errorCodeFromBody === "invalid_url" || errorCodeFromBody === "host_not_allowed") {
        setUrlError("URL inválida. Use https://github.com/owner/repo.");
        return;
      }
      if (errorCodeFromBody === "repo_not_accessible") {
        setGlobalError(
          "Repositório não encontrado ou inacessível. Se for privado, baixe o CLI local — link no fim da página.",
        );
        return;
      }
      if (errorCodeFromBody === "repo_forbidden") {
        setGlobalError(
          "Repo privado detectado. V0 não suporta clone privado via Web. Baixe o CLI local para analisar com o código na sua máquina.",
        );
        return;
      }
      if (errorCodeFromBody === "repo_too_large") {
        setGlobalError(
          "Repositório > 500 MB. Para repos grandes, use o CLI local sem limite de tamanho.",
        );
        return;
      }
      if (errorCodeFromBody === "validation_failed") {
        setGlobalError(
          `Dados inválidos: ${serverMsg || "verifique os campos."}`,
        );
        return;
      }
      setGlobalError(serverMsg || "Requisição inválida.");
      return;
    }

    if (status === 429) {
      const retryAfter = headers.get("Retry-After");
      const hint = retryAfter
        ? ` Tente novamente em ${humanizeRetryAfter(retryAfter)}.`
        : "";
      setGlobalError(
        `Limite por IP atingido (3 jobs/dia anônimo).${hint} Para uso recorrente, baixe o CLI local.`,
      );
      return;
    }

    if (status === 503) {
      setGlobalError(
        "Fila cheia (worker ocupado). Tente em alguns segundos. Se for urgente, baixe o CLI local.",
      );
      return;
    }

    setGlobalError(
      serverMsg || `Erro ${status}. Tente novamente ou use o CLI local.`,
    );
  }

  // ============================================================
  // Polling GET /api/jobs/{id}
  // ============================================================
  function startPolling(newJobId: string, newPollPath: string) {
    currentJobRef.current = newJobId;
    pollActive.current = true;
    pollStartRef.current = Date.now();

    setJobId(newJobId);
    setPollPath(newPollPath);
    setStage("queued");
    setStartedAt(pollStartRef.current);
    setState("running");

    scheduleNextPoll();
  }

  function scheduleNextPoll() {
    if (!pollActive.current) return;
    pollTimer.current = window.setTimeout(() => {
      void pollOnce();
    }, POLL_INTERVAL_MS);
  }

  async function pollOnce() {
    if (!pollActive.current) return;

    if (Date.now() - pollStartRef.current > POLL_TIMEOUT_MS) {
      stopPolling();
      setErrorCode("timeout");
      setErrorDetail(null);
      setState("failed");
      return;
    }

    const id = currentJobRef.current;
    try {
      const res = await apiFetch<JobPollResponse>(
        `/api/jobs/${encodeURIComponent(id)}`,
      );

      if (res.status === 404) {
        stopPolling();
        setErrorCode("internal_error");
        setErrorDetail("job_id não encontrado no servidor (404).");
        setState("failed");
        return;
      }

      const body = res.body;
      if (!body) {
        scheduleNextPoll();
        return;
      }

      if (body.status === "queued" || body.status === "running") {
        const newStage = body.stage ?? body.status ?? "analyzing";
        setStage(newStage);
        scheduleNextPoll();
        return;
      }

      if (body.status === "completed") {
        stopPolling();
        setCompletedData({
          job_id: body.job_id ?? id,
          grade: body.grade ?? "F",
          score: body.score ?? 0,
          violation_count: body.violation_count ?? 0,
          hard_locks_open: body.hard_locks_open ?? 0,
          canon_version: body.canon_version ?? "1.0",
          pdf_url: body.pdf_url ?? "",
          json_url: body.json_url ?? "",
          expires_at: body.expires_at ?? null,
        });
        setState("completed");
        return;
      }

      if (body.status === "failed") {
        stopPolling();
        setErrorCode(body.error_code ?? "internal_error");
        setErrorDetail(body.error_message ?? null);
        setState("failed");
        return;
      }

      // Unknown status — keep polling.
      scheduleNextPoll();
    } catch (err) {
      console.error(`GET /api/jobs/${id} falhou`, err);
      // Network glitch: try again next tick.
      scheduleNextPoll();
    }
  }

  function handleCancelRunning() {
    stopPolling();
    setState("form");
  }

  function handleCompletedRestart() {
    setCompletedData(null);
    setState("form");
  }

  function handleFailedRetry() {
    setErrorDetail(null);
    setGlobalError(null);
    setState("form");
  }

  return (
    <>
      <SkipLink />
      <Header />
      <AuroraPageHeader
        maxWidthClass="max-w-2xl"
        eyebrow="Web Inspector · V0"
        title="Cole a URL do GitHub. Receba o laudo PDF."
        subtitle={
          <>
            Mesmo motor determinístico do CLI, rodando no nosso backend. Clone efêmero,
            descartado em até 60 segundos. Para uso recorrente ou repo privado,{" "}
            <Link
              href="/cli"
              className="underline decoration-emerald-400/50 underline-offset-2 hover:text-paper hover:decoration-emerald-300"
            >
              baixe o CLI local
            </Link>{" "}
            — o código nunca sai da sua máquina.
          </>
        }
      />
      <main id="main" className="lt-dark-glow lt-noise px-6 py-12 md:py-16 text-paper">
        <div className="max-w-2xl mx-auto">
          {state === "form" && (
            <InspectForm
              globalError={globalError}
              submitting={submitting}
              urlError={urlError}
              defaultUrl={prefilledUrl}
              onSubmit={handleSubmit}
              onClearErrors={() => {
                setGlobalError(null);
                setUrlError(null);
              }}
            />
          )}

          {state === "running" && (
            <InspectRunning
              jobId={jobId}
              pollPath={pollPath}
              stage={stage}
              startedAt={startedAt}
              onCancel={handleCancelRunning}
            />
          )}

          {state === "completed" && completedData && (
            <InspectCompleted
              data={completedData}
              onRestart={handleCompletedRestart}
            />
          )}

          {state === "failed" && (
            <InspectFailed
              errorCode={errorCode}
              errorDetail={errorDetail}
              onRetry={handleFailedRetry}
            />
          )}
        </div>
      </main>
      <Footer />
    </>
  );
}
