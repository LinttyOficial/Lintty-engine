"use client";

/**
 * Modal "Importar do GitHub" — fluxo em duas etapas dentro do mesmo
 * modal:
 *
 *   1. Pré-conexão (só aparece se `useGitHubConnect()` reportou
 *      `connected !== true`): convite para conectar o GitHub primeiro.
 *      Click no CTA dispara o full-page redirect para o backend OAuth
 *      start, mesmo padrão do `GitHubConnectBadge`.
 *   2. Etapa "orgs": lista as GitHub orgs do user via
 *      `listGitHubOrgs()`. Click numa org avança para a etapa 3.
 *   3. Etapa "repos": lista repos da org escolhida via
 *      `listGitHubOrgRepos(login)` (inclui privados). Filtro client-side
 *      por nome. Cada item tem botão "Importar" que chama
 *      `importRepo(...)`.
 *
 * Decisões editoriais:
 *   - **Não fechamos o modal após import.** Importar várias repos da
 *     mesma org em sequência é o caso de uso comum; fechar a cada click
 *     forçaria reabrir + navegar até a org de novo. O CTA "Concluído"
 *     no rodapé fecha quando o user terminou.
 *   - **409 conta como sucesso.** O backend é idempotente em
 *     `(org_id, github_repo_id)`: se o repo já estava importado, ele
 *     retorna 200 com a mesma row. Para o user, "✓ Importado" é a
 *     mensagem certa nos dois casos — o estado do mundo é o mesmo.
 *   - **401 redireciona para /login.** Sessão expirada é igual a não
 *     autenticado; não tentamos reconciliar dentro do modal.
 *   - **Busca client-side.** O backend devolve até 100 repos por
 *     org; filtrar aqui evita um round-trip por keystroke e mantém o
 *     UX previsível (sem debounce / sem loading flicker).
 *
 * A11y: mesmo padrão de AddRepoModal — `role="dialog" aria-modal`, ESC
 * fecha, foco volta para o trigger.
 */

import {
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
  type RefObject,
} from "react";
import { useRouter } from "next/navigation";
import {
  gitHubConnectStartUrl,
  importRepo,
  listGitHubOrgRepos,
  listGitHubOrgs,
  parseApiError,
  type GitHubOrgSummary,
  type GitHubRepoSummary,
} from "@/lib/dashboard-api";
import { useGitHubConnect } from "@/lib/github-connect";

interface ImportFromGithubModalProps {
  open: boolean;
  onClose: () => void;
  /** Disparado a cada importação bem-sucedida (201 ou 409). O pai
   *  re-fetcha a lista de repos sem fechar este modal. */
  onImported: () => void;
  /** Reaberto a foco quando o modal fecha. */
  triggerRef: RefObject<HTMLButtonElement | null>;
}

type Step = "needs-connect" | "pick-org" | "pick-repo";

interface OrgsState {
  status: "idle" | "loading" | "ready" | "error";
  orgs: GitHubOrgSummary[];
  errorMessage: string | null;
  errorCode: string | null;
}

interface ReposState {
  status: "idle" | "loading" | "ready" | "error";
  repos: GitHubRepoSummary[];
  errorMessage: string | null;
  errorCode: string | null;
}

interface ImportedMap {
  [fullName: string]: "pending" | "done" | { error: string };
}

interface BulkProgress {
  total: number;
  done: number;
  active: boolean;
}

