"use client";

/**
 * Detalhe do repositório (`/dashboard/repos/[id]`). Sprint 3 / PR F5 +
 * S1f (preflight + scan-target picker).
 *
 * Três responsabilidades:
 *   1. Carregar `getRepo(id)` + `listRepoScans(id)` + `getRepoPreflight(id)`
 *      em paralelo. Loading, network error e 404 reusam o mesmo padrão
 *      visual de `app/dashboard/page.tsx`. Falha não-fatal do preflight
 *      (rede ou 5xx) não bloqueia o resto — o trigger continua disponível
 *      e o engine vai falhar coerentemente se a config for ruim.
 *   2. Renderizar um card adaptativo (`<TargetCard />`) por
 *      `preflight.status`:
 *        - `ready`            → Trigger card com bloco "Alvo:" + toggle
 *                              "Trocar alvo" que reaproveita o picker.
 *        - `needs_config`     → Picker em destaque com "Salvar e analisar
 *                              agora" (PUT + trigger num click) e "Salvar
 *                              configuração" (só PUT).
 *        - `no_dotnet_project` → Mensagem soft + "Recarregar".
 *   3. Listar o histórico de scans (sem polling — F6 cuida do live).
 *
 * Tenant isolation:
 *   - 404 do `getRepo` e do `setRepoScanTarget` mapeiam para a mesma
 *     mensagem ("não encontrado"). Nunca confirmamos existência cross-org.
 */

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useCallback, useEffect, useState } from "react";
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
  getRepoPreflight,
  gitHubConnectStartUrl,
  listRepoScans,
  parseApiError,
  setRepoScanTarget,
  triggerScan,
  type PreflightAutoDetected,
  type PreflightAutoDetectedKind,
  type PreflightCandidateKind,
  type PreflightResult,
  type RepoDetail,
  type ScanStatus,
  type ScanSummary,
} from "@/lib/dashboard-api";
import {
  ScanTargetPicker,
  describeCandidateKind,
} from "@/app/dashboard/components/ScanTargetPicker";

// Canon snapshotada pelo backend no momento do trigger (§3.7 do ADR
// 0007). Hardcode porque o V0 só tem uma — `DefaultCanonVersionProvider`
// devolve "1.0.0". Quando o backend expor canon dinâmica via endpoint
// próprio (V1.1), trocar por fetch.
const CANON_VERSION_DISPLAY = "1.0.0";

// ── Page shell ──────────────────────────────────────────────────────────────

