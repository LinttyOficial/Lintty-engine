"use client";

/**
 * Scan-target picker — presentational component used by the repo detail
 * page in two contexts (Sprint 3 / PR S1f):
 *
 *   1. `status === "needs_config"` — user must pick before any scan.
 *   2. `status === "ready"` (Trocar alvo) — user wants to override the
 *      current selection / auto-detect.
 *
 * Both contexts render the same UI: three sections (yaml radios, sln
 * radios, csproj checkboxes) with a single mutex across yaml/sln and
 * csproj. The component is purely controlled — selection state lives in
 * the parent so that "Save and analyze" can read the final value
 * synchronously, and the parent decides whether to show a Cancel button
 * (only meaningful when toggleable, i.e. inside the `ready` branch).
 *
 * Why mutex client-side when the backend already validates: instant
 * feedback. Selecting a `.sln` after a `.csproj` flips the form into a
 * coherent state without a round-trip and without the user having to
 * read an inline error.
 *
 * A11y:
 *   - Each section has its own `role="group"` with an `aria-labelledby`
 *     pointing at the visible heading.
 *   - Errors use `role="alert" aria-live="polite"` and are referenced
 *     from the Save button via `aria-describedby` so screen readers
 *     announce why submission was blocked.
 *   - Labels wrap the input + path so the full row is clickable.
 */

import { useId, useMemo } from "react";
import type {
  PreflightCandidate,
  PreflightCandidateKind,
} from "@/lib/dashboard-api";

export interface ScanTargetPickerProps {
  candidates: PreflightCandidate[];
  /** Repo-relative paths currently selected. Parent owns this state. */
  selection: string[];
  onSelectionChange: (next: string[]) => void;
  /** Save button label — varies by context ("Salvar e analisar agora"
   *  vs. "Salvar nova configuração" vs. "Salvar configuração"). */
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
  const yamls = useMemo(
    () => candidates.filter((c) => c.kind === "yaml"),
    [candidates],
  );
  const slns = useMemo(
    () => candidates.filter((c) => c.kind === "sln"),
    [candidates],
  );
  const csprojs = useMemo(
    () => candidates.filter((c) => c.kind === "csproj"),
    [candidates],
  );

  const selectedSet = useMemo(() => new Set(selection), [selection]);
  const hasYaml = selection.some((p) => isYaml(p));
  const hasSln = selection.some((p) => isSln(p));
  const hasCsproj = selection.some((p) => isCsproj(p));

  const clientError = validateClientSide(selection);
  const blockSave = busy || !!clientError;

  // Picking a yaml/sln clears any csproj selection (mutex). Picking a
  // csproj clears any yaml/sln. Picking the SAME yaml/sln again keeps
  // it (radios are sticky — to clear, the user picks something else or
  // hits Cancel).
  function pickSingle(path: string) {
    onSelectionChange([path]);
  }

