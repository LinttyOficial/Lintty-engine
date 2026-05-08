"use client";

/**
 * Detalhe do repositório (`/dashboard/repos/[id]`). Sprint 3 / PR F5.
 *
 * Três responsabilidades:
 *   1. Carregar `getRepo(id)` + `listRepoScans(id)` em paralelo. Loading,
 *      network error e 404 reusam o mesmo padrão visual de
 *      `app/dashboard/page.tsx`.
 *   2. Disparar um scan via `triggerScan({ repoId })`. Quando o repo é
 *      privado, o botão fica trancado até o usuário conectar GitHub —
 *      idêntico ao `useGitHubConnect()` do badge global. O sucesso do
 *      trigger navega para `/dashboard/scans/{publicId}` (página de F6).
 *   3. Listar o histórico de scans (sem polling — F6 cuida do live).
 *
 * Tenant isolation:
 *   - `getRepo` 404 ⇒ "Repositório não encontrado". Mesma mensagem para
 *     "não existe" e "existe em outra org" (§3.5 do ADR 0007). Nunca
 *     mostramos ID nem confirmamos existência cross-tenant.
 *
 * Static export:
 *   - `output: "export"` exige `generateStaticParams` para segmentos
 *     dinâmicos. Geramos um placeholder porque os ids reais são DB-driven
 *     e não conhecidos em build time. `useParams()` lê o id real no
 *     cliente; o `Number(...)` valida e cai em "not found" se quiserem
 *     forçar uma rota inválida.
 *   - Quando hospedado por trás do ASP.NET (em vez de Cloudflare Pages),
 *     o servidor precisa fazer fallback de `/dashboard/repos/<n>/` para
 *     o html gerado. Issue documentada no PR — não é bloqueio para `next
 *     dev` nem para Cloudflare.
 */

import Link from "next/link";
import { useParams, useRouter } from "next/navigation";
import { useCallback, useEffect, useMemo, useState } from "react";
import { Header } from "@/components/Header";
import { Footer } from "@/components/Footer";
import { SkipLink } from "@/components/SkipLink";
import { useAuth } from "@/lib/auth";
import {
  GitHubConnectProvider,
  useGitHubConnect,
} from "@/lib/github-connect";
import {
  getRepo,
  gitHubConnectStartUrl,
  listRepoScans,
  parseApiError,
  triggerScan,
  type RepoDetail,
  type ScanStatus,
  type ScanSummary,
} from "@/lib/dashboard-api";

// Canon snapshotada pelo backend no momento do trigger (§3.7 do ADR
// 0007). Hardcode porque o V0 só tem uma — `DefaultCanonVersionProvider`
// devolve "1.0.0". Quando o backend expor canon dinâmica via endpoint
// próprio (V1.1), trocar por fetch.
const CANON_VERSION_DISPLAY = "1.0.0";

// ── Page shell ──────────────────────────────────────────────────────────────

export default function RepoDetailPage() {
  const router = useRouter();
  const params = useParams<{ id: string }>();
  const { isLoading: authLoading, isAuthenticated, networkError } = useAuth();

  useEffect(() => {
    if (!authLoading && !isAuthenticated && !networkError) {
      router.replace("/login");
    }
  }, [authLoading, isAuthenticated, networkError, router]);

  // Parse early so we render the "not found" branch for non-numeric ids
  // (the static-export placeholder, malformed hand-typed urls, etc.)
  // without a backend round-trip.
  const repoId = useMemo(() => {
    const raw = params?.id;
    if (typeof raw !== "string") return null;
    const n = Number.parseInt(raw, 10);
    if (!Number.isFinite(n) || n <= 0 || String(n) !== raw) return null;
    return n;
  }, [params]);

  return (
    <>
      <SkipLink />
      <Header />
      <main id="main" className="px-6 py-12 md:py-16">
        {authLoading && <PageSkeleton />}
        {!authLoading && networkError && <NetworkErrorState />}
        {!authLoading && !networkError && isAuthenticated && (
          <GitHubConnectProvider>
            {repoId === null ? (
              <NotFoundState />
            ) : (
              <RepoDetailBody repoId={repoId} />
            )}
          </GitHubConnectProvider>
        )}
      </main>
      <Footer />
    </>
  );
}