export default function RepoDetailPage() {
  const router = useRouter();
  const { isLoading: authLoading, isAuthenticated, networkError } = useAuth();

  useEffect(() => {
    if (!authLoading && !isAuthenticated && !networkError) {
      router.replace("/login");
    }
  }, [authLoading, isAuthenticated, networkError, router]);

  // Read the id from window.location.pathname instead of useParams().
  // With output: "export" + generateStaticParams(["_"]), Next renders
  // a single placeholder HTML and useParams() returns the build-time
  // "_" — never the runtime URL segment. window.location is the only
  // reactive source of the actual id after the SPA fallback rewrite.
  // undefined = pre-hydration, null = invalid id, number = ready.
  const [repoId, setRepoId] = useState<number | null | undefined>(undefined);
  useEffect(() => {
    const m = window.location.pathname.match(
      /^\/dashboard\/repos\/([^/]+)\/?$/,
    );
    if (!m) {
      setRepoId(null);
      return;
    }
    const raw = m[1];
    const n = Number.parseInt(raw, 10);
    if (!Number.isFinite(n) || n <= 0 || String(n) !== raw) {
      setRepoId(null);
      return;
    }
    setRepoId(n);
  }, []);

  return (
    <>
      <SkipLink />
      <Header />
      <main id="main" className="lt-dark-glow lt-noise px-6 py-12 md:py-16 text-paper">
        {authLoading && <PageSkeleton />}
        {!authLoading && networkError && <NetworkErrorState />}
        {!authLoading && !networkError && isAuthenticated && (
          <GitHubConnectProvider>
            {repoId === undefined ? (
              <PageSkeleton />
            ) : repoId === null ? (
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
  | {
      kind: "ready";
      repo: RepoDetail;
      scans: ScanSummary[];
      preflight: PreflightResult | null;
    }
  | { kind: "not_found" }
  | { kind: "error"; message: string };

function RepoDetailBody({ repoId }: { repoId: number }) {
  const [state, setState] = useState<LoadState>({ kind: "loading" });

  const load = useCallback(async () => {
    setState({ kind: "loading" });
    try {
      const [repoRes, scansRes, preflightRes] = await Promise.all([
        getRepo(repoId),
        listRepoScans(repoId),
        getRepoPreflight(repoId),
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

      // Preflight é não-fatal: se a discovery falhou (502 transient,
      // network), seguimos sem o card de configuração — o trigger
      // continua disponível e o engine retorna erro coerente se a
      // config for ruim.
      const preflight =
        preflightRes.ok && preflightRes.body ? preflightRes.body : null;

      setState({ kind: "ready", repo: repoRes.body, scans, preflight });
    } catch {
      setState({
        kind: "error",
        message: "Falha de rede ao carregar este repositório.",
      });
    }
  }, [repoId]);

  // Refresh apenas do preflight, sem mexer em scans/repo.
  const refreshPreflight = useCallback(async () => {
    try {
      const res = await getRepoPreflight(repoId);
      const next = res.ok && res.body ? res.body : null;
      setState((prev) =>
        prev.kind === "ready" ? { ...prev, preflight: next } : prev,
      );
    } catch {
      // Mantém o preflight anterior; um erro transient não deve
      // limpar a UI configurada.
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
      preflight={state.preflight}
      onScansChanged={() => void load()}
      onPreflightChanged={() => void refreshPreflight()}
    />
  );
}

// ── Ready: header + adaptive card + history ─────────────────────────────────

function ReadyView({
  repo,
  scans,
  preflight,
  onScansChanged,
  onPreflightChanged,
}: {
  repo: RepoDetail;
  scans: ScanSummary[];
  preflight: PreflightResult | null;
  onScansChanged: () => void;
  onPreflightChanged: () => void;
}) {
  const label = repoLabelFromUrl(repo.githubUrl);

  return (
    <div className="max-w-3xl mx-auto space-y-8">
      <BackLink />
      <RepoHeader repo={repo} label={label} />
      <TargetCard
        repo={repo}
        preflight={preflight}
        onTriggered={onScansChanged}
        onPreflightChanged={onPreflightChanged}
      />
      <HistorySection scans={scans} />
    </div>
  );
}

function BackLink() {
  return (
    <Link
      href="/dashboard"
      className="inline-flex items-center gap-1.5 text-sm text-neutral-400 hover:text-paper transition"
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
        <span className="font-mono normal-case tracking-normal text-neutral-300">
          {label}
        </span>
      </p>
      <h1 className="mt-2 text-2xl md:text-3xl font-bold tracking-tight text-paper break-all">
        {label}
      </h1>
      <div className="mt-3 flex flex-wrap items-center gap-2">
        <a
          href={repo.githubUrl}
          target="_blank"
          rel="noopener noreferrer"
          className="text-sm text-neutral-400 hover:text-paper underline decoration-emerald-400/40 underline-offset-2 break-all"
        >
          {repo.githubUrl}
        </a>
        {repo.isPrivate && (
          <span
            className="inline-flex items-center text-[10px] font-semibold uppercase tracking-wider text-neutral-300 bg-white/5 border border-neutral-700/60 rounded px-1.5 py-0.5"
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

// ── Adaptive target card ────────────────────────────────────────────────────

/**
 * Branches on `preflight.status`. When the preflight call failed
 * altogether (`preflight === null`), we fall back to the legacy trigger
 * card — better than blocking the page on a transient discovery hiccup.
 */
function TargetCard({
  repo,
  preflight,
  onTriggered,
  onPreflightChanged,
}: {
  repo: RepoDetail;
  preflight: PreflightResult | null;
  onTriggered: () => void;
  onPreflightChanged: () => void;
}) {
  if (preflight === null) {
    // Discovery failed — degrade to the basic trigger card.
    return <TriggerCard repo={repo} onTriggered={onTriggered} />;
  }

  if (preflight.status === "no_dotnet_project") {
    return (
      <NoDotnetCard
        reason={preflight.reason}
        onReload={onPreflightChanged}
      />
    );
  }

  if (preflight.status === "needs_config") {
    return (
      <NeedsConfigCard
        repo={repo}
        preflight={preflight}
        onTriggered={onTriggered}
        onPreflightChanged={onPreflightChanged}
      />
    );
  }

  return (
    <ReadyCard
      repo={repo}
      preflight={preflight}
      onTriggered={onTriggered}
      onPreflightChanged={onPreflightChanged}
    />
  );
}

// ── Card: ready ─────────────────────────────────────────────────────────────

type TriggerState =
  | { kind: "idle" }
  | { kind: "submitting" }
  | { kind: "error"; message: string };

function ReadyCard({
  repo,
  preflight,
  onTriggered,
  onPreflightChanged,
}: {
  repo: RepoDetail;
  preflight: PreflightResult;
  onTriggered: () => void;
  onPreflightChanged: () => void;
}) {
  const router = useRouter();
  const { status: ghStatus, isLoading: ghLoading } = useGitHubConnect();
  const [trigger, setTrigger] = useState<TriggerState>({ kind: "idle" });
  const [editing, setEditing] = useState(false);

  const needsGithub = repo.isPrivate && ghStatus?.connected !== true;
  const githubChecking = repo.isPrivate && ghLoading;
  const submitting = trigger.kind === "submitting";
  const disabled = submitting || needsGithub || githubChecking;

  async function handleTrigger() {
    if (disabled) return;
    setTrigger({ kind: "submitting" });
    try {
      const res = await triggerScan({ repoId: repo.id });
      if (res.status === 201 && res.body) {
        onTriggered();
        router.push(`/dashboard/scans/${res.body.publicId}/`);
        return;
      }
      if (res.status === 404) {
        setTrigger({
          kind: "error",
          message:
            "Este repositório não está mais disponível. Volte ao dashboard.",
        });
        return;
      }
      const err = parseApiError(res);
      setTrigger({
        kind: "error",
        message:
          err?.message ?? `Erro ${res.status} ao disparar o scan. Tente novamente.`,
      });
    } catch {
      setTrigger({
        kind: "error",
        message: "Falha de rede ao disparar o scan. Tente novamente.",
      });
    }
  }

  const ctaLabel = submitting ? "Disparando..." : "Analisar agora";

  return (
    <section
      aria-labelledby="analyze-title"
      className="lt-card-form p-6 md:p-8"
    >
      <h2
        id="analyze-title"
        className="text-lg font-semibold tracking-tight text-paper"
      >
        Analisar repositório
      </h2>
      <p className="mt-2 text-sm text-neutral-300 leading-relaxed">
        O Lintty vai clonar o repositório, rodar o motor Roslyn e gerar
        um laudo PDF determinístico. O mesmo commit produz o mesmo PDF
        byte-a-byte.
      </p>
      <p className="mt-3 text-xs text-neutral-500">
        Canon: <span className="font-mono">{CANON_VERSION_DISPLAY}</span> ·
        snapshotada no momento do trigger.
      </p>

      <TargetSummary preflight={preflight} />

      {!editing ? (
        <button
          type="button"
          onClick={() => setEditing(true)}
          className="mt-3 text-sm text-neutral-400 hover:text-paper underline decoration-emerald-400/40 underline-offset-2 transition"
        >
          Trocar alvo
        </button>
      ) : (
        <ChangeTargetPanel
          repoId={repo.id}
          preflight={preflight}
          onClose={() => setEditing(false)}
          onSaved={() => {
            setEditing(false);
            onPreflightChanged();
          }}
        />
      )}

      {needsGithub && !githubChecking && (
        <div
          className="mt-4 lt-alert-warning"
          role="status"
        >
          <p className="font-medium">
            Conecte o GitHub para analisar repositórios privados.
          </p>
          <p className="mt-1">
            Privados exigem um token OAuth do usuário com escopo{" "}
            <code className="font-mono">repo</code>. Conexão pública (PAT)
            não basta.
          </p>
          <a
            href={gitHubConnectStartUrl()}
            className="mt-3 lt-btn-primary text-sm px-4 py-2"
          >
            Conectar GitHub
          </a>
        </div>
      )}

      {trigger.kind === "error" && (
        <p
          className="mt-4 text-sm text-red-300"
          role="alert"
          aria-live="polite"
        >
          {trigger.message}
        </p>
      )}

      <div className="mt-5">
        <button
          type="button"
          onClick={handleTrigger}
          disabled={disabled}
          className="lt-btn-primary text-sm px-5 py-2.5 disabled:opacity-50"
        >
          {ctaLabel}
        </button>
      </div>
    </section>
  );
}

function TargetSummary({ preflight }: { preflight: PreflightResult }) {
  const saved = preflight.scanProjects ?? [];
  if (saved.length > 0) {
    const heading =
      saved.length === 1
        ? "Alvo (escolhido por você)"
        : `Alvos (${saved.length}) — combinados em 1 PDF`;
    return (
      <div className="mt-5 lt-alert-info">
        <p className="text-xs font-semibold uppercase tracking-wider text-emerald-300">
          {heading}
        </p>
        <ul
          className={`mt-2 space-y-1 ${saved.length > 1 ? "list-decimal list-inside marker:text-neutral-500 marker:font-mono marker:text-xs" : ""}`}
        >
          {saved.map((path) => (
            <li
              key={path}
              className="flex items-baseline gap-2 font-mono text-sm text-paper break-all"
            >
              <KindBadge kind={kindFromPath(path)} />
              <span>{path}</span>
            </li>
          ))}
        </ul>
      </div>
    );
  }
  if (preflight.autoDetected) {
    return <AutoDetectedSummary auto={preflight.autoDetected} />;
  }
  return null;
}

function AutoDetectedSummary({ auto }: { auto: PreflightAutoDetected }) {
  return (
    <div className="mt-5 rounded-md border border-neutral-800/60 bg-white/5 px-4 py-3">
      <p className="text-xs font-semibold uppercase tracking-wider text-neutral-400">
        Alvo (detectado automaticamente)
      </p>
      <p className="mt-2 flex items-baseline gap-2 font-mono text-sm text-paper break-all">
        <KindBadge kind={auto.kind} />
        <span>{auto.path}</span>
      </p>
    </div>
  );
}

/**
 * Kind badge used in the saved-target list and auto-detect summary.
 * Accepts the wider `PreflightAutoDetectedKind` (which still includes
 * `yaml`) because auto-detect can surface a root `lintty.yml`. The
 * picker proper only deals with sln/csproj — see
 * `ScanTargetPicker.CandidateKindBadge`.
 */
function KindBadge({ kind }: { kind: PreflightAutoDetectedKind }) {
  return (
    <span
      className="inline-flex items-center text-[10px] font-semibold uppercase tracking-wider text-neutral-300 bg-white/5 border border-neutral-700/60 rounded px-1.5 py-0.5 shrink-0"
      title={describeCandidateKind(kind)}
    >
      {kind}
    </span>
  );
}

/**
 * Inline collapsable picker shown on the ready card when the user clicks
 * "Trocar alvo". Pre-populates with the current saved selection (or
 * empty when auto-detect is active). Two save paths:
 *   - "Salvar nova configuração" → PUT, then refresh preflight.
 *   - "Voltar para auto-detect"   → PUT with [], then refresh.
 */
function ChangeTargetPanel({
  repoId,
  preflight,
  onClose,
  onSaved,
}: {
  repoId: number;
  preflight: PreflightResult;
  onClose: () => void;
  onSaved: () => void;
}) {
  const [selection, setSelection] = useState<string[]>(
    () => preflight.scanProjects ?? [],
  );
  const [busy, setBusy] = useState(false);
  const [serverError, setServerError] = useState<string | null>(null);

  async function save(projects: string[]) {
    setBusy(true);
    setServerError(null);
    try {
      const res = await setRepoScanTarget(repoId, projects);
      if (res.status === 204 || res.ok) {
        onSaved();
        return;
      }
      if (res.status === 404) {
        setServerError(
          "Repositório não está mais disponível. Volte ao dashboard.",
        );
        return;
      }
      const err = parseApiError(res);
      setServerError(
        err?.message ?? `Erro ${res.status} ao salvar a configuração.`,
      );
    } catch {
      setServerError("Falha de rede ao salvar. Tente novamente.");
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="mt-4 rounded-lg border border-neutral-800/60 bg-white/[0.03] p-4 md:p-5 space-y-4">
      <div className="flex items-baseline justify-between gap-3">
        <h3 className="text-sm font-semibold text-paper">Trocar alvo</h3>
        <button
          type="button"
          onClick={() => void save([])}
          disabled={busy}
          className="text-xs text-neutral-400 hover:text-paper underline decoration-emerald-400/40 underline-offset-2 transition disabled:opacity-50"
        >
          Voltar para auto-detect
        </button>
      </div>
      <ScanTargetPicker
        candidates={preflight.candidates}
        selection={selection}
        onSelectionChange={setSelection}
        primaryLabel="Salvar nova configuração"
        cancelLabel="Cancelar"
        onPrimary={() => void save(selection)}
        onCancel={onClose}
        serverError={serverError}
        busy={busy}
        truncated={preflight.truncated}
      />
    </div>
  );
}

// ── Card: needs_config ──────────────────────────────────────────────────────

function NeedsConfigCard({
  repo,
  preflight,
  onTriggered,
  onPreflightChanged,
}: {
  repo: RepoDetail;
  preflight: PreflightResult;
  onTriggered: () => void;
  onPreflightChanged: () => void;
}) {
  const router = useRouter();
  const { status: ghStatus, isLoading: ghLoading } = useGitHubConnect();
  const [selection, setSelection] = useState<string[]>([]);
  const [busy, setBusy] = useState(false);
  const [serverError, setServerError] = useState<string | null>(null);

  const needsGithub = repo.isPrivate && ghStatus?.connected !== true;
  const githubChecking = repo.isPrivate && ghLoading;

  // PUT then optionally trigger. Two callers:
  //   - "Salvar e analisar agora" passes withTrigger=true.
  //   - "Salvar configuração" passes withTrigger=false.
  // On PUT failure we stay on the page with the error inline; the
  // trigger never fires unless the save succeeded.
  async function save(withTrigger: boolean) {
    if (selection.length === 0) return;
    setBusy(true);
    setServerError(null);
    try {
      const res = await setRepoScanTarget(repo.id, selection);
      if (res.status !== 204 && !res.ok) {
        if (res.status === 404) {
          setServerError(
            "Repositório não está mais disponível. Volte ao dashboard.",
          );
          return;
        }
        const err = parseApiError(res);
        setServerError(
          err?.message ?? `Erro ${res.status} ao salvar a configuração.`,
        );
        return;
      }

      if (!withTrigger) {
        onPreflightChanged();
        return;
      }

      const tr = await triggerScan({ repoId: repo.id });
      if (tr.status === 201 && tr.body) {
        onTriggered();
        router.push(`/dashboard/scans/${tr.body.publicId}/`);
        return;
      }
      if (tr.status === 404) {
        setServerError(
          "Configuração salva, mas o repositório não está mais disponível.",
        );
        onPreflightChanged();
        return;
      }
      const trErr = parseApiError(tr);
      setServerError(
        trErr?.message ??
          `Configuração salva, mas erro ${tr.status} ao disparar o scan.`,
      );
      onPreflightChanged();
    } catch {
      setServerError("Falha de rede ao salvar. Tente novamente.");
    } finally {
      setBusy(false);
    }
  }

  const primaryLabel = "Salvar e analisar agora";

  return (
    <section
      aria-labelledby="config-title"
      className="lt-card-form p-6 md:p-8"
    >
      <h2
        id="config-title"
        className="text-lg font-semibold tracking-tight text-paper"
      >
        Configurar alvo de scan
      </h2>
      <p className="mt-2 text-sm text-neutral-300 leading-relaxed">
        {preflight.reason ??
          "Vamos precisar saber o que escanear neste repositório."}
      </p>

      {needsGithub && !githubChecking && (
        <div
          className="mt-4 lt-alert-warning"
          role="status"
        >
          <p className="font-medium">
            Conecte o GitHub para analisar repositórios privados.
          </p>
          <p className="mt-1">
            Você pode salvar a configuração agora; a análise só dispara
            depois que o token estiver ativo.
          </p>
          <a
            href={gitHubConnectStartUrl()}
            className="mt-3 lt-btn-primary text-sm px-4 py-2"
          >
            Conectar GitHub
          </a>
        </div>
      )}

      <div className="mt-5">
        <ScanTargetPicker
          candidates={preflight.candidates}
          selection={selection}
          onSelectionChange={setSelection}
          primaryLabel={primaryLabel}
          secondaryLabel="Salvar configuração"
          onPrimary={() => void save(true)}
          onSecondary={() => void save(false)}
          serverError={serverError}
          busy={busy}
          truncated={preflight.truncated}
        />
      </div>
    </section>
  );
}

// ── Card: no_dotnet_project ─────────────────────────────────────────────────

function NoDotnetCard({
  reason,
  onReload,
}: {
  reason: string | null;
  onReload: () => void;
}) {
  return (
    <section
      aria-labelledby="no-dotnet-title"
      className="lt-card-form p-6 md:p-8"
    >
      <h2
        id="no-dotnet-title"
        className="text-lg font-semibold tracking-tight text-paper"
      >
        Sem projeto .NET detectável
      </h2>
      <p className="mt-2 text-sm text-neutral-300 leading-relaxed">
        {reason ??
          "Não encontramos .sln, .csproj ou lintty.yml na árvore deste repositório."}
      </p>
      <p className="mt-2 text-sm text-neutral-300 leading-relaxed">
        Se você acabou de adicionar um arquivo, aguarde alguns minutos e
        clique em <strong className="text-paper">Recarregar</strong>.
      </p>
      <div className="mt-5">
        <button
          type="button"
          onClick={onReload}
          className="lt-btn-secondary text-sm px-4 py-2"
        >
          Recarregar
        </button>
      </div>
    </section>
  );
}

// ── Legacy trigger card (preflight unavailable) ─────────────────────────────

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
      className="lt-card-form p-6 md:p-8"
    >
      <h2
        id="analyze-title"
        className="text-lg font-semibold tracking-tight text-paper"
      >
        Analisar repositório
      </h2>
      <p className="mt-2 text-sm text-neutral-300 leading-relaxed">
        O Lintty vai clonar o repositório, rodar o motor Roslyn e gerar
        um laudo PDF determinístico. O mesmo commit produz o mesmo PDF
        byte-a-byte.
      </p>
      <p className="mt-3 text-xs text-neutral-500">
        Canon: <span className="font-mono">{CANON_VERSION_DISPLAY}</span> ·
        snapshotada no momento do trigger.
      </p>

      {needsGithub && !githubChecking && (
        <div
          className="mt-4 lt-alert-warning"
          role="status"
        >
          <p className="font-medium">
            Conecte o GitHub para analisar repositórios privados.
          </p>
          <p className="mt-1">
            Privados exigem um token OAuth do usuário com escopo{" "}
            <code className="font-mono">repo</code>. Conexão pública (PAT)
            não basta.
          </p>
          <a
            href={gitHubConnectStartUrl()}
            className="mt-3 lt-btn-primary text-sm px-4 py-2"
          >
            Conectar GitHub
          </a>
        </div>
      )}

      {state.kind === "error" && (
        <p
          className="mt-4 text-sm text-red-300"
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
          className="lt-btn-primary text-sm px-5 py-2.5 disabled:opacity-50"
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
          className="text-lg font-semibold tracking-tight text-paper"
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
        <div className="mt-4 bg-white/[0.02] border border-dashed border-neutral-700/60 rounded-xl p-8 text-center">
          <p className="text-sm text-neutral-400">
            Ainda não há scans deste repositório. Clique em{" "}
            <strong className="text-paper">Analisar agora</strong> acima para
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
      className="block lt-card-soft bg-neutral-900/40 rounded-xl p-4 transition"
    >
      <div className="flex items-center justify-between gap-4">
        <div className="min-w-0 flex-1">
          <div className="flex items-center gap-2 flex-wrap">
            <code className="text-xs font-mono text-neutral-300">
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
    cls: "text-neutral-300 bg-white/5 border-neutral-700/60",
  },
  running: {
    label: "rodando",
    cls: "text-sky-300 bg-sky-500/10 border-sky-400/30",
  },
  completed: {
    label: "ok",
    cls: "text-emerald-200 bg-emerald-500/10 border-emerald-400/30",
  },
  failed: {
    label: "erro",
    cls: "text-red-300 bg-red-500/10 border-red-400/30",
  },
  cancelled: {
    label: "cancelado",
    cls: "text-neutral-400 bg-white/[0.03] border-neutral-700/60",
  },
};

// ── Generic states ──────────────────────────────────────────────────────────

function PageSkeleton() {
  return (
    <section className="max-w-3xl mx-auto space-y-6">
      <div className="lt-skeleton h-4 w-32" />
      <div className="lt-card-form p-8">
        <div className="lt-skeleton h-7 w-1/2 mb-4" />
        <div className="lt-skeleton h-4 w-2/3 mb-2" />
        <div className="lt-skeleton h-4 w-1/3" />
      </div>
      <div className="lt-card-form p-6">
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
      <div className="lt-card-form p-6 md:p-8" style={{ borderColor: "rgba(220, 38, 38, 0.35)" }}>
        <h1 className="text-2xl font-bold tracking-tight text-red-300">
          Backend indisponível
        </h1>
        <p className="mt-3 text-neutral-300 text-sm">
          Não conseguimos contatar o servidor para verificar sua sessão.
        </p>
        <div className="mt-6 flex flex-wrap gap-3">
          <button
            type="button"
            onClick={() => window.location.reload()}
            className="lt-btn-primary text-sm px-4 py-2"
          >
            Tentar novamente
          </button>
          <Link
            href="/dashboard"
            className="lt-btn-secondary text-sm px-4 py-2"
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
      <div className="lt-card-form p-8 md:p-10 text-center">
        <h1 className="text-xl font-bold tracking-tight text-paper">
          Repositório não encontrado
        </h1>
        <p className="mt-3 text-sm text-neutral-300 max-w-md mx-auto">
          Este repositório não existe ou já foi removido. Volte ao
          dashboard e adicione-o de novo se necessário.
        </p>
        <Link
          href="/dashboard"
          className="mt-6 lt-btn-primary text-sm px-4 py-2"
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
        className="lt-card-form p-6"
        style={{ borderColor: "rgba(220, 38, 38, 0.35)" }}
        role="alert"
        aria-live="polite"
      >
        <p className="text-red-300 font-semibold">
          Não foi possível carregar este repositório
        </p>
        <p className="mt-2 text-sm text-neutral-300">{message}</p>
        <div className="mt-4 flex flex-wrap gap-3">
          <button
            type="button"
            onClick={onRetry}
            className="lt-btn-primary text-sm px-4 py-2"
          >
            Tentar novamente
          </button>
          <Link
            href="/dashboard"
            className="lt-btn-secondary text-sm px-4 py-2"
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

/**
 * Saved scan_projects entries are guaranteed to be `.sln` or `.csproj`
 * paths server-side (PR S2 — yaml entries are rejected with
 * `invalid_scan_projects`). Default to `csproj` for any unrecognised
 * shape rather than synthesising a `yaml` badge that would be a lie.
 */
function kindFromPath(path: string): PreflightCandidateKind {
  if (/\.sln$/i.test(path)) return "sln";
  return "csproj";
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
