"use client";

/**
 * Scan-target picker. Tabbed layout (PR S3f):
 *
 *   - Notice card on top points users at the auto-detect shortcut
 *     (lintty.yml in repo root with `projects:`).
 *   - Search input filters candidates in the active tab.
 *   - Two tabs: "Solutions" (.sln, radio — single-select) and
 *     "Projetos" (.csproj, checkbox — multi-select).
 *   - Switching tabs clears the other tab's selection so submission
 *     never carries an invalid mix (backend rejects sln+csproj
 *     combinations and 2+ slns; the UI prevents them at the source).
 *   - Footer copy reflects the single-PDF model: 1+ csprojs combine
 *     into one runtime lintty.yml at scan time and produce one PDF.
 *
 * Backend constraint (RepoPreflightService.ValidateCombination):
 *   empty → ok (clears) | 1 sln → ok | 1+ csprojs → ok | anything else
 *   → 400 invalid_scan_projects. This component honors all of that
 *   client-side; the server is the source of truth either way.
 */

import { useId, useMemo, useState } from "react";
import {
  MAX_SCAN_PROJECTS,
  type PreflightCandidate,
  type PreflightCandidateKind,
} from "@/lib/dashboard-api";

interface ScanTargetPickerProps {
  candidates: PreflightCandidate[];
  selection: string[];
  onSelectionChange: (next: string[]) => void;
  primaryLabel: string;
  secondaryLabel?: string;
  cancelLabel?: string;
  onPrimary: () => void;
  onSecondary?: () => void;
  onCancel?: () => void;
  serverError?: string | null;
  busy?: boolean;
  truncated?: boolean;
}