// ── Body (auth-gated) ───────────────────────────────────────────────────────

type LoadState =
  | { kind: "loading" }
  | { kind: "ready"; repo: RepoDetail; scans: ScanSummary[] }
  | { kind: "not_found" }
  | { kind: "error"; message: string };

function RepoDetailBody({ repoId }: { repoId: number }) {
  const [state, setState] = useState<LoadState>({ kind: "loading" });

  const load = useCallback(async () => {
    setState({ kind: "loading" });
    try {
      const [repoRes, scansRes] = await Promise.all([
        getRepo(repoId),
        listRepoScans(repoId),
      ]);

      if (repoRes.status === 404) {
        setState({ kind: "not_found" });
        return;
      }
      if (!repoRes.ok || !repoRes.body) {
        const err = parseApiError(repoRes);
        setState({
          kind: "error",
          message: err?.message ?? "Não foi possível carregar este repositório.",
        });
        return;
      }

      // Scans 404 cai junto com o repo. Outros erros não-fatais: lista
      // vazia para não bloquear o dispatch.
      let scans: ScanSummary[] = [];
      if (scansRes.ok && Array.isArray(scansRes.body)) {
        scans = scansRes.body;
      }

      setState({ kind: "ready", repo: repoRes.body, scans });
    } catch {
      setState({
        kind: "error",
        message: "Falha de rede ao carregar este repositório.",
      });
    }
  }, [repoId]);

  useEffect(() => {
    void load();
  }, [load]);

  if (state.kind === "loading") return <PageSkeleton />;
  if (state.kind === "not_found") return <NotFoundState />;
  if (state.kind === "error") {
    return <LoadErrorState message={state.message} onRetry={load} />;
  }

  return (
    <ReadyView
      repo={state.repo}
      scans={state.scans}
      onScansChanged={() => void load()}
    />
  );
}

// ── Ready: header + trigger + history ───────────────────────────────────────

function ReadyView({
  repo,
  scans,
  onScansChanged,
}: {
  repo: RepoDetail;
  scans: ScanSummary[];
  onScansChanged: () => void;
}) {
  const label = repoLabelFromUrl(repo.githubUrl);

  return (
    <div className="max-w-3xl mx-auto space-y-8">
      <BackLink />
      <RepoHeader repo={repo} label={label} />
      <TriggerCard repo={repo} onTriggered={onScansChanged} />
      <HistorySection scans={scans} />
    </div>
  );
}

function BackLink() {
  return (
    <Link
      href="/dashboard"
      className="inline-flex items-center gap-1.5 text-sm text-neutral-600 hover:text-ink transition"
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
      Voltar ao dashboard
    </Link>
  );
}

function RepoHeader({ repo, label }: { repo: RepoDetail; label: string }) {
  return (
    <header>
      <p className="text-xs uppercase tracking-wider text-neutral-500">
        Repositórios <span aria-hidden="true">›</span>{" "}
        <span className="font-mono normal-case tracking-normal text-neutral-700">
          {label}
        </span>
      </p>
      <h1 className="mt-2 text-2xl md:text-3xl font-bold tracking-tight text-ink break-all">
        {label}
      </h1>
      <div className="mt-3 flex flex-wrap items-center gap-2">
        <a
          href={repo.githubUrl}
          target="_blank"
          rel="noopener noreferrer"
          className="text-sm text-neutral-600 hover:text-ink underline decoration-neutral-300 underline-offset-2 break-all"
        >
          {repo.githubUrl}
        </a>
        {repo.isPrivate && (
          <span
            className="inline-flex items-center text-[10px] font-semibold uppercase tracking-wider text-neutral-700 bg-neutral-100 border border-neutral-200 rounded px-1.5 py-0.5"
            title="Repositório privado"
          >
            privado
          </span>
        )}
      </div>
      <p className="mt-3 text-xs text-neutral-500">
        Adicionado em {formatAbsoluteDate(repo.createdAt)} por{" "}
        {repo.addedBy.displayName}
      </p>
    </header>
  );
}

