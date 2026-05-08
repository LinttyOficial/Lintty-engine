"use client";

/**
 * Detalhe do scan (`/dashboard/scans/[publicId]`). Sprint 3 / PR F6.
 *
 * Última peça do Sprint 3 do frontend. Onde o user materializa a
 * invariante de produto: **PDF do dashboard = PDF da CLI byte-a-byte**.
 *
 * Responsabilidades:
 *   1. Validar o `publicId` da URL (UUID v4) antes de bater no backend.
 *   2. Carregar `getScan(publicId)`. Se 404 → "scan não encontrado"
 *      (mesma mensagem para "não existe" e "existe em outra org" — §3.5
 *      do ADR 0007).
 *   3. Renderizar a UI por status:
 *      - `queued` / `running`: card de progresso com mini-spinner.
 *      - `completed`: CTA principal de download (PDF + report.json).
 *      - `failed`: mensagem mapeada (codes conhecidos) + CTA voltar ao
 *        repositório.
 *      - `cancelled`: card neutro.
 *   4. Polling de 2s enquanto o status estiver em estado não-terminal.
 *      Cleanup correto no unmount; teto de segurança de 30 min.
 *
 * Decisões editoriais (campos do `ScanDetail` que NÃO existem no
 * backend hoje — confirmado em `Persistence/Entities/Scan.cs`):
 *   - **Não há `grade`** no `ScanResponse`. O bloco de grade é V1.1; por
 *     enquanto mostramos `hashContent` (sha256 do report.json) como
 *     evidência de determinismo já no laudo.
 *   - **Não há `summary` / contagem de violations** no `ScanResponse`.
 *     Quem quiser os números abre o PDF ou o report.json. Pendência V1.1.
 *   - **`error` é livre-form (string)**. O backend ainda não emite
 *     `error_code` estruturado — a entidade `Scan.cs` deixa esse TODO
 *     explícito ("Apêndice E §E.9"). Mapeamos heurísticamente alguns
 *     prefixos conhecidos; o resto cai em "mensagem crua".
 *
 * Static export:
 *   - `output: "export"` exige `generateStaticParams` (em `layout.tsx`).
 *     Geramos placeholder porque `publicId` é DB-driven.
 *   - `useParams()` lê o id real no cliente; validamos UUID v4 antes de
 *     bater no backend para não desperdiçar round-trip em ids inválidos.
 */

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useEffect, useRef, useState } from "react";
import { Header } from "@/components/Header";
import { Footer } from "@/components/Footer";
import { SkipLink } from "@/components/SkipLink";
import { useAuth } from "@/lib/auth";
import {
  getScan,
  gitHubConnectStartUrl,
  laudoPdfUrl,
  parseApiError,
  reportJsonUrl,
  type ScanDetail,
  type ScanStatus,
} from "@/lib/dashboard-api";

// ── Constants ───────────────────────────────────────────────────────────────

const POLL_INTERVAL_MS = 2000;
// Teto de segurança: 30 min = 900 polls de 2s. Worker tem timeout próprio
// (15 min na V0); 30 min cobre fila + execução com folga e ainda nos
// protege contra polling infinito se o backend ficar mudo.
const POLL_TIMEOUT_MS = 30 * 60 * 1000;

const UUID_V4_REGEX =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

const TERMINAL_STATUSES: ReadonlySet<ScanStatus> = new Set([
  "completed",
  "failed",
  "cancelled",
]);

function isTerminal(status: ScanStatus): boolean {
  return TERMINAL_STATUSES.has(status);
}

