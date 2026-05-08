"use client";

/**
 * Confirmation modal for revoking the local GitHub OAuth grant.
 *
 * Two-step UX:
 *   1. User clicks "Desconectar" on the badge → this modal opens.
 *   2. User confirms → caller invokes `disconnectGitHub()` and surfaces
 *      the upstream revocation URL via an inline banner (handled by the
 *      badge component, not here).
 *
 * A11y mirrors `AddRepoModal`: ESC closes, focus enters the cancel
 * button on open, focus returns to the trigger when closed.
 */

import { useEffect, useRef } from "react";

interface GitHubDisconnectModalProps {
  open: boolean;
  pending: boolean;
  onCancel: () => void;
  onConfirm: () => void;
  triggerRef: React.RefObject<HTMLButtonElement | null>;
}

export function GitHubDisconnectModal({
  open,
  pending,
  onCancel,
  onConfirm,
  triggerRef,
}: GitHubDisconnectModalProps) {
  const cancelBtnRef = useRef<HTMLButtonElement>(null);

  useEffect(() => {
    if (!open) return undefined;

    const id = window.setTimeout(() => cancelBtnRef.current?.focus(), 0);

    const onKey = (ev: KeyboardEvent) => {
      if (ev.key === "Escape" && !pending) {
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
  }, [open, pending]);

  function handleClose() {
    onCancel();
    window.setTimeout(() => triggerRef.current?.focus(), 0);
  }

  if (!open) return null;

  return (
    <div
      className="lt-modal-backdrop is-open"
      onClick={() => {
        if (!pending) handleClose();
      }}
      aria-hidden="false"
    >
      <div
        role="dialog"
        aria-modal="true"
        aria-labelledby="gh-disconnect-title"
        aria-describedby="gh-disconnect-desc"
        className="lt-modal"
        onClick={(ev) => ev.stopPropagation()}
      >
        <h2
          id="gh-disconnect-title"
          className="text-lg font-bold tracking-tight text-ink"
        >
          Desconectar GitHub?
        </h2>
        <p
          id="gh-disconnect-desc"
          className="mt-3 text-sm text-neutral-700 leading-relaxed"
        >
          Você deixará de conseguir importar ou analisar repositórios
          privados. Repositórios já cadastrados continuam visíveis, mas
          scans futuros em privados vão falhar com{" "}
          <code className="font-mono text-xs bg-neutral-100 border border-neutral-200 rounded px-1 py-0.5">
            GITHUB_TOKEN_REVOKED
          </code>{" "}
          até você reconectar.
        </p>

        <div className="mt-6 flex flex-col-reverse sm:flex-row sm:justify-end gap-2">
          <button
            ref={cancelBtnRef}
            type="button"
            onClick={handleClose}
            disabled={pending}
            className="px-4 py-2 rounded-md border border-neutral-300 text-neutral-800 text-sm font-medium hover:bg-neutral-100 transition disabled:opacity-50"
          >
            Cancelar
          </button>
          <button
            type="button"
            onClick={onConfirm}
            disabled={pending}
            className="inline-flex items-center justify-center gap-2 px-5 py-2 rounded-md bg-sinner text-white text-sm font-semibold hover:bg-[#5a1717] transition disabled:opacity-50 disabled:cursor-not-allowed"
          >
            {pending ? "Desconectando..." : "Desconectar"}
          </button>
        </div>
      </div>
    </div>
  );
}