type TabKey = "sln" | "csproj";

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
  busy = false,
  truncated = false,
}: ScanTargetPickerProps) {
  const errorId = useId();
  const tablistId = useId();

  const slnCandidates = useMemo(
    () => candidates.filter((c) => c.kind === "sln"),
    [candidates],
  );
  const csprojCandidates = useMemo(
    () => candidates.filter((c) => c.kind === "csproj"),
    [candidates],
  );

  // Default tab: prefer the side that already has a saved selection,
  // else "sln" if any sln exists, else "csproj". Reactive only on mount.
  const [tab, setTab] = useState<TabKey>(() => {
    const initialKind = inferKindFromSelection(selection, candidates);
    if (initialKind) return initialKind;
    return slnCandidates.length > 0 ? "sln" : "csproj";
  });

  const [search, setSearch] = useState("");

  const activeCandidates = tab === "sln" ? slnCandidates : csprojCandidates;
  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase();
    if (!q) return activeCandidates;
    return activeCandidates.filter((c) => c.path.toLowerCase().includes(q));
  }, [activeCandidates, search]);

  const slnSelected = selection.find((p) =>
    slnCandidates.some((c) => c.path === p),
  );
  const csprojSelected = selection.filter((p) =>
    csprojCandidates.some((c) => c.path === p),
  );

  const slnTabCount = slnSelected ? `1/${slnCandidates.length}` : `${slnCandidates.length}`;
  const csprojTabCount = csprojSelected.length > 0
    ? `${csprojSelected.length}/${csprojCandidates.length}`
    : `${csprojCandidates.length}`;

  function switchTab(next: TabKey) {
    if (next === tab) return;
    // Drop the other tab's selection so submission is always valid.
    onSelectionChange([]);
    setTab(next);
    setSearch("");
  }

  function pickSln(path: string) {
    onSelectionChange([path]);
  }

  function toggleCsproj(path: string) {
    const isOn = csprojSelected.includes(path);
    if (isOn) {
      onSelectionChange(csprojSelected.filter((p) => p !== path));
      return;
    }
    onSelectionChange([...csprojSelected, path]);
  }

  function selectAllVisibleCsprojs() {
    const merged = new Set(csprojSelected);
    filtered.forEach((c) => merged.add(c.path));
    onSelectionChange(Array.from(merged));
  }

  function clearCsprojs() {
    onSelectionChange([]);
  }

  // Cap mirror — block submit before round-trip when the user goes wild.
  const overCap = selection.length > MAX_SCAN_PROJECTS;
  const clientError = overCap
    ? `Máximo ${MAX_SCAN_PROJECTS} alvos. Reduza a seleção.`
    : null;

  const errorText = serverError ?? clientError;
  const canSubmit = selection.length > 0 && !overCap && !busy;

  return (
    <div className="space-y-4">
      <NoticeCard />

      {truncated && <TruncatedBanner />}

      <SearchInput
        value={search}
        onChange={setSearch}
        placeholder={
          tab === "sln" ? "Buscar solution…" : "Buscar projeto…"
        }
      />

      {/* Tabs */}
      <div role="tablist" aria-label="Tipo de alvo" id={tablistId} className="flex gap-1 border-b border-neutral-200">
        <TabButton
          active={tab === "sln"}
          onClick={() => switchTab("sln")}
          disabled={busy || slnCandidates.length === 0}
          panelId="picker-panel-sln"
        >
          Solutions <span className="ml-1 text-neutral-500">({slnTabCount})</span>
        </TabButton>
        <TabButton
          active={tab === "csproj"}
          onClick={() => switchTab("csproj")}
          disabled={busy || csprojCandidates.length === 0}
          panelId="picker-panel-csproj"
        >
          Projetos <span className="ml-1 text-neutral-500">({csprojTabCount})</span>
        </TabButton>
      </div>

      {/* Tab panel */}
      {tab === "sln" ? (
        <div
          role="tabpanel"
          id="picker-panel-sln"
          aria-labelledby={tablistId}
          className="space-y-2"
        >
          {slnCandidates.length === 0 ? (
            <EmptyTab message="Nenhum arquivo .sln encontrado neste repositório." />
          ) : filtered.length === 0 ? (
            <NoMatchMessage query={search} />
          ) : (
            <ul className="space-y-2" aria-label="Solutions disponíveis">
              {filtered.map((c) => (
                <li key={c.path}>
                  <SlnRow
                    path={c.path}
                    selected={slnSelected === c.path}
                    onPick={() => pickSln(c.path)}
                    disabled={busy}
                  />
                </li>
              ))}
            </ul>
          )}
        </div>
      ) : (
        <div
          role="tabpanel"
          id="picker-panel-csproj"
          aria-labelledby={tablistId}
          className="space-y-3"
        >
          {csprojCandidates.length === 0 ? (
            <EmptyTab message="Nenhum arquivo .csproj encontrado neste repositório." />
          ) : (
            <>
              <CsprojToolbar
                selectedCount={csprojSelected.length}
                visibleCount={filtered.length}
                allVisibleSelected={
                  filtered.length > 0 &&
                  filtered.every((c) => csprojSelected.includes(c.path))
                }
                onSelectAllVisible={selectAllVisibleCsprojs}
                onClear={clearCsprojs}
                disabled={busy}
              />
              {filtered.length === 0 ? (
                <NoMatchMessage query={search} />
              ) : (
                <ul className="space-y-2" aria-label="Projetos disponíveis">
                  {filtered.map((c) => (
                    <li key={c.path}>
                      <CsprojRow
                        path={c.path}
                        selected={csprojSelected.includes(c.path)}
                        onToggle={() => toggleCsproj(c.path)}
                        disabled={busy}
                      />
                    </li>
                  ))}
                </ul>
              )}
            </>
          )}
        </div>
      )}

      {/* Footer status */}
      <div className="text-sm text-neutral-600">
        {selection.length === 0 ? (
          <span>Selecione 1 solution OU 1+ projetos para continuar.</span>
        ) : tab === "sln" || slnSelected ? (
          <span>1 solution selecionada → 1 PDF.</span>
        ) : (
          <span>
            <strong className="text-ink">{csprojSelected.length}</strong>{" "}
            {csprojSelected.length === 1 ? "projeto selecionado" : "projetos selecionados"}{" "}
            → 1 PDF combinado.
          </span>
        )}
      </div>

      {errorText && (
        <p
          id={errorId}
          className="text-sm text-sinner"
          role="alert"
          aria-live="polite"
        >
          {errorText}
        </p>
      )}

      <div className="flex flex-wrap items-center gap-3 pt-2">
        <button
          type="button"
          onClick={onPrimary}
          disabled={!canSubmit}
          aria-describedby={errorText ? errorId : undefined}
          className="inline-flex items-center justify-center gap-2 px-5 py-2 rounded-md bg-saint text-white text-sm font-semibold hover:bg-[#0c3d2e] transition disabled:opacity-50 disabled:cursor-not-allowed"
        >
          {busy ? "Salvando…" : primaryLabel}
        </button>
        {onSecondary && secondaryLabel && (
          <button
            type="button"
            onClick={onSecondary}
            disabled={!canSubmit}
            className="inline-flex items-center justify-center gap-2 px-5 py-2 rounded-md border border-neutral-300 text-neutral-800 text-sm font-medium hover:bg-neutral-100 transition disabled:opacity-50 disabled:cursor-not-allowed"
          >
            {secondaryLabel}
          </button>
        )}
        {onCancel && cancelLabel && (
          <button
            type="button"
            onClick={onCancel}
            disabled={busy}
            className="inline-flex items-center text-sm text-neutral-600 hover:text-ink underline decoration-neutral-300 underline-offset-2 transition disabled:opacity-50"
          >
            {cancelLabel}
          </button>
        )}
      </div>
    </div>
  );
}