// Replicado de `repos/[id]/page.tsx` por desenho — não promovendo a
// utility shared sem necessidade ainda. F5 e F6 usam, ponto. Se um
// terceiro consumidor aparecer (V1.1?), extrair.
const STATUS_META: Record<ScanStatus, { label: string; cls: string }> = {
  queued: {
    label: "na fila",
    cls: "text-neutral-700 bg-neutral-100 border-neutral-200",
  },
  running: {
    label: "rodando",
    cls: "text-blue-800 bg-blue-50 border-blue-200",
  },
  completed: {
    label: "ok",
    cls: "text-saint bg-saint-bg border-saint/30",
  },
  failed: {
    label: "erro",
    cls: "text-sinner bg-sinner-bg border-sinner/30",
  },
  cancelled: {
    label: "cancelado",
    cls: "text-neutral-600 bg-neutral-50 border-neutral-200",
  },
};

// ── Page shell ──────────────────────────────────────────────────────────────

export default function ScanDetailPage() {
  const router = useRouter();
  const { isLoading: authLoading, isAuthenticated, networkError } = useAuth();

  useEffect(() => {
    if (!authLoading && !isAuthenticated && !networkError) {
      router.replace("/login");
    }
  }, [authLoading, isAuthenticated, networkError, router]);

  // Read publicId from window.location.pathname. With output: "export"
  // + generateStaticParams(["_"]), useParams() returns the build-time
  // placeholder "_" forever — never the runtime URL segment. After the
  // ASP.NET SPA fallback rewrites /dashboard/scans/<uuid> to the
  // placeholder html, window.location is the only source of the real
  // segment. undefined = pre-hydration, null = invalid, string = ready.
  const [publicId, setPublicId] = useState<string | null | undefined>(undefined);
  useEffect(() => {
    const m = window.location.pathname.match(
      /^\/dashboard\/scans\/([^/]+)\/?$/,
    );
    if (!m) {
      setPublicId(null);
      return;
    }
    const raw = m[1];
    if (!UUID_V4_REGEX.test(raw)) {
      setPublicId(null);
      return;
    }
    setPublicId(raw.toLowerCase());
  }, []);

  return (
    <>
      <SkipLink />
      <Header />
      <main id="main" className="px-6 py-12 md:py-16">
        {authLoading && <PageSkeleton />}
        {!authLoading && networkError && <NetworkErrorState />}
        {!authLoading && !networkError && isAuthenticated && (
          publicId === undefined ? (
            <PageSkeleton />
          ) : publicId === null ? (
            <NotFoundState />
          ) : (
            <ScanDetailBody publicId={publicId} />
          )
        )}
      </main>
      <Footer />
    </>
  );
}

// ── Body (auth-gated) ───────────────────────────────────────────────────────

type LoadState =
  | { kind: "loading" }
  | { kind: "ready"; scan: ScanDetail }
  | { kind: "not_found" }
  | { kind: "error"; message: string };