  function toggleCsproj(path: string) {
    if (hasYaml || hasSln) {
      // Switching from a single (yaml/sln) into multi-csproj mode
      // replaces the selection with just this csproj.
      onSelectionChange([path]);
      return;
    }
    if (selectedSet.has(path)) {
      onSelectionChange(selection.filter((p) => p !== path));
    } else {
      onSelectionChange([...selection, path]);
    }
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

      {yamls.length > 0 && (
        <CandidateGroup
          title="Lintty.yml"
          description="Configuração explícita do projeto. Tem precedência sobre .sln."
          name="scan-target-yaml"
        >
          <ul className="space-y-1">
            {yamls.map((c) => (
              <li key={c.path}>
                <SingleChoice
                  groupName="scan-target-single"
                  path={c.path}
                  checked={selectedSet.has(c.path)}
                  onSelect={() => pickSingle(c.path)}
                  disabled={busy}
                />
              </li>
            ))}
          </ul>
        </CandidateGroup>
      )}

      {slns.length > 0 && (
        <CandidateGroup
          title="Solutions (.sln)"
          description={
            slns.length > 1
              ? "Escolha qual solution analisar — só uma por vez."
              : "Solution discoverável neste repositório."
          }
          name="scan-target-sln"
        >
          <ul className="space-y-1">
            {slns.map((c) => (
              <li key={c.path}>
                <SingleChoice
                  groupName="scan-target-single"
                  path={c.path}
                  checked={selectedSet.has(c.path)}
                  onSelect={() => pickSingle(c.path)}
                  disabled={busy}
                />
              </li>
            ))}
          </ul>
        </CandidateGroup>
      )}

      {csprojs.length > 0 && (
        <CandidateGroup
          title="Projetos (.csproj)"
          description={
            csprojs.length > 1
              ? "Selecione um ou vários projetos. Selecionar 1+ aqui descarta a escolha de .sln/.yaml."
              : "Projeto discoverável neste repositório."
          }
          name="scan-target-csproj"
        >
          <ul className="space-y-1">
            {csprojs.map((c) => (
              <li key={c.path}>
                <MultiChoice
                  path={c.path}
                  checked={selectedSet.has(c.path)}
                  onToggle={() => toggleCsproj(c.path)}
                  disabled={busy}
                />
              </li>
            ))}
          </ul>
        </CandidateGroup>
      )}

      {candidates.length === 0 && (
        <div className="rounded-md border border-dashed border-neutral-300 bg-neutral-50 p-4 text-sm text-neutral-700">
          Nenhum candidato discoverável.
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

      {hasCsproj && (hasYaml || hasSln) && (
        <p className="text-xs text-neutral-500">
          Dica: misturar .sln/.yaml com .csproj não é permitido — apague
          uma das seleções.
        </p>
      )}
    </div>
  );
}

// ── Internal: section + row primitives ──────────────────────────────────

function CandidateGroup({
  title,
  description,
  name,
  children,
}: {
  title: string;
  description: string;
  name: string;
  children: React.ReactNode;
}) {
  const headingId = `${name}-heading`;
  return (
    <fieldset
      className="border border-neutral-200 rounded-lg bg-white"
      aria-labelledby={headingId}
    >
      <legend className="sr-only" id={headingId}>
        {title}
      </legend>
      <header className="px-4 pt-4">
        <p className="text-sm font-semibold text-ink">{title}</p>
        <p className="text-xs text-neutral-600 mt-0.5">{description}</p>
      </header>
      <div className="px-4 py-3">{children}</div>
    </fieldset>
  );
}

function SingleChoice({
  groupName,
  path,
  checked,
  onSelect,
  disabled,
}: {
  groupName: string;
  path: string;
  checked: boolean;
  onSelect: () => void;
  disabled?: boolean;
}) {
  return (
    <label
      className={`flex items-center gap-3 px-2 py-2 rounded-md cursor-pointer transition border ${
        checked
          ? "bg-saint-bg border-saint/40"
          : "border-transparent hover:bg-neutral-50"
      } ${disabled ? "opacity-60 cursor-not-allowed" : ""}`}
    >
      <input
        type="radio"
        name={groupName}
        checked={checked}
        onChange={onSelect}
        disabled={disabled}
        className="w-4 h-4 accent-saint shrink-0"
      />
      <code className="font-mono text-sm text-ink break-all">{path}</code>
    </label>
  );
}

function MultiChoice({
  path,
  checked,
  onToggle,
  disabled,
}: {
  path: string;
  checked: boolean;
  onToggle: () => void;
  disabled?: boolean;
}) {
  return (
    <label
      className={`flex items-center gap-3 px-2 py-2 rounded-md cursor-pointer transition border ${
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
        className="w-4 h-4 accent-saint shrink-0"
      />
      <code className="font-mono text-sm text-ink break-all">{path}</code>
    </label>
  );
}

// ── Validation (mirrors backend ValidateCombination — fast feedback) ────

function validateClientSide(selection: string[]): string | null {
  if (selection.length === 0) {
    return "Escolha pelo menos um candidato.";
  }
  let yaml = 0;
  let sln = 0;
  let csproj = 0;
  for (const p of selection) {
    if (isYaml(p)) yaml++;
    else if (isSln(p)) sln++;
    else if (isCsproj(p)) csproj++;
  }
  if (sln > 1) return "Escolha apenas um .sln.";
  if (yaml > 1) return "Escolha apenas um lintty.yml.";
  if ((sln > 0 || yaml > 0) && csproj > 0) {
    return "Não misture .sln/.yaml com .csproj. Escolha uma solution OU uma lista de projetos.";
  }
  return null;
}

function isYaml(path: string): boolean {
  return path === "lintty.yml" || /\/lintty\.yml$/i.test(path);
}

function isSln(path: string): boolean {
  return /\.sln$/i.test(path);
}

function isCsproj(path: string): boolean {
  return /\.csproj$/i.test(path);
}

export function describeCandidateKind(kind: PreflightCandidateKind): string {
  switch (kind) {
    case "yaml":
      return "lintty.yml";
    case "sln":
      return "solution";
    case "csproj":
      return "projeto";
  }
}