// ── Notice ──────────────────────────────────────────────────────────────────

function NoticeCard() {
  return (
    <div className="rounded-md border border-saint/20 bg-saint-bg/40 px-4 py-3 text-sm text-neutral-700">
      <p>
        <span className="font-semibold text-saint">Dica:</span>{" "}
        Quer detecção automática e específica? Adicione um arquivo{" "}
        <code className="font-mono text-xs bg-white border border-neutral-200 rounded px-1 py-0.5">
          lintty.yml
        </code>{" "}
        na raiz do projeto com a chave{" "}
        <code className="font-mono text-xs bg-white border border-neutral-200 rounded px-1 py-0.5">
          projects:
        </code>{" "}
        declarando o que escanear — o Lintty resolve sozinho a cada execução.
      </p>
    </div>
  );
}

function TruncatedBanner() {
  return (
    <div
      className="rounded-md border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-900"
      role="status"
    >
      <p className="font-medium">Lista truncada.</p>
      <p className="mt-1 text-amber-800/90">
        Repositório muito grande para uma listagem completa via API do
        GitHub. Foram listados os primeiros candidatos. Para repos com mais
        de 100k arquivos use a CLI local.
      </p>
    </div>
  );
}

// ── Search ──────────────────────────────────────────────────────────────────

function SearchInput({
  value,
  onChange,
  placeholder,
}: {
  value: string;
  onChange: (v: string) => void;
  placeholder: string;
}) {
  return (
    <div>
      <label htmlFor="picker-search" className="sr-only">
        Buscar
      </label>
      <input
        id="picker-search"
        type="search"
        autoComplete="off"
        value={value}
        onChange={(ev) => onChange(ev.target.value)}
        placeholder={placeholder}
        className="w-full px-4 py-2 border border-neutral-300 rounded-md text-sm bg-white focus:border-saint focus:outline-none transition"
      />
    </div>
  );
}

// ── Tabs ────────────────────────────────────────────────────────────────────

function TabButton({
  active,
  onClick,
  disabled,
  panelId,
  children,
}: {
  active: boolean;
  onClick: () => void;
  disabled: boolean;
  panelId: string;
  children: React.ReactNode;
}) {
  return (
    <button
      type="button"
      role="tab"
      aria-selected={active}
      aria-controls={panelId}
      tabIndex={active ? 0 : -1}
      disabled={disabled}
      onClick={onClick}
      className={`px-4 py-2 -mb-px border-b-2 text-sm font-semibold transition disabled:opacity-50 disabled:cursor-not-allowed ${
        active
          ? "border-saint text-saint"
          : "border-transparent text-neutral-600 hover:text-ink hover:border-neutral-300"
      }`}
    >
      {children}
    </button>
  );
}

// ── Csproj toolbar ──────────────────────────────────────────────────────────

function CsprojToolbar({
  selectedCount,
  visibleCount,
  allVisibleSelected,
  onSelectAllVisible,
  onClear,
  disabled,
}: {
  selectedCount: number;
  visibleCount: number;
  allVisibleSelected: boolean;
  onSelectAllVisible: () => void;
  onClear: () => void;
  disabled: boolean;
}) {
  return (
    <div className="flex items-center justify-between gap-3 text-sm">
      <span className="text-neutral-600">
        {selectedCount === 0
          ? `${visibleCount} disponíve${visibleCount === 1 ? "l" : "is"}`
          : `${selectedCount} selecionado${selectedCount === 1 ? "" : "s"}`}
      </span>
      <div className="flex items-center gap-3">
        {selectedCount > 0 && (
          <button
            type="button"
            onClick={onClear}
            disabled={disabled}
            className="text-neutral-700 hover:text-ink underline underline-offset-2 disabled:opacity-50"
          >
            Limpar
          </button>
        )}
        {visibleCount > 0 && (
          <button
            type="button"
            onClick={allVisibleSelected ? onClear : onSelectAllVisible}
            disabled={disabled}
            className="text-neutral-700 hover:text-ink underline underline-offset-2 disabled:opacity-50"
          >
            {allVisibleSelected ? "Desmarcar todos" : "Selecionar todos"}
          </button>
        )}
      </div>
    </div>
  );
}