function ScanDetailBody({ publicId }: { publicId: string }) {
  const [state, setState] = useState<LoadState>({ kind: "loading" });
  const [polling, setPolling] = useState(false);
  const [pollTimedOut, setPollTimedOut] = useState(false);

  // Todo o ciclo de polling vive dentro do `useEffect` abaixo. Refs
  // ficam só para o que precisa atravessar tick: o handle do timer
  // (para limpar no cleanup) e a flag de "ainda válido" (para a closure
  // descartar respostas tardias se o effect já refez).
  //
  // Padrão deliberadamente diferente do `/inspect` (que sobe os
  // helpers para o nível da função) porque aqui o polling depende de
  // `publicId` e da identidade do effect — fazer dependency injection
  // de `useCallback`s recursivos rendeu warning legítimo do
  // `react-hooks/exhaustive-deps`. Estrutura inline = uma fonte de
  // verdade.
  const pollTimerRef = useRef<number | null>(null);

  // Carregamento inicial + ciclo de polling até estado terminal.
  useEffect(() => {
    let cancelled = false;
    let pollStart = 0;

    setState({ kind: "loading" });
    setPolling(false);
    setPollTimedOut(false);

    function clearPollTimer() {
      if (pollTimerRef.current !== null) {
        window.clearTimeout(pollTimerRef.current);
        pollTimerRef.current = null;
      }
    }

    /**
     * Carrega um snapshot do scan. `firstLoad=true` é o load inicial
     * (cuida dos estados loading/error/not_found); `firstLoad=false` é
     * um tick de polling (não troca para erro em transientes — só
     * agenda próxima tentativa).
     *
     * Retorna `{ scan, halt }`. Se `halt`, o caller para o ciclo.
     */
    async function fetchOnce(
      firstLoad: boolean,
    ): Promise<{ scan: ScanDetail | null; halt: boolean }> {
      try {
        const res = await getScan(publicId);
        if (cancelled) return { scan: null, halt: true };

        if (res.status === 404) {
          if (firstLoad) {
            setState({ kind: "not_found" });
          }
          // 404 superveniente em meio-polling: terminal. (Repo pode ter
          // sido soft-deleted entre dois polls.)
          return { scan: null, halt: true };
        }
        if (!res.ok || !res.body) {
          if (firstLoad) {
            const err = parseApiError(res);
            setState({
              kind: "error",
              message:
                err?.message ?? "Não foi possível carregar este scan.",
            });
            return { scan: null, halt: true };
          }
          // Erro transiente em polling: re-agenda.
          return { scan: null, halt: false };
        }
        const scan = res.body;
        setState({ kind: "ready", scan });
        return { scan, halt: isTerminal(scan.status) };
      } catch {
        if (cancelled) return { scan: null, halt: true };
        if (firstLoad) {
          setState({
            kind: "error",
            message: "Falha de rede ao carregar este scan.",
          });
          return { scan: null, halt: true };
        }
        // Glitch de rede em polling: re-agenda.
        return { scan: null, halt: false };
      }
    }

    function scheduleNextPoll() {
      if (cancelled) return;
      pollTimerRef.current = window.setTimeout(() => {
        void pollOnce();
      }, POLL_INTERVAL_MS);
    }

    async function pollOnce() {
      if (cancelled) return;

      if (Date.now() - pollStart > POLL_TIMEOUT_MS) {
        clearPollTimer();
        setPolling(false);
        setPollTimedOut(true);
        return;
      }

      const { halt } = await fetchOnce(false);
      if (cancelled) return;
      if (halt) {
        clearPollTimer();
        setPolling(false);
        return;
      }
      scheduleNextPoll();
    }

    void (async () => {
      const { scan } = await fetchOnce(true);
      if (cancelled || !scan) return;
      if (!isTerminal(scan.status)) {
        pollStart = Date.now();
        setPolling(true);
        scheduleNextPoll();
      }
    })();

    // Cleanup global: cobre navegação, unmount e re-fire por `publicId`
    // mudando. Sem isso, o polling continua em background.
    return () => {
      cancelled = true;
      clearPollTimer();
    };
  }, [publicId]);

  if (state.kind === "loading") return <PageSkeleton />;
  if (state.kind === "not_found") return <NotFoundState />;
  if (state.kind === "error") {
    return <LoadErrorState message={state.message} />;
  }

  return (
    <ReadyView
      scan={state.scan}
      polling={polling}
      pollTimedOut={pollTimedOut}
    />
  );
}

// ── Ready: header + status-specific body ────────────────────────────────────

function ReadyView({
  scan,
  polling,
  pollTimedOut,
}: {
  scan: ScanDetail;
  polling: boolean;
  pollTimedOut: boolean;
}) {
  return (
    <div className="max-w-3xl mx-auto space-y-8">
      <ScanHeader scan={scan} polling={polling} />
      <StatusBlock scan={scan} />
      {pollTimedOut && <PollTimedOutBanner />}
    </div>
  );
}

