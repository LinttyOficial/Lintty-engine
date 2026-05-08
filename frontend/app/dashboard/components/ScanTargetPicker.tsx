"use client";

/**
 * Scan-target picker — presentational component used by the repo detail
 * page in two contexts (PR S2f redesign):
 *
 *   1. `status === "needs_config"` — user must pick before any scan.
 *   2. `status === "ready"` (Trocar alvo) — user wants to override the
 *      current selection / auto-detect.
 *
 * UX (PR S2f):
 *   - **Single flat list with checkboxes.** sln + csproj entries mix
 *     freely; each selected item becomes one independent scan (N
 *     targets = N PDFs). The previous yaml/sln-radios + csproj-checkbox
 *     mutex is gone — yaml is no longer a user-pickable candidate
 *     (engine resolver requires `projects:` block we can't verify in a
 *     tree walk; backend's `BuildOrderedCandidates` filters yaml out).
 *   - **Toolbar header** with selection count + Selecionar todos /
 *     Limpar mini-actions, mirroring the tone of `ImportFromGithubModal`.
 *   - **Search filter** when the candidate list grows past 5, so
 *     monorepos with dozens of csprojs stay usable. Substring match on
 *     the path.
 *   - **Footer copy** spells out the multi-PDF promise:
 *     "X selecionados → X PDFs serão gerados". Never lies — uses 1 vs
 *     plural.
 *   - **Truncated banner** preserved (GitHub Tree API truncation
 *     surfaces here and points at the CLI).
 *
 * Validation:
 *   - At least one selection.
 *   - At most {@link MAX_TARGETS_PER_TRIGGER}, mirroring backend
 *     `ScanService.MaxTargetsPerTrigger`.
 *   - We don't reject yaml entries client-side because the backend
 *     filters them out of `Candidates`; if a legacy yaml leaks in, the
 *     filter below skips it for display and the backend's
 *     `invalid_scan_projects` is a clean fallback.
 *
 * A11y:
 *   - List is a `<ul>` with `aria-labelledby` pointing at the toolbar
 *     heading.
 *   - Each row is a `<label>` wrapping the checkbox + path so the whole
 *     row is the click target.
 *   - Errors use `role="alert" aria-live="polite"` and the Save button
 *     references the error via `aria-describedby` so screen readers
 *     announce why submission was blocked.
 *   - Mini-action buttons (Selecionar todos / Limpar) carry an
 *     `aria-label` derived from the live count when their visible text
 *     is short.
 */

import { useId, useMemo, useState } from "react";
import {
  MAX_TARGETS_PER_TRIGGER,
  type PreflightCandidate,
  type PreflightCandidateKind,
} from "@/lib/dashboard-api";

/** When the candidate list is bigger than this, render a search filter. */
const SEARCH_THRESHOLD = 5;

export interface ScanTargetPickerProps {
  candidates: PreflightCandidate[];
  /** Repo-relative paths currently selected. Parent owns this state. */
  selection: string[];
  onSelectionChange: (next: string[]) => void;
  /** Save button label — varies by context ("Salvar e analisar agora"
   *  vs. "Salvar nova configuração" vs. "Salvar configuração"). The
   *  parent is also free to derive the count into the label (e.g.
   *  "Salvar e gerar 3 PDFs"); this component does not append. */
  primaryLabel: string;
  /** Optional secondary action: "Salvar configuração" (sem trigger).
   *  Omit to render only the primary CTA. */
  secondaryLabel?: string;
  /** Optional tertiary cancel action. Used by the inline "Trocar alvo"
   *  flow on the ready card; omitted on the needs_config card where
   *  there's nothing to cancel back to. */
  cancelLabel?: string;
  onPrimary: () => void;
  onSecondary?: () => void;
  onCancel?: () => void;
  /** Server-side error to surface (e.g. 400 invalid_scan_projects). */
  serverError?: string | null;
  /** True while a save request is in flight. Disables every action. */
  busy?: boolean;
  /** Banner to render above the candidate list when GitHub Tree API
   *  truncated the discovery. */
  truncated?: boolean;
}