// ── Rows ────────────────────────────────────────────────────────────────────

function SlnRow({
  path,
  selected,
  onPick,
  disabled,
}: {
  path: string;
  selected: boolean;
  onPick: () => void;
  disabled: boolean;
}) {
  return (
    <label
      className={`flex items-center gap-3 p-3 rounded-lg border cursor-pointer transition ${
        selected
          ? "border-saint/60 bg-saint-bg/60 ring-1 ring-saint/20"
          : "border-neutral-200 bg-white hover:border-neutral-300"
      } ${disabled ? "opacity-60 cursor-not-allowed" : ""}`}
    >
      <input
        type="radio"
        name="sln-pick"
        checked={selected}
        onChange={onPick}
        disabled={disabled}
        className="w-4 h-4 accent-saint shrink-0"
      />
      <KindBadge kind="sln" />
      <code className="flex-1 min-w-0 font-mono text-sm text-ink truncate">
        {path}
      </code>
    </label>
  );
}

function CsprojRow({
  path,
  selected,
  onToggle,
  disabled,
}: {
  path: string;
  selected: boolean;
  onToggle: () => void;
  disabled: boolean;
}) {
  return (
    <label
      className={`flex items-center gap-3 p-3 rounded-lg border cursor-pointer transition ${
        selected
          ? "border-saint/60 bg-saint-bg/60 ring-1 ring-saint/20"
          : "border-neutral-200 bg-white hover:border-neutral-300"
      } ${disabled ? "opacity-60 cursor-not-allowed" : ""}`}
    >
      <input
        type="checkbox"
        checked={selected}
        onChange={onToggle}
        disabled={disabled}
        className="w-4 h-4 accent-saint shrink-0"
      />
      <KindBadge kind="csproj" />
      <code className="flex-1 min-w-0 font-mono text-sm text-ink truncate">
        {path}
      </code>
    </label>
  );
}

function KindBadge({ kind }: { kind: PreflightCandidateKind }) {
  const cls =
    kind === "sln"
      ? "text-saint bg-saint-bg border-saint/30"
      : "text-neutral-700 bg-neutral-100 border-neutral-200";
  return (
    <span
      className={`inline-flex items-center text-[10px] font-semibold uppercase tracking-wider rounded border px-1.5 py-0.5 shrink-0 ${cls}`}
    >
      {kind}
    </span>
  );
}

// ── Empty / no-match ────────────────────────────────────────────────────────

function EmptyTab({ message }: { message: string }) {
  return (
    <div className="bg-white border border-dashed border-neutral-300 rounded-lg p-6 text-center">
      <p className="text-sm text-neutral-600">{message}</p>
    </div>
  );
}

function NoMatchMessage({ query }: { query: string }) {
  return (
    <div className="text-center text-sm text-neutral-600 py-6">
      Nenhum candidato bate com{" "}
      <strong className="text-ink">{query}</strong>.
    </div>
  );
}

// ── Helpers ─────────────────────────────────────────────────────────────────

/**
 * If the current selection contains a sln, default the picker to the
 * Solutions tab; if it contains csprojs, default to Projetos. Used only
 * on initial mount so the user reopens the picker on the side they
 * configured before.
 */
function inferKindFromSelection(
  selection: string[],
  candidates: PreflightCandidate[],
): TabKey | null {
  if (selection.length === 0) return null;
  const first = selection[0];
  const match = candidates.find((c) => c.path === first);
  if (!match) return null;
  return match.kind;
}

/**
 * Human-readable label for a candidate kind. Exported because the repo
 * detail page uses it on the auto-detect summary (where a yaml may
 * still appear via the auto-detect channel — see
 * `PreflightAutoDetectedKind`).
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