function ScanHeader({ scan, polling }: { scan: ScanDetail; polling: boolean }) {
  const shortId = scan.publicId.slice(0, 8);
  const repoLabel = repoLabelFromUrl(scan.repo.githubUrl);
  const repoHref = `/dashboard/repos/${scan.repo.id}/`;

  return (
    <header>
      <p className="text-xs uppercase tracking-wider text-neutral-500">
        <Link
          href="/dashboard/repos/"
          className="hover:text-neutral-700 transition"
        >
          Repositórios
        </Link>{" "}
        <span aria-hidden="true">›</span>{" "}
        <Link
          href={repoHref}
          className="font-mono normal-case tracking-normal text-neutral-700 hover:text-ink transition"
        >
          {repoLabel}
        </Link>{" "}
        <span aria-hidden="true">›</span>{" "}
        <span className="font-mono normal-case tracking-normal text-neutral-700">
          Scan {shortId}
        </span>
      </p>

      <div className="mt-2 flex flex-wrap items-center gap-3">
        <h1 className="text-2xl md:text-3xl font-bold tracking-tight text-ink">
          Scan{" "}
          <span className="font-mono">{shortId}</span>
        </h1>
        <StatusPill status={scan.status} />
        {polling && <PollingIndicator />}
      </div>

      <p className="mt-3 text-xs text-neutral-500">
        Disparado{" "}
        <time
          dateTime={scan.queuedAt}
          title={formatAbsoluteDate(scan.queuedAt)}
        >
          {formatRelativeTime(scan.queuedAt)}
        </time>{" "}
        · canon{" "}
        <span className="font-mono">{scan.canonVersion || "—"}</span>
        {scan.ref ? (
          <>
            {" "}· ref <span className="font-mono">{scan.ref}</span>
          </>
        ) : null}
      </p>
      <p className="mt-1 text-xs text-neutral-500">
        Alvo:{" "}
        {scan.target ? (
          <span className="font-mono text-neutral-700 break-all">
            {scan.target}
          </span>
        ) : (
          <span className="italic">detectado automaticamente</span>
        )}
      </p>
      <p className="mt-1 text-xs text-neutral-500">
        Repositório:{" "}
        <a
          href={scan.repo.githubUrl}
          target="_blank"
          rel="noopener noreferrer"
          className="font-mono hover:text-ink underline decoration-neutral-300 underline-offset-2"
        >
          {repoLabel}
        </a>
      </p>
    </header>
  );
}

function StatusBlock({ scan }: { scan: ScanDetail }) {
  switch (scan.status) {
    case "queued":
      return <QueuedCard />;
    case "running":
      return <RunningCard />;
    case "completed":
      return <CompletedCard scan={scan} />;
    case "failed":
      return <FailedCard scan={scan} />;
    case "cancelled":
      return <CancelledCard scan={scan} />;
    default: {
      // Defesa contra evolução do enum no backend sem update aqui.
      const _exhaustive: never = scan.status;
      void _exhaustive;
      return null;
    }
  }
}

// ── Queued / Running cards ──────────────────────────────────────────────────

function QueuedCard() {
  return (
    <section
      aria-labelledby="status-title"
      className="bg-white border border-neutral-200 rounded-xl shadow-sm p-6 md:p-8"
    >
      <div className="flex items-start gap-3">
        <Spinner />
        <div>
          <h2
            id="status-title"
            className="text-lg font-semibold tracking-tight text-ink"
          >
            Aguardando worker...
          </h2>
          <p className="mt-2 text-sm text-neutral-700 leading-relaxed">
            Seu scan está na fila. Normalmente sai em segundos. Esta página
            atualiza sozinha.
          </p>
        </div>
      </div>
    </section>
  );
}

function RunningCard() {
  return (
    <section
      aria-labelledby="status-title"
      className="bg-white border border-neutral-200 rounded-xl shadow-sm p-6 md:p-8"
    >
      <div className="flex items-start gap-3">
        <Spinner />
        <div>
          <h2
            id="status-title"
            className="text-lg font-semibold tracking-tight text-ink"
          >
            Analisando o repositório...
          </h2>
          <p className="mt-2 text-sm text-neutral-700 leading-relaxed">
            Cloning, restore, análise Roslyn e renderização do PDF rodam
            aqui no servidor. Não há estágio granular exposto para o
            dashboard — o status volta para <strong>concluído</strong> ou{" "}
            <strong>erro</strong> quando o worker termina.
          </p>
          <p className="mt-2 text-xs text-neutral-500">
            Você pode fechar esta aba — quando voltar, o status estará
            atualizado.
          </p>
        </div>
      </div>
    </section>
  );
}