// ── Trigger card ────────────────────────────────────────────────────────────

type TriggerState =
  | { kind: "idle" }
  | { kind: "submitting" }
  | { kind: "error"; message: string };

function TriggerCard({
  repo,
  onTriggered,
}: {
  repo: RepoDetail;
  onTriggered: () => void;
}) {
  const router = useRouter();
  const { status: ghStatus, isLoading: ghLoading } = useGitHubConnect();
  const [state, setState] = useState<TriggerState>({ kind: "idle" });

  // Gate: privados exigem GitHub conectado. Public repos podem rodar
  // sem token (mesma regra do AddRepoModal manual).
  const needsGithub = repo.isPrivate && ghStatus?.connected !== true;
  const githubChecking = repo.isPrivate && ghLoading;
  const submitting = state.kind === "submitting";
  const disabled = submitting || needsGithub || githubChecking;

  async function handleTrigger() {
    if (disabled) return;
    setState({ kind: "submitting" });
    try {
      const res = await triggerScan({ repoId: repo.id });
      if (res.status === 201 && res.body) {
        // Histórico será re-carregado quando o usuário voltar; e a
        // navegação leva para a página de live polling (F6).
        onTriggered();
        router.push(`/dashboard/scans/${res.body.publicId}/`);
        return;
      }
      if (res.status === 404) {
        setState({
          kind: "error",
          message:
            "Este repositório não está mais disponível. Volte ao dashboard.",
        });
        return;
      }
      const err = parseApiError(res);
      setState({
        kind: "error",
        message:
          err?.message ?? `Erro ${res.status} ao disparar o scan. Tente novamente.`,
      });
    } catch {
      setState({
        kind: "error",
        message: "Falha de rede ao disparar o scan. Tente novamente.",
      });
    }
  }

  return (
    <section
      aria-labelledby="analyze-title"
      className="bg-white border border-neutral-200 rounded-xl shadow-sm p-6 md:p-8"
    >
      <h2
        id="analyze-title"
        className="text-lg font-semibold tracking-tight text-ink"
      >
        Analisar repositório
      </h2>
      <p className="mt-2 text-sm text-neutral-700 leading-relaxed">
        O Lintty vai clonar o repositório, rodar o motor Roslyn e gerar um
        laudo PDF determinístico. O mesmo commit produz o mesmo PDF
        byte-a-byte.
      </p>
      <p className="mt-3 text-xs text-neutral-500">
        Canon: <span className="font-mono">{CANON_VERSION_DISPLAY}</span> ·
        snapshotada no momento do trigger.
      </p>

      {needsGithub && !githubChecking && (
        <div
          className="mt-4 rounded-md border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-900"
          role="status"
        >
          <p className="font-medium">
            Conecte o GitHub para analisar repositórios privados.
          </p>
          <p className="mt-1 text-amber-800/90">
            Privados exigem um token OAuth do usuário com escopo{" "}
            <code className="font-mono">repo</code>. Conexão pública (PAT)
            não basta.
          </p>
          <a
            href={gitHubConnectStartUrl()}
            className="mt-3 inline-flex items-center gap-2 px-4 py-2 rounded-md bg-ink text-white text-sm font-semibold hover:bg-neutral-800 transition"
          >
            Conectar GitHub
          </a>
        </div>
      )}

      {state.kind === "error" && (
        <p
          className="mt-4 text-sm text-sinner"
          role="alert"
          aria-live="polite"
        >
          {state.message}
        </p>
      )}

      <div className="mt-5">
        <button
          type="button"
          onClick={handleTrigger}
          disabled={disabled}
          className="inline-flex items-center gap-2 px-5 py-2.5 rounded-md bg-saint text-white text-sm font-semibold hover:bg-[#0c3d2e] transition disabled:opacity-50 disabled:cursor-not-allowed"
        >
          {submitting ? "Disparando..." : "Analisar agora"}
        </button>
      </div>
    </section>
  );
}

// ── History ─────────────────────────────────────────────────────────────────