export function ScanTargetPicker({
  candidates,
  selection,
  onSelectionChange,
  primaryLabel,
  secondaryLabel,
  cancelLabel,
  onPrimary,
  onSecondary,
  onCancel,
  serverError,
  busy,
  truncated,
}: ScanTargetPickerProps) {
  const errorId = useId();
  const headingId = useId();
  const searchId = useId();

  const [search, setSearch] = useState("");

  // Keep only sln/csproj for display. Backend's `BuildOrderedCandidates`
  // already filters yaml out, but a legacy payload could still surface
  // one — silently skip rather than render an entry the trigger would
  // reject. Order is the backend's stable order: sln first (alpha),
  // then csproj (alpha).
  const visibleCandidates = useMemo(
    () =>
      candidates.filter(
        (c) => c.kind === "sln" || c.kind === "csproj",
      ),
    [candidates],
  );

  const showSearch = visibleCandidates.length > SEARCH_THRESHOLD;

  const filteredCandidates = useMemo(() => {
    const q = search.trim().toLowerCase();
    if (!q) return visibleCandidates;
    return visibleCandidates.filter((c) =>
      c.path.toLowerCase().includes(q),
    );
  }, [visibleCandidates, search]);

  const selectedSet = useMemo(() => new Set(selection), [selection]);

  // Selection count restricted to the live, visible candidate set —
  // stale paths from a previous preflight (e.g. a csproj that has since
  // been removed from the repo) silently disappear from the count and
  // from the submit payload. The "X selecionados" copy then matches
  // what the user actually sees.
  const liveSelection = useMemo(
    () =>
      visibleCandidates
        .map((c) => c.path)
        .filter((p) => selectedSet.has(p)),
    [visibleCandidates, selectedSet],
  );
  const liveCount = liveSelection.length;

  const filteredSet = useMemo(
    () => new Set(filteredCandidates.map((c) => c.path)),
    [filteredCandidates],
  );
  const visibleSelectedCount = useMemo(
    () => liveSelection.filter((p) => filteredSet.has(p)).length,
    [liveSelection, filteredSet],
  );

  const filteredAllSelected =
    filteredCandidates.length > 0 &&
    filteredCandidates.every((c) => selectedSet.has(c.path));

  const clientError = validateClientSide(liveSelection);
  const blockSave = busy || !!clientError;

  function toggle(path: string) {
    if (selectedSet.has(path)) {
      onSelectionChange(selection.filter((p) => p !== path));
    } else {
      onSelectionChange([...selection, path]);
    }
  }

  function selectAllVisible() {
    const merged = new Set(selection);
    filteredCandidates.forEach((c) => merged.add(c.path));
    onSelectionChange(Array.from(merged));
  }

  function clearVisible() {
    if (search.trim().length === 0) {
      // No filter active — clearing means clearing everything.
      onSelectionChange([]);
      return;
    }
    // Filter active — only drop the items the user can currently see.
    onSelectionChange(selection.filter((p) => !filteredSet.has(p)));
  }

  return (
    <div className="space-y-5">
      {truncated && (
        <div
          className="rounded-md border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-900"
          role="status"
        >
          <p className="font-medium">Lista truncada</p>
          <p className="mt-1 text-amber-800/90">
            Repositório muito grande — listamos os primeiros candidatos.
            Para repos com mais de 100k arquivos, use o{" "}
            <a
              href="/cli/"
              className="underline decoration-amber-700 underline-offset-2 hover:text-amber-950"
            >
              CLI local
            </a>
            .
          </p>
        </div>
      )}

      {visibleCandidates.length === 0 ? (
        <div className="rounded-md border border-dashed border-neutral-300 bg-neutral-50 p-4 text-sm text-neutral-700">
          Nenhum candidato discoverável.
        </div>
      ) : (
        <div className="border border-neutral-200 rounded-lg bg-white">
          <header className="flex items-center justify-between gap-3 px-4 py-3 border-b border-neutral-200">
            <p
              id={headingId}
              className="text-sm font-semibold text-ink"
            >
              {liveCount === 0
                ? `${visibleCandidates.length} alvo${visibleCandidates.length === 1 ? "" : "s"} discoverável${visibleCandidates.length === 1 ? "" : "is"}`
                : `${liveCount} de ${visibleCandidates.length} selecionado${liveCount === 1 ? "" : "s"}`}
            </p>
            <div className="flex items-center gap-3 text-xs">
              {filteredCandidates.length > 0 && !filteredAllSelected && (
                <button
                  type="button"
                  onClick={selectAllVisible}
                  disabled={busy}
                  className="text-neutral-700 hover:text-ink underline underline-offset-2 disabled:opacity-50"
                  aria-label={
                    search.trim().length > 0
                      ? `Selecionar todos os ${filteredCandidates.length} alvos visíveis`
                      : `Selecionar todos os ${visibleCandidates.length} alvos`
                  }
                >
                  Selecionar todos
                </button>
              )}
              {visibleSelectedCount > 0 && (
                <button
                  type="button"
                  onClick={clearVisible}
                  disabled={busy}
                  className="text-neutral-700 hover:text-ink underline underline-offset-2 disabled:opacity-50"
                  aria-label={
                    search.trim().length > 0
                      ? `Limpar os ${visibleSelectedCount} alvos visíveis`
                      : "Limpar seleção"
                  }
                >
                  Limpar
                </button>
              )}
            </div>
          </header>

          {showSearch && (
            <div className="px-4 pt-3">
              <label htmlFor={searchId} className="sr-only">
                Filtrar alvos
              </label>
              <input
                id={searchId}
                type="search"
                autoComplete="off"
                value={search}
                onChange={(e) => setSearch(e.target.value)}
                placeholder="Filtrar por caminho…"
                disabled={busy}
                className="w-full px-3 py-2 border border-neutral-300 rounded-md text-sm bg-white focus:border-saint focus:outline-none transition disabled:opacity-50"
              />
            </div>
          )}

          {filteredCandidates.length === 0 ? (
            <p className="px-4 py-6 text-center text-sm text-neutral-600">
              Nenhum alvo bate com{" "}
              <strong className="text-ink break-all">{search}</strong>.
            </p>
          ) : (
            <ul
              className="px-2 py-2 space-y-0.5"
              aria-labelledby={headingId}
            >
              {filteredCandidates.map((c) => (
                <li key={c.path}>
                  <CandidateRow
                    candidate={c}
                    checked={selectedSet.has(c.path)}
                    onToggle={() => toggle(c.path)}
                    disabled={busy}
                  />
                </li>
              ))}
            </ul>
          )}

          <footer className="px-4 py-3 border-t border-neutral-200 bg-neutral-50/60 rounded-b-lg">
            <p className="text-xs text-neutral-700">
              {liveCount === 0 ? (
                <span className="text-neutral-500">
                  Selecione pelo menos um alvo para continuar.
                </span>
              ) : (
                <>
                  <strong className="text-ink">
                    {liveCount} alvo{liveCount === 1 ? "" : "s"} selecionado
                    {liveCount === 1 ? "" : "s"}
                  </strong>{" "}
                  → {liveCount} PDF{liveCount === 1 ? "" : "s"} ser
                  {liveCount === 1 ? "á" : "ão"} gerado
                  {liveCount === 1 ? "" : "s"}.
                </>
              )}
            </p>
          </footer>
        </div>
      )}

      {(clientError || serverError) && (
        <p
          id={errorId}
          className="text-sm text-sinner"
          role="alert"
          aria-live="polite"
        >
          {serverError ?? clientError}
        </p>
      )}

      <div className="flex flex-wrap items-center gap-3">
        <button
          type="button"
          onClick={onPrimary}
          disabled={blockSave}
          aria-describedby={clientError || serverError ? errorId : undefined}
          className="inline-flex items-center gap-2 px-5 py-2.5 rounded-md bg-saint text-white text-sm font-semibold hover:bg-[#0c3d2e] transition disabled:opacity-50 disabled:cursor-not-allowed"
        >
          {busy ? "Salvando..." : primaryLabel}
        </button>
        {secondaryLabel && onSecondary && (
          <button
            type="button"
            onClick={onSecondary}
            disabled={blockSave}
            className="inline-flex items-center gap-2 px-4 py-2.5 rounded-md border border-neutral-300 text-neutral-800 text-sm font-medium hover:bg-neutral-100 transition disabled:opacity-50 disabled:cursor-not-allowed"
          >
            {secondaryLabel}
          </button>
        )}
        {cancelLabel && onCancel && (
          <button
            type="button"
            onClick={onCancel}
            disabled={busy}
            className="text-sm text-neutral-600 hover:text-ink transition disabled:opacity-50"
          >
            {cancelLabel}
          </button>
        )}
      </div>
    </div>
  );
}