// ── Completed card ──────────────────────────────────────────────────────────

function CompletedCard({ scan }: { scan: ScanDetail }) {
  const repoHref = `/dashboard/repos/${scan.repo.id}/`;
  const completedAt = scan.completedAt;

  return (
    <section
      aria-labelledby="status-title"
      className="bg-saint-bg border border-saint/20 rounded-xl p-6 md:p-8"
    >
      <h2
        id="status-title"
        className="text-lg font-semibold tracking-tight text-saint"
      >
        Análise concluída.
      </h2>
      <p className="mt-2 text-sm text-neutral-700 leading-relaxed">
        O laudo está pronto.{" "}
        {completedAt && (
          <>
            Finalizado{" "}
            <time
              dateTime={completedAt}
              title={formatAbsoluteDate(completedAt)}
            >
              {formatRelativeTime(completedAt)}
            </time>
            .{" "}
          </>
        )}
        O mesmo commit produziria o mesmo PDF byte-a-byte.
      </p>

      {/*
        V1.1 PENDING — campos não expostos pelo backend hoje:
          • `grade` (A/B/C/D/F)
          • `summary` (contagem de violations por severidade)
          • `hardLocksOpen`
        Quando `ScanResponse` (engine/.../ScansEndpoints.cs) ganhar esses
        campos, adicionar bloco visual aqui. O usuário ainda obtém todos
        os números abrindo o PDF ou o report.json — então isto é UX, não
        bloqueio funcional.
      */}

      {scan.hashContent && (
        <p
          className="mt-4 text-xs text-neutral-600 break-all"
          title="Hash do report.json. O PDF gerado pela CLI local com o mesmo commit produz o mesmo hash."
        >
          <span className="font-mono">{scan.hashContent}</span>
        </p>
      )}

      <div className="mt-6 flex flex-wrap gap-3">
        <a
          href={laudoPdfUrl(scan.publicId)}
          download={`laudo-${scan.publicId.slice(0, 8)}.pdf`}
          className="inline-flex items-center gap-2 px-5 py-2.5 rounded-md bg-saint text-white text-sm font-semibold hover:bg-[#0c3d2e] transition"
        >
          <DownloadIcon />
          Baixar laudo PDF
        </a>
        <a
          href={reportJsonUrl(scan.publicId)}
          download={`report-${scan.publicId.slice(0, 8)}.json`}
          className="inline-flex items-center gap-2 px-5 py-2.5 rounded-md border border-neutral-300 bg-white text-neutral-800 text-sm font-medium hover:bg-neutral-50 transition"
        >
          <DownloadIcon />
          Baixar report.json
        </a>
      </div>

      <div className="mt-6 pt-4 border-t border-saint/20">
        <Link
          href={repoHref}
          className="inline-flex items-center gap-1.5 text-sm text-neutral-700 hover:text-ink transition"
        >
          <svg
            width="14"
            height="14"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            strokeWidth="2"
            strokeLinecap="round"
            strokeLinejoin="round"
            aria-hidden="true"
          >
            <path d="M15 18l-6-6 6-6" />
          </svg>
          Voltar ao repositório
        </Link>
      </div>
    </section>
  );
}

// ── Failed card ─────────────────────────────────────────────────────────────

interface MappedError {
  title: string;
  body: string;
  cta?: { kind: "github_connect" } | { kind: "back_to_repo" };
}