function HistorySection({ scans }: { scans: ScanSummary[] }) {
  const total = scans.length;

  return (
    <section aria-labelledby="history-title">
      <div className="flex items-baseline justify-between">
        <h2
          id="history-title"
          className="text-lg font-semibold tracking-tight text-ink"
        >
          Histórico
        </h2>
        <span className="text-xs text-neutral-500">
          {total === 0
            ? "nenhum scan ainda"
            : total === 1
              ? "1 scan"
              : `${total} scans`}
        </span>
      </div>

      {total === 0 ? (
        <div className="mt-4 bg-white border border-dashed border-neutral-300 rounded-xl p-8 text-center">
          <p className="text-sm text-neutral-600">
            Ainda não há scans deste repositório. Clique em{" "}
            <strong className="text-ink">Analisar agora</strong> acima para
            começar.
          </p>
        </div>
      ) : (
        <ul className="mt-4 space-y-2" aria-label="Histórico de scans">
          {scans.map((scan) => (
            <li key={scan.publicId}>
              <ScanRow scan={scan} />
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}

function ScanRow({ scan }: { scan: ScanSummary }) {
  const shortId = scan.publicId.slice(0, 8);
  const startTimestamp = scan.startedAt ?? scan.queuedAt;

  return (
    <Link
      href={`/dashboard/scans/${scan.publicId}/`}
      className="block bg-white border border-neutral-200 rounded-xl p-4 hover:border-neutral-300 transition"
    >
      <div className="flex items-center justify-between gap-4">
        <div className="min-w-0 flex-1">
          <div className="flex items-center gap-2">
            <code className="text-xs font-mono text-neutral-700">
              {shortId}
            </code>
            <StatusPill status={scan.status} />
          </div>
          <p className="mt-1 text-xs text-neutral-500">
            {scan.startedAt ? "Iniciado" : "Enfileirado"}{" "}
            <time
              dateTime={startTimestamp}
              title={formatAbsoluteDate(startTimestamp)}
            >
              {formatRelativeTime(startTimestamp)}
            </time>
            {scan.canonVersion && (
              <>
                {" "}
                · canon{" "}
                <span className="font-mono">{scan.canonVersion}</span>
              </>
            )}
          </p>
        </div>
        <span className="text-xs text-neutral-500 shrink-0">Ver →</span>
      </div>
    </Link>
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

// ── Generic states ──────────────────────────────────────────────────────────

function PageSkeleton() {
  return (
    <section className="max-w-3xl mx-auto space-y-6">
      <div className="lt-skeleton h-4 w-32" />
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
            href="/dashboard"
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
          Repositório não encontrado
        </h1>
        <p className="mt-3 text-sm text-neutral-700 max-w-md mx-auto">
          Este repositório não existe ou já foi removido. Volte ao
          dashboard e adicione-o de novo se necessário.
        </p>
        <Link
          href="/dashboard"
          className="mt-6 inline-flex items-center gap-2 px-4 py-2 rounded-md bg-ink text-white text-sm font-semibold hover:bg-neutral-800 transition"
        >
          Voltar ao dashboard
        </Link>
      </div>
    </section>
  );
}

function LoadErrorState({
  message,
  onRetry,
}: {
  message: string;
  onRetry: () => void;
}) {
  return (
    <section className="max-w-3xl mx-auto">
      <div
        className="bg-sinner-bg border border-sinner/20 rounded-xl p-6"
        role="alert"
        aria-live="polite"
      >
        <p className="text-sinner font-semibold">
          Não foi possível carregar este repositório
        </p>
        <p className="mt-2 text-sm text-neutral-700">{message}</p>
        <div className="mt-4 flex flex-wrap gap-3">
          <button
            type="button"
            onClick={onRetry}
            className="inline-flex items-center gap-2 px-4 py-2 rounded-md bg-ink text-white text-sm font-semibold hover:bg-neutral-800 transition"
          >
            Tentar novamente
          </button>
          <Link
            href="/dashboard"
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
 * "há X minutos" estilo PT-BR sem dependência externa. Cobre os 5
 * intervalos que importam para um histórico de scans (segundos a meses);
 * acima de 30 dias caímos para uma data absoluta porque "há 4 meses" já
 * não é informativo.
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