export function ImportFromGithubModal({
  open,
  onClose,
  onImported,
  triggerRef,
}: ImportFromGithubModalProps) {
  const router = useRouter();
  const { status: connectStatus, refresh: refreshConnect } = useGitHubConnect();
  const isConnected = connectStatus?.connected === true;

  const [step, setStep] = useState<Step>("pick-org");
  const [orgsState, setOrgsState] = useState<OrgsState>({
    status: "idle",
    orgs: [],
    errorMessage: null,
    errorCode: null,
  });
  const [reposState, setReposState] = useState<ReposState>({
    status: "idle",
    repos: [],
    errorMessage: null,
    errorCode: null,
  });
  const [selectedOrg, setSelectedOrg] = useState<GitHubOrgSummary | null>(null);
  const [search, setSearch] = useState("");
  const [imported, setImported] = useState<ImportedMap>({});
  const [selected, setSelected] = useState<Set<string>>(() => new Set());
  const [bulk, setBulk] = useState<BulkProgress>({
    total: 0,
    done: 0,
    active: false,
  });

  const cardRef = useRef<HTMLDivElement>(null);
  const closeBtnRef = useRef<HTMLButtonElement>(null);

  // ── Loaders ────────────────────────────────────────────────────────

  const loadOrgs = useCallback(async () => {
    setOrgsState({
      status: "loading",
      orgs: [],
      errorMessage: null,
      errorCode: null,
    });
    try {
      const res = await listGitHubOrgs();
      if (res.ok && Array.isArray(res.body)) {
        setOrgsState({
          status: "ready",
          orgs: res.body,
          errorMessage: null,
          errorCode: null,
        });
        return;
      }
      if (res.status === 401) {
        router.push("/login");
        return;
      }
      const apiErr = parseApiError(res);
      if (res.status === 403 && apiErr?.error === "github_not_connected") {
        // Backend says the token vanished between our connect-status
        // check and this call. Re-sync the badge and pivot to the
        // connect step so the user gets a recovery path.
        await refreshConnect();
        setStep("needs-connect");
        return;
      }
      setOrgsState({
        status: "error",
        orgs: [],
        errorMessage:
          apiErr?.message ??
          `Erro ${res.status} ao listar organizações do GitHub.`,
        errorCode: apiErr?.error ?? null,
      });
    } catch (err) {
      console.error("GET /api/github/orgs falhou", err);
      setOrgsState({
        status: "error",
        orgs: [],
        errorMessage:
          "Não foi possível contatar o servidor. Verifique sua conexão.",
        errorCode: null,
      });
    }
  }, [router, refreshConnect]);

  const loadRepos = useCallback(
    async (login: string) => {
      setReposState({
        status: "loading",
        repos: [],
        errorMessage: null,
        errorCode: null,
      });
      try {
        const res = await listGitHubOrgRepos(login);
        if (res.ok && Array.isArray(res.body)) {
          setReposState({
            status: "ready",
            repos: res.body,
            errorMessage: null,
            errorCode: null,
          });
          return;
        }
        if (res.status === 401) {
          router.push("/login");
          return;
        }
        const apiErr = parseApiError(res);
        if (res.status === 403 && apiErr?.error === "github_not_connected") {
          await refreshConnect();
          setStep("needs-connect");
          return;
        }
        setReposState({
          status: "error",
          repos: [],
          errorMessage:
            apiErr?.message ??
            (res.status === 404 || res.status === 403
              ? "Não foi possível acessar essa organização. Tente reconectar o GitHub."
              : res.status >= 500
                ? "Erro ao falar com o GitHub. Tente novamente em alguns segundos."
                : `Erro ${res.status} ao listar repositórios.`),
          errorCode: apiErr?.error ?? null,
        });
      } catch (err) {
        console.error("GET /api/github/orgs/{login}/repos falhou", err);
        setReposState({
          status: "error",
          repos: [],
          errorMessage:
            "Não foi possível contatar o servidor. Verifique sua conexão.",
          errorCode: null,
        });
      }
    },
    [router, refreshConnect],
  );

  // ── Lifecycle ──────────────────────────────────────────────────────

  // Reset state when the modal opens; pick the right starting step
  // based on the live Connect status from the shared context.
  useEffect(() => {
    if (!open) return;
    setSearch("");
    setImported({});
    setSelected(new Set());
    setBulk({ total: 0, done: 0, active: false });
    setSelectedOrg(null);
    setReposState({
      status: "idle",
      repos: [],
      errorMessage: null,
      errorCode: null,
    });
    if (!isConnected) {
      setStep("needs-connect");
      setOrgsState({
        status: "idle",
        orgs: [],
        errorMessage: null,
        errorCode: null,
      });
      return;
    }
    setStep("pick-org");
    void loadOrgs();
  }, [open, isConnected, loadOrgs]);

  // ESC fecha + foco inicial.
  useEffect(() => {
    if (!open) return undefined;
    const id = window.setTimeout(() => closeBtnRef.current?.focus(), 0);
    const onKey = (ev: KeyboardEvent) => {
      if (ev.key === "Escape") {
        ev.stopPropagation();
        handleClose();
      }
    };
    document.addEventListener("keydown", onKey);
    return () => {
      window.clearTimeout(id);
      document.removeEventListener("keydown", onKey);
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open]);

  function handleClose() {
    onClose();
    window.setTimeout(() => triggerRef.current?.focus(), 0);
  }

  function handleConnectGithub() {
    window.location.href = gitHubConnectStartUrl();
  }

  function handlePickOrg(org: GitHubOrgSummary) {
    setSelectedOrg(org);
    setSearch("");
    setSelected(new Set());
    setStep("pick-repo");
    void loadRepos(org.login);
  }

  function handleBackToOrgs() {
    setSelectedOrg(null);
    setSearch("");
    setSelected(new Set());
    setStep("pick-org");
    setReposState({
      status: "idle",
      repos: [],
      errorMessage: null,
      errorCode: null,
    });
  }

  function toggleSelect(fullName: string) {
    setSelected((prev) => {
      const next = new Set(prev);
      if (next.has(fullName)) next.delete(fullName);
      else next.add(fullName);
      return next;
    });
  }

  async function handleImport(repo: GitHubRepoSummary) {
    if (!selectedOrg || bulk.active) return;
    if (imported[repo.fullName] === "done" || imported[repo.fullName] === "pending") {
      return;
    }
    await importOne(repo, selectedOrg.login);
  }

  // ── Bulk import ────────────────────────────────────────────────────

  // Sequential, not parallel: GitHub's API has user rate limits and
  // serial keeps progress predictable for the user. Errors don't halt
  // the run — each repo's outcome is recorded in `imported` and the
  // user sees per-row ✓ / error after.
  async function importOne(
    repo: GitHubRepoSummary,
    orgLogin: string,
  ): Promise<"ok" | "fatal"> {
    setImported((prev) => ({ ...prev, [repo.fullName]: "pending" }));
    try {
      const res = await importRepo({
        githubOrgLogin: orgLogin,
        repoFullName: repo.fullName,
      });
      if (res.status === 201 || res.status === 200) {
        setImported((prev) => ({ ...prev, [repo.fullName]: "done" }));
        onImported();
        return "ok";
      }
      if (res.status === 401) {
        router.push("/login");
        return "fatal";
      }
      const apiErr = parseApiError(res);
      if (res.status === 403 && apiErr?.error === "github_not_connected") {
        await refreshConnect();
        setStep("needs-connect");
        return "fatal";
      }
      const message =
        apiErr?.message ??
        (res.status >= 500
          ? "Erro ao falar com o GitHub. Tente novamente."
          : `Erro ${res.status} ao importar.`);
      setImported((prev) => ({
        ...prev,
        [repo.fullName]: { error: message },
      }));
      return "ok";
    } catch (err) {
      console.error("POST /api/repos/import falhou", err);
      setImported((prev) => ({
        ...prev,
        [repo.fullName]: { error: "Falha de rede ao importar." },
      }));
      return "ok";
    }
  }

  async function handleBulkImport() {
    if (!selectedOrg || bulk.active) return;
    // Snapshot pegs the order at click time; a search filter change
    // mid-import won't shift the queue. Skip already-done items.
    const queue = reposState.repos.filter(
      (r) => selected.has(r.fullName) && imported[r.fullName] !== "done",
    );
    if (queue.length === 0) return;

    setBulk({ total: queue.length, done: 0, active: true });
    for (let i = 0; i < queue.length; i++) {
      const result = await importOne(queue[i], selectedOrg.login);
      if (result === "fatal") {
        setBulk({ total: queue.length, done: i, active: false });
        return;
      }
      setBulk({ total: queue.length, done: i + 1, active: true });
    }
    setBulk({ total: queue.length, done: queue.length, active: false });
    // Drop the imported items from the selection so the toolbar count
    // reflects what's still actionable.
    setSelected((prev) => {
      const next = new Set(prev);
      queue.forEach((r) => next.delete(r.fullName));
      return next;
    });
  }

  // ── Filter ─────────────────────────────────────────────────────────
  const filteredRepos = useMemo(() => {
    const q = search.trim().toLowerCase();
    if (!q) return reposState.repos;
    return reposState.repos.filter(
      (r) =>
        r.fullName.toLowerCase().includes(q) ||
        r.name.toLowerCase().includes(q),
    );
  }, [reposState.repos, search]);

  if (!open) return null;

  // ── Render ─────────────────────────────────────────────────────────

  const titleId = "import-gh-title";

  return (
    <div
      className="lt-modal-backdrop is-open"
      onClick={handleClose}
      aria-hidden="false"
    >
      <div
        ref={cardRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        className="lt-modal lt-modal-wide max-h-[80vh] flex flex-col"
        onClick={(ev) => ev.stopPropagation()}
      >
        <div className="flex items-start justify-between gap-4">
          <div className="min-w-0">
            <h2
              id={titleId}
              className="text-lg font-bold tracking-tight text-ink"
            >
              Importar do GitHub
            </h2>
            {step === "pick-repo" && selectedOrg && (
              <p className="mt-1 text-sm text-neutral-600 truncate">
                Repositórios de{" "}
                <strong className="text-ink">{selectedOrg.login}</strong>
              </p>
            )}
            {step === "pick-org" && (
              <p className="mt-1 text-sm text-neutral-600">
                Escolha uma organização para listar seus repositórios.
              </p>
            )}
          </div>
          <button
            ref={closeBtnRef}
            type="button"
            onClick={handleClose}
            className="text-neutral-500 hover:text-ink transition -mt-1 -mr-1 p-1 shrink-0"
            aria-label="Fechar"
          >
            <svg
              width="20"
              height="20"
              viewBox="0 0 20 20"
              fill="none"
              aria-hidden="true"
            >
              <path
                d="M5 5l10 10M15 5L5 15"
                stroke="currentColor"
                strokeWidth="1.6"
                strokeLinecap="round"
              />
            </svg>
          </button>
        </div>

        {/* Body grows; rolagem rola o miolo, não a página. */}
        <div className="mt-5 flex-1 min-h-0 overflow-y-auto -mx-1 px-1">
          {step === "needs-connect" && (
            <NeedsConnectStep onConnect={handleConnectGithub} />
          )}

          {step === "pick-org" && (
            <PickOrgStep state={orgsState} onPick={handlePickOrg} onRetry={() => void loadOrgs()} />
          )}

          {step === "pick-repo" && selectedOrg && (
            <PickRepoStep
              state={reposState}
              search={search}
              onSearchChange={setSearch}
              filteredRepos={filteredRepos}
              imported={imported}
              selected={selected}
              onToggleSelect={toggleSelect}
              onSelectAllVisible={() => {
                setSelected((prev) => {
                  const next = new Set(prev);
                  filteredRepos.forEach((r) => {
                    if (imported[r.fullName] !== "done") next.add(r.fullName);
                  });
                  return next;
                });
              }}
              onClearSelection={() => setSelected(new Set())}
              bulkActive={bulk.active}
              onImport={handleImport}
              onRetry={() => void loadRepos(selectedOrg.login)}
              onReconnect={handleConnectGithub}
            />
          )}
        </div>

        {/* Footer: ações contextuais. */}
        <div className="mt-5 pt-4 border-t border-neutral-200 flex flex-col-reverse sm:flex-row sm:items-center sm:justify-between gap-3">
          <div className="text-xs text-neutral-500">
            {step === "pick-repo" && (
              <button
                type="button"
                onClick={handleBackToOrgs}
                className="inline-flex items-center gap-1 text-neutral-700 hover:text-ink transition"
              >
                <svg
                  width="14"
                  height="14"
                  viewBox="0 0 20 20"
                  fill="none"
                  aria-hidden="true"
                >
                  <path
                    d="M12 5l-5 5 5 5"
                    stroke="currentColor"
                    strokeWidth="1.6"
                    strokeLinecap="round"
                    strokeLinejoin="round"
                  />
                </svg>
                Voltar para organizações
              </button>
            )}
          </div>
          <div className="flex items-center gap-3">
            {step === "pick-repo" && (selected.size > 0 || bulk.active) && (
              <button
                type="button"
                onClick={handleBulkImport}
                disabled={bulk.active || selected.size === 0}
                className="inline-flex items-center justify-center gap-2 px-5 py-2 rounded-md bg-saint text-white text-sm font-semibold hover:bg-[#0c3d2e] transition disabled:opacity-60 disabled:cursor-not-allowed"
              >
                {bulk.active
                  ? `Importando ${bulk.done}/${bulk.total}…`
                  : `Importar ${selected.size} selecionado${selected.size === 1 ? "" : "s"}`}
              </button>
            )}
            <button
              type="button"
              onClick={handleClose}
              disabled={bulk.active}
              className="inline-flex items-center justify-center gap-2 px-5 py-2 rounded-md border border-neutral-300 text-neutral-800 text-sm font-medium hover:bg-neutral-100 transition disabled:opacity-60 disabled:cursor-not-allowed"
            >
              Concluído
            </button>
          </div>
        </div>
      </div>
    </div>
  );
}

// ── Steps ───────────────────────────────────────────────────────────

function NeedsConnectStep({ onConnect }: { onConnect: () => void }) {
  return (
    <div className="bg-neutral-50 border border-neutral-200 rounded-lg p-6 text-center">
      <p className="text-base font-semibold text-ink">
        Conecte seu GitHub primeiro
      </p>
      <p className="mt-2 text-sm text-neutral-600 max-w-md mx-auto">
        Para listar suas organizações e importar repositórios — incluindo
        os privados — precisamos de uma autorização OAuth com escopos{" "}
        <code className="font-mono text-xs bg-white border border-neutral-200 rounded px-1 py-0.5">
          repo
        </code>{" "}
        e{" "}
        <code className="font-mono text-xs bg-white border border-neutral-200 rounded px-1 py-0.5">
          read:org
        </code>
        . Você pode revogar a qualquer momento pelo dashboard ou direto
        no GitHub.
      </p>
      <button
        type="button"
        onClick={onConnect}
        className="mt-5 inline-flex items-center gap-2 px-5 py-2.5 rounded-md bg-ink text-white text-sm font-semibold hover:bg-neutral-800 transition"
      >
        <svg
          width="14"
          height="14"
          viewBox="0 0 24 24"
          fill="currentColor"
          aria-hidden="true"
        >
          <path d="M12 .5C5.65.5.5 5.65.5 12c0 5.08 3.29 9.39 7.86 10.91.57.1.78-.25.78-.55 0-.27-.01-1-.02-1.96-3.2.69-3.87-1.54-3.87-1.54-.52-1.32-1.27-1.67-1.27-1.67-1.04-.71.08-.7.08-.7 1.15.08 1.76 1.18 1.76 1.18 1.02 1.75 2.68 1.25 3.34.96.1-.74.4-1.25.72-1.54-2.55-.29-5.24-1.28-5.24-5.7 0-1.26.45-2.29 1.18-3.1-.12-.29-.51-1.46.11-3.05 0 0 .96-.31 3.16 1.18a10.97 10.97 0 0 1 5.76 0c2.2-1.49 3.16-1.18 3.16-1.18.62 1.59.23 2.76.11 3.05.74.81 1.18 1.84 1.18 3.1 0 4.43-2.69 5.41-5.26 5.69.41.36.78 1.06.78 2.13 0 1.54-.01 2.78-.01 3.16 0 .31.21.66.79.55C20.21 21.39 23.5 17.07 23.5 12 23.5 5.65 18.35.5 12 .5z" />
        </svg>
        Conectar GitHub
      </button>
    </div>
  );
}

function PickOrgStep({
  state,
  onPick,
  onRetry,
}: {
  state: OrgsState;
  onPick: (org: GitHubOrgSummary) => void;
  onRetry: () => void;
}) {
  if (state.status === "loading" || state.status === "idle") {
    return <ListSkeleton rows={3} />;
  }
  if (state.status === "error") {
    return (
      <ErrorBlock message={state.errorMessage} onRetry={onRetry} />
    );
  }
  if (state.orgs.length === 0) {
    return (
      <div className="bg-white border border-dashed border-neutral-300 rounded-lg p-8 text-center">
        <p className="text-base font-semibold text-ink">
          Nenhuma organização
        </p>
        <p className="mt-2 text-sm text-neutral-600 max-w-md mx-auto">
          Você não pertence a nenhuma organização no GitHub. Use{" "}
          <strong>Adicionar repositório</strong> para colar a URL de um
          repo público.
        </p>
      </div>
    );
  }
  return (
    <ul
      className="space-y-2"
      aria-label="Organizações do GitHub disponíveis"
    >
      {state.orgs.map((org) => (
        <li key={org.id}>
          <button
            type="button"
            onClick={() => onPick(org)}
            className="w-full flex items-center gap-3 p-3 rounded-lg border border-neutral-200 bg-white hover:border-saint/40 hover:bg-saint-bg/40 transition text-left"
          >
            <OrgAvatar org={org} />
            <span className="flex-1 min-w-0">
              <span className="block font-semibold text-ink truncate">
                {org.login}
              </span>
            </span>
            <svg
              width="16"
              height="16"
              viewBox="0 0 20 20"
              fill="none"
              aria-hidden="true"
              className="text-neutral-400 shrink-0"
            >
              <path
                d="M8 5l5 5-5 5"
                stroke="currentColor"
                strokeWidth="1.6"
                strokeLinecap="round"
                strokeLinejoin="round"
              />
            </svg>
          </button>
        </li>
      ))}
    </ul>
  );
}

function PickRepoStep({
  state,
  search,
  onSearchChange,
  filteredRepos,
  imported,
  selected,
  onToggleSelect,
  onSelectAllVisible,
  onClearSelection,
  bulkActive,
  onImport,
  onRetry,
  onReconnect,
}: {
  state: ReposState;
  search: string;
  onSearchChange: (v: string) => void;
  filteredRepos: GitHubRepoSummary[];
  imported: ImportedMap;
  selected: Set<string>;
  onToggleSelect: (fullName: string) => void;
  onSelectAllVisible: () => void;
  onClearSelection: () => void;
  bulkActive: boolean;
  onImport: (repo: GitHubRepoSummary) => void;
  onRetry: () => void;
  onReconnect: () => void;
}) {
  if (state.status === "loading" || state.status === "idle") {
    return <ListSkeleton rows={4} />;
  }
  if (state.status === "error") {
    const looksLikeOrgIssue =
      state.errorCode === "org_not_in_user_orgs" ||
      state.errorCode === "github_not_connected";
    return (
      <ErrorBlock
        message={state.errorMessage}
        onRetry={onRetry}
        secondary={
          looksLikeOrgIssue
            ? { label: "Reconectar GitHub", onClick: onReconnect }
            : undefined
        }
      />
    );
  }
  // How many of the currently-visible (post-search) rows are selectable
  // (i.e., not already imported). Drives the toolbar's "Selecionar todos"
  // affordance — when zero, the action is meaningless.
  const visibleSelectable = filteredRepos.filter(
    (r) => imported[r.fullName] !== "done",
  );
  const allVisibleSelected =
    visibleSelectable.length > 0 &&
    visibleSelectable.every((r) => selected.has(r.fullName));

  return (
    <>
      <div className="mb-3">
        <label htmlFor="import-gh-search" className="sr-only">
          Buscar repositório
        </label>
        <input
          id="import-gh-search"
          type="search"
          autoComplete="off"
          value={search}
          onChange={(e) => onSearchChange(e.target.value)}
          placeholder="Buscar por nome…"
          className="w-full px-4 py-2 border border-neutral-300 rounded-md text-sm bg-white focus:border-saint focus:outline-none transition"
        />
      </div>
      {state.repos.length > 0 && (
        <div className="mb-3 flex items-center justify-between gap-3 text-sm">
          <span className="text-neutral-600">
            {selected.size === 0
              ? `${visibleSelectable.length} disponíve${visibleSelectable.length === 1 ? "l" : "is"} para importar`
              : `${selected.size} selecionado${selected.size === 1 ? "" : "s"}`}
          </span>
          <div className="flex items-center gap-2">
            {selected.size > 0 && (
              <button
                type="button"
                onClick={onClearSelection}
                disabled={bulkActive}
                className="text-neutral-700 hover:text-ink underline underline-offset-2 disabled:opacity-50"
              >
                Limpar seleção
              </button>
            )}
            {visibleSelectable.length > 0 && (
              <button
                type="button"
                onClick={allVisibleSelected ? onClearSelection : onSelectAllVisible}
                disabled={bulkActive}
                className="text-neutral-700 hover:text-ink underline underline-offset-2 disabled:opacity-50"
              >
                {allVisibleSelected ? "Desmarcar todos" : "Selecionar todos"}
              </button>
            )}
          </div>
        </div>
      )}
      {state.repos.length === 0 ? (
        <div className="bg-white border border-dashed border-neutral-300 rounded-lg p-8 text-center">
          <p className="text-base font-semibold text-ink">
            Nenhum repositório
          </p>
          <p className="mt-2 text-sm text-neutral-600">
            Esta organização não tem repositórios visíveis para a sua
            conta.
          </p>
        </div>
      ) : filteredRepos.length === 0 ? (
        <div className="text-center text-sm text-neutral-600 py-8">
          Nenhum repositório bate com{" "}
          <strong className="text-ink">{search}</strong>.
        </div>
      ) : (
        <ul className="space-y-2" aria-label="Repositórios da organização">
          {filteredRepos.map((repo) => (
            <li key={repo.id}>
              <RepoRow
                repo={repo}
                state={imported[repo.fullName]}
                isSelected={selected.has(repo.fullName)}
                onToggleSelect={() => onToggleSelect(repo.fullName)}
                bulkActive={bulkActive}
                onImport={() => onImport(repo)}
              />
            </li>
          ))}
        </ul>
      )}
    </>
  );
}

function RepoRow({
  repo,
  state,
  isSelected,
  onToggleSelect,
  bulkActive,
  onImport,
}: {
  repo: GitHubRepoSummary;
  state: ImportedMap[string] | undefined;
  isSelected: boolean;
  onToggleSelect: () => void;
  bulkActive: boolean;
  onImport: () => void;
}) {
  const isPending = state === "pending";
  const isDone = state === "done";
  const errorMessage =
    typeof state === "object" && state !== null && "error" in state
      ? state.error
      : null;

  return (
    <article
      className={`bg-white border rounded-lg transition ${isSelected ? "border-saint/60 ring-1 ring-saint/20" : "border-neutral-200"}`}
    >
      <div className="flex items-center gap-3 p-3">
        {!isDone && (
          <input
            type="checkbox"
            checked={isSelected}
            onChange={onToggleSelect}
            disabled={bulkActive || isPending}
            aria-label={`Selecionar ${repo.fullName}`}
            className="w-4 h-4 accent-saint cursor-pointer disabled:opacity-50 disabled:cursor-not-allowed shrink-0"
          />
        )}
        <div className="flex-1 min-w-0">
          <div className="flex items-center gap-2 flex-wrap">
            <p className="font-semibold text-ink truncate">
              {repo.fullName}
            </p>
            {repo.private && (
              <span
                className="inline-flex items-center text-[10px] font-semibold uppercase tracking-wider text-neutral-700 bg-neutral-100 border border-neutral-200 rounded px-1.5 py-0.5"
                title="Repositório privado"
              >
                privado
              </span>
            )}
            <span className="text-[10px] font-mono text-neutral-500">
              {repo.defaultBranch}
            </span>
          </div>
        </div>
        {isDone ? (
          <span className="inline-flex items-center gap-1.5 px-3 h-9 rounded-md bg-saint-bg border border-saint/20 text-saint text-sm font-semibold shrink-0">
            <svg
              width="14"
              height="14"
              viewBox="0 0 20 20"
              fill="none"
              aria-hidden="true"
            >
              <path
                d="M5 10l3.5 3.5L15 7"
                stroke="currentColor"
                strokeWidth="1.8"
                strokeLinecap="round"
                strokeLinejoin="round"
              />
            </svg>
            Importado
          </span>
        ) : (
          <button
            type="button"
            onClick={onImport}
            disabled={isPending || bulkActive}
            className="inline-flex items-center justify-center gap-2 px-4 h-9 rounded-md border border-neutral-300 text-neutral-800 text-sm font-medium hover:bg-neutral-100 transition disabled:opacity-50 disabled:cursor-not-allowed shrink-0"
          >
            {isPending ? "Importando…" : "Importar só este"}
          </button>
        )}
      </div>
      {errorMessage && (
        <p
          className="px-3 pb-3 -mt-1 text-sm text-sinner"
          role="alert"
          aria-live="polite"
        >
          {errorMessage}
        </p>
      )}
    </article>
  );
}

function OrgAvatar({ org }: { org: GitHubOrgSummary }) {
  if (org.avatarUrl) {
    return (
      // GitHub avatars são externos (avatars.githubusercontent.com); next/image
      // exigiria configurar remotePatterns + ignora a vantagem de optimization
      // para um asset que entra apenas em modal autenticado.
      // eslint-disable-next-line @next/next/no-img-element
      <img
        src={org.avatarUrl}
        alt=""
        width={32}
        height={32}
        className="rounded-md border border-neutral-200 shrink-0"
      />
    );
  }
  const initial = (org.login[0] ?? "?").toUpperCase();
  return (
    <span
      className="w-8 h-8 rounded-md bg-saint text-white inline-flex items-center justify-center font-bold text-sm shrink-0"
      aria-hidden="true"
    >
      {initial}
    </span>
  );
}

function ListSkeleton({ rows }: { rows: number }) {
  return (
    <ul
      className="space-y-2"
      aria-busy="true"
      aria-label="Carregando"
    >
      {Array.from({ length: rows }, (_, i) => (
        <li
          key={i}
          className="bg-white border border-neutral-200 rounded-lg p-3"
        >
          <div className="lt-skeleton h-4 w-1/2 mb-2" />
          <div className="lt-skeleton h-3 w-1/3" />
        </li>
      ))}
    </ul>
  );
}

function ErrorBlock({
  message,
  onRetry,
  secondary,
}: {
  message: string | null;
  onRetry: () => void;
  secondary?: { label: string; onClick: () => void };
}) {
  return (
    <div
      className="bg-sinner-bg border border-sinner/20 rounded-lg p-5"
      role="alert"
      aria-live="polite"
    >
      <p className="text-sinner font-semibold">Não foi possível carregar</p>
      <p className="mt-2 text-sm text-neutral-700">
        {message ?? "Erro desconhecido."}
      </p>
      <div className="mt-4 flex flex-wrap gap-2">
        <button
          type="button"
          onClick={onRetry}
          className="inline-flex items-center gap-2 px-4 py-2 rounded-md bg-ink text-white text-sm font-semibold hover:bg-neutral-800 transition"
        >
          Tentar novamente
        </button>
        {secondary && (
          <button
            type="button"
            onClick={secondary.onClick}
            className="inline-flex items-center gap-2 px-4 py-2 rounded-md border border-neutral-300 text-neutral-800 text-sm font-medium hover:bg-neutral-100 transition"
          >
            {secondary.label}
          </button>
        )}
      </div>
    </div>
  );
}