function mapErrorMessage(error: string | null): MappedError {
  if (!error || error.trim().length === 0) {
    return {
      title: "O motor de análise falhou.",
      body: "Suporte foi notificado. Tente novamente em alguns minutos.",
      cta: { kind: "back_to_repo" },
    };
  }

  const lower = error.toLowerCase();

  // Codes estruturados (Apêndice E §E.9 — backend ainda não emite, mas
  // já estamos prontos para quando emitir).
  if (error === "GITHUB_TOKEN_REVOKED" || lower.includes("github_token_revoked")) {
    return {
      title: "Token do GitHub foi revogado.",
      body:
        "Reconecte sua conta do GitHub para repositórios privados e tente o scan de novo.",
      cta: { kind: "github_connect" },
    };
  }
  if (lower.includes("clone_failed") || lower.includes("repo_unreachable")) {
    return {
      title: "Não foi possível clonar o repositório.",
      body:
        "Verifique se a URL ainda existe e se você tem acesso. Se o repo é privado, confirme que o GitHub está conectado.",
      cta: { kind: "back_to_repo" },
    };
  }
  if (lower.includes("engine_failed") || lower.includes("compile_failed")) {
    return {
      title: "O motor de análise falhou.",
      body:
        "O Roslyn não conseguiu compilar a solution. Tente disparar de novo ou abra um chamado se persistir.",
      cta: { kind: "back_to_repo" },
    };
  }
  if (lower.includes("timeout")) {
    return {
      title: "A análise excedeu o tempo limite.",
      body:
        "Para repos muito grandes, use o CLI local — sem limite de tempo e sem subir código para nosso servidor.",
      cta: { kind: "back_to_repo" },
    };
  }

  // Fallback: mostra a mensagem crua que veio do backend.
  return {
    title: "Análise falhou.",
    body: error,
    cta: { kind: "back_to_repo" },
  };
}

function FailedCard({ scan }: { scan: ScanDetail }) {
  const mapped = mapErrorMessage(scan.error);
  const repoHref = `/dashboard/repos/${scan.repo.id}/`;

  return (
    <section
      aria-labelledby="status-title"
      className="bg-sinner-bg border border-sinner/20 rounded-xl p-6 md:p-8"
    >
      <h2
        id="status-title"
        className="text-lg font-semibold tracking-tight text-sinner"
      >
        {mapped.title}
      </h2>
      <p className="mt-2 text-sm text-neutral-800 leading-relaxed whitespace-pre-line">
        {mapped.body}
      </p>

      <div className="mt-6 flex flex-wrap gap-3">
        {mapped.cta?.kind === "github_connect" ? (
          <a
            href={gitHubConnectStartUrl()}
            className="inline-flex items-center gap-2 px-4 py-2 rounded-md bg-ink text-white text-sm font-semibold hover:bg-neutral-800 transition"
          >
            Conectar GitHub
          </a>
        ) : null}
        <Link
          href={repoHref}
          className="inline-flex items-center gap-2 px-4 py-2 rounded-md bg-ink text-white text-sm font-semibold hover:bg-neutral-800 transition"
        >
          Tentar novamente
        </Link>
        <Link
          href="/dashboard/"
          className="inline-flex items-center gap-2 px-4 py-2 rounded-md border border-neutral-300 bg-white text-neutral-800 text-sm font-medium hover:bg-neutral-50 transition"
        >
          Voltar ao dashboard
        </Link>
      </div>
    </section>
  );
}

// ── Cancelled card ──────────────────────────────────────────────────────────

function CancelledCard({ scan }: { scan: ScanDetail }) {
  const repoHref = `/dashboard/repos/${scan.repo.id}/`;
  return (
    <section
      aria-labelledby="status-title"
      className="bg-white border border-neutral-200 rounded-xl shadow-sm p-6 md:p-8"
    >
      <h2
        id="status-title"
        className="text-lg font-semibold tracking-tight text-ink"
      >
        Análise cancelada.
      </h2>
      <p className="mt-2 text-sm text-neutral-700 leading-relaxed">
        Este scan foi cancelado antes de terminar. Você pode disparar um
        novo a qualquer momento na página do repositório.
      </p>
      <div className="mt-6">
        <Link
          href={repoHref}
          className="inline-flex items-center gap-1.5 text-sm text-neutral-700 hover:text-ink transition"
        >
          <svg
            width="14"
            height="14"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            strokeWidth="2"
            strokeLinecap="round"
            strokeLinejoin="round"
            aria-hidden="true"
          >
            <path d="M15 18l-6-6 6-6" />
          </svg>
          Voltar ao repositório
        </Link>
      </div>
    </section>
  );
}