// ── Internal: row primitive ─────────────────────────────────────────────

function CandidateRow({
  candidate,
  checked,
  onToggle,
  disabled,
}: {
  candidate: PreflightCandidate;
  checked: boolean;
  onToggle: () => void;
  disabled?: boolean;
}) {
  const kindLabel = describeCandidateKind(candidate.kind);
  return (
    <label
      className={`flex items-center gap-3 px-3 py-2 rounded-md cursor-pointer transition border ${
        checked
          ? "bg-saint-bg border-saint/40"
          : "border-transparent hover:bg-neutral-50"
      } ${disabled ? "opacity-60 cursor-not-allowed" : ""}`}
    >
      <input
        type="checkbox"
        checked={checked}
        onChange={onToggle}
        disabled={disabled}
        aria-label={`${kindLabel} ${candidate.path}`}
        className="w-4 h-4 accent-saint shrink-0"
      />
      <CandidateKindBadge kind={candidate.kind} />
      <code className="font-mono text-sm text-ink break-all">
        {candidate.path}
      </code>
    </label>
  );
}

function CandidateKindBadge({ kind }: { kind: PreflightCandidateKind }) {
  // sln gets the saint accent — it's the canonical entrypoint.
  // csproj stays neutral so a list of csprojs reads as "many of the same".
  const cls =
    kind === "sln"
      ? "text-saint bg-saint-bg border-saint/30"
      : "text-neutral-700 bg-white border-neutral-200";
  return (
    <span
      className={`inline-flex items-center text-[10px] font-semibold uppercase tracking-wider rounded px-1.5 py-0.5 border shrink-0 ${cls}`}
      title={describeCandidateKind(kind)}
    >
      {kind}
    </span>
  );
}

// ── Validation ──────────────────────────────────────────────────────────

function validateClientSide(selection: string[]): string | null {
  if (selection.length === 0) {
    return "Escolha pelo menos um alvo.";
  }
  if (selection.length > MAX_TARGETS_PER_TRIGGER) {
    return `Máximo ${MAX_TARGETS_PER_TRIGGER} alvos por execução. Você selecionou ${selection.length}.`;
  }
  return null;
}

/**
 * Human-readable label for a candidate kind. Exported because the repo
 * detail page uses it on the auto-detect summary (where a yaml may
 * still appear) — see {@link PreflightCandidateKind} for why the
 * picker proper only deals with sln/csproj.
 */
export function describeCandidateKind(
  kind: PreflightCandidateKind | "yaml",
): string {
  switch (kind) {
    case "yaml":
      return "lintty.yml";
    case "sln":
      return "solution";
    case "csproj":
      return "projeto";
  }
}