// ── Polling indicator + timeout banner ──────────────────────────────────────

function PollingIndicator() {
  return (
    <span
      className="inline-flex items-center gap-1.5 text-[11px] text-neutral-500"
      aria-live="polite"
      title="Atualizando status a cada 2 segundos"
    >
      <span
        className="inline-block w-1.5 h-1.5 rounded-full bg-blue-500 animate-pulse"
        aria-hidden="true"
      />
      Atualizando...
    </span>
  );
}

function PollTimedOutBanner() {
  return (
    <div
      className="rounded-md border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-900"
      role="status"
    >
      <p className="font-medium">
        A análise demorou mais que o esperado.
      </p>
      <p className="mt-1 text-amber-800/90">
        Paramos de atualizar automaticamente para não consumir conexão à
        toa. Recarregue a página para verificar o status mais recente.
      </p>
    </div>
  );
}

function Spinner() {
  return (
    <span
      className="inline-flex shrink-0 mt-0.5"
      role="status"
      aria-label="Carregando"
    >
      <svg
        className="animate-spin h-5 w-5 text-saint"
        viewBox="0 0 24 24"
        fill="none"
        aria-hidden="true"
      >
        <circle
          className="opacity-25"
          cx="12"
          cy="12"
          r="10"
          stroke="currentColor"
          strokeWidth="4"
        />
        <path
          className="opacity-75"
          fill="currentColor"
          d="M4 12a8 8 0 018-8v4a4 4 0 00-4 4H4z"
        />
      </svg>
    </span>
  );
}

function DownloadIcon() {
  return (
    <svg
      width="16"
      height="16"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="2"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
    >
      <path d="M21 15v4a2 2 0 01-2 2H5a2 2 0 01-2-2v-4" />
      <polyline points="7 10 12 15 17 10" />
      <line x1="12" y1="15" x2="12" y2="3" />
    </svg>
  );
}

function StatusPill({ status }: { status: ScanStatus }) {
  const meta = STATUS_META[status];
  return (
    <span
      className={`inline-flex items-center text-[10px] font-semibold uppercase tracking-wider rounded px-1.5 py-0.5 border ${meta.cls}`}
    >
      {meta.label}
    </span>
  );
}

// ── Generic states ──────────────────────────────────────────────────────────

function PageSkeleton() {
  return (
    <section className="max-w-3xl mx-auto space-y-6">
      <div className="lt-skeleton h-4 w-48" />
      <div className="bg-white border border-neutral-200 rounded-xl p-8 shadow-sm">
        <div className="lt-skeleton h-7 w-1/2 mb-4" />
        <div className="lt-skeleton h-4 w-2/3 mb-2" />
        <div className="lt-skeleton h-4 w-1/3" />
      </div>
      <div className="bg-white border border-neutral-200 rounded-xl p-6 shadow-sm">
        <div className="lt-skeleton h-5 w-40 mb-4" />
        <div className="lt-skeleton h-4 w-full mb-2" />
        <div className="lt-skeleton h-9 w-32 mt-4" />
      </div>
    </section>
  );
}

function NetworkErrorState() {
  return (
    <section className="max-w-3xl mx-auto">
      <div className="bg-sinner-bg border border-sinner/20 rounded-xl p-6 md:p-8">
        <h1 className="text-2xl font-bold tracking-tight text-sinner">
          Backend indisponível
        </h1>
        <p className="mt-3 text-neutral-700 text-sm">
          Não conseguimos contatar o servidor para verificar sua sessão.
        </p>
        <div className="mt-6 flex flex-wrap gap-3">
          <button
            type="button"
            onClick={() => window.location.reload()}
            className="inline-flex items-center gap-2 px-4 py-2 rounded-md bg-ink text-white text-sm font-semibold hover:bg-neutral-800 transition"
          >
            Tentar novamente
          </button>
          <Link
            href="/dashboard/"
            className="inline-flex items-center gap-2 px-4 py-2 rounded-md border border-neutral-300 text-neutral-800 text-sm font-medium hover:bg-neutral-100 transition"
          >
            Voltar ao dashboard
          </Link>
        </div>
      </div>
    </section>
  );
}

function NotFoundState() {
  return (
    <section className="max-w-3xl mx-auto">
      <div className="bg-white border border-neutral-200 rounded-xl p-8 md:p-10 text-center">
        <h1 className="text-xl font-bold tracking-tight text-ink">
          Scan não encontrado
        </h1>
        <p className="mt-3 text-sm text-neutral-700 max-w-md mx-auto">
          Este scan não existe ou já foi removido. Volte ao dashboard
          para ver os repositórios e seus scans recentes.
        </p>
        <Link
          href="/dashboard/"
          className="mt-6 inline-flex items-center gap-2 px-4 py-2 rounded-md bg-ink text-white text-sm font-semibold hover:bg-neutral-800 transition"
        >
          Voltar ao dashboard
        </Link>
      </div>
    </section>
  );
}

function LoadErrorState({ message }: { message: string }) {
  return (
    <section className="max-w-3xl mx-auto">
      <div
        className="bg-sinner-bg border border-sinner/20 rounded-xl p-6"
        role="alert"
        aria-live="polite"
      >
        <p className="text-sinner font-semibold">
          Não foi possível carregar este scan
        </p>
        <p className="mt-2 text-sm text-neutral-700">{message}</p>
        <div className="mt-4 flex flex-wrap gap-3">
          <button
            type="button"
            onClick={() => window.location.reload()}
            className="inline-flex items-center gap-2 px-4 py-2 rounded-md bg-ink text-white text-sm font-semibold hover:bg-neutral-800 transition"
          >
            Tentar novamente
          </button>
          <Link
            href="/dashboard/"
            className="inline-flex items-center gap-2 px-4 py-2 rounded-md border border-neutral-300 text-neutral-800 text-sm font-medium hover:bg-neutral-100 transition"
          >
            Voltar ao dashboard
          </Link>
        </div>
      </div>
    </section>
  );
}

// ── Helpers ─────────────────────────────────────────────────────────────────

function repoLabelFromUrl(url: string): string {
  try {
    const u = new URL(url);
    const parts = u.pathname.split("/").filter(Boolean);
    if (parts.length >= 2) {
      return `${parts[0]}/${parts[1].replace(/\.git$/, "")}`;
    }
    return url;
  } catch {
    return url;
  }
}

function formatAbsoluteDate(iso: string): string {
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return iso;
  return d.toLocaleString("pt-BR", {
    day: "2-digit",
    month: "short",
    year: "numeric",
    hour: "2-digit",
    minute: "2-digit",
  });
}

/**
 * "há X minutos" estilo PT-BR sem dependência externa. Mesma função do
 * F5 — duplicada propositalmente; promover a util shared só quando um
 * terceiro consumidor pedir (cf. comentário sobre `STATUS_META`).
 */
function formatRelativeTime(iso: string): string {
  const t = new Date(iso).getTime();
  if (!Number.isFinite(t)) return iso;
  const diffMs = Date.now() - t;
  if (diffMs < 0) return "agora mesmo";

  const sec = Math.floor(diffMs / 1000);
  if (sec < 45) return "há instantes";
  const min = Math.floor(sec / 60);
  if (min < 60) return min === 1 ? "há 1 minuto" : `há ${min} minutos`;
  const hr = Math.floor(min / 60);
  if (hr < 24) return hr === 1 ? "há 1 hora" : `há ${hr} horas`;
  const day = Math.floor(hr / 24);
  if (day < 30) return day === 1 ? "há 1 dia" : `há ${day} dias`;
  return formatAbsoluteDate(iso);
}
