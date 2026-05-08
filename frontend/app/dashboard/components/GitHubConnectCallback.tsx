"use client";

/**
 * Reads `?github_connect=success|error[&reason=...]` and renders a
 * one-shot banner at the top of the dashboard.
 *
 * Why we clear the querystring after rendering: a user pasting the
 * post-callback URL into a new tab, or hitting reload, should NOT see
 * the banner a second time. `router.replace("/dashboard")` rewrites
 * the entry in place (no extra history record) and removes the
 * `github_connect` keys.
 *
 * The success path also asks the parent provider to refresh — the
 * backend just persisted a new token, so the cached `connected: false`
 * snapshot is stale.
 */

import { useEffect, useRef, useState } from "react";
import { useRouter, useSearchParams } from "next/navigation";
import { useGitHubConnect } from "@/lib/github-connect";

type Outcome =
  | { kind: "success" }
  | { kind: "error"; reason: string | null };

export function GitHubConnectCallback() {
  const router = useRouter();
  const searchParams = useSearchParams();
  const { refresh } = useGitHubConnect();

  const [outcome, setOutcome] = useState<Outcome | null>(null);
  const errorBannerRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const param = searchParams.get("github_connect");
    if (!param) return;

    if (param === "success") {
      setOutcome({ kind: "success" });
      void refresh();
    } else if (param === "error") {
      setOutcome({ kind: "error", reason: searchParams.get("reason") });
    }

    // Strip the querystring so a refresh / shared link won't replay
    // the banner. router.replace keeps history clean (no back-stack
    // entry pointing at the callback URL).
    router.replace("/dashboard");
  }, [searchParams, router, refresh]);

  // Move screen-reader focus to the error banner when it appears so
  // assistive tech announces the failure rather than silently re-render.
  useEffect(() => {
    if (outcome?.kind === "error") {
      errorBannerRef.current?.focus();
    }
  }, [outcome]);

  if (!outcome) return null;

  if (outcome.kind === "success") {
    return (
      <div
        className="mb-6 bg-saint-bg border border-saint/20 rounded-md px-4 py-3 text-sm text-saint"
        role="status"
        aria-live="polite"
      >
        <div className="flex items-start gap-3">
          <p className="flex-1 font-semibold">
            GitHub conectado. Você já pode importar repositórios das suas
            organizações.
          </p>
          <button
            type="button"
            onClick={() => setOutcome(null)}
            className="text-saint hover:text-[#0c3d2e] p-1 -m-1"
            aria-label="Fechar aviso"
          >
            <DismissIcon />
          </button>
        </div>
      </div>
    );
  }

  return (
    <div
      ref={errorBannerRef}
      tabIndex={-1}
      className="mb-6 bg-sinner-bg border border-sinner/20 rounded-md px-4 py-3 text-sm text-sinner outline-none"
      role="alert"
      aria-live="assertive"
    >
      <div className="flex items-start gap-3">
        <div className="flex-1">
          <p className="font-semibold">Não foi possível conectar o GitHub.</p>
          <p className="mt-1 text-neutral-700">
            {humanizeReason(outcome.reason)}
          </p>
        </div>
        <button
          type="button"
          onClick={() => setOutcome(null)}
          className="text-sinner hover:text-[#5a1717] p-1 -m-1"
          aria-label="Fechar aviso"
        >
          <DismissIcon />
        </button>
      </div>
    </div>
  );
}

/**
 * Maps backend `reason` query values to friendly Portuguese sentences.
 * Mirrors the error codes in `AuthGithubConnectEndpoints.cs`:
 *   - invalid_oauth_callback / invalid_oauth_state → CSRF / param miss.
 *   - github_oauth_exchange_failed                  → upstream code rejected.
 *   - insufficient_scopes                           → user deselected on prompt.
 *   - github_identity_mismatch                      → linked under another login.
 *   - github_oauth_not_configured                   → server-side ClientId/Secret missing.
 * Anything outside this list falls through to a neutral retry message.
 */
function humanizeReason(reason: string | null): string {
  switch (reason) {
    case "invalid_oauth_callback":
      return "A resposta do GitHub veio incompleta. Tente conectar novamente.";
    case "invalid_oauth_state":
    case "state_mismatch":
      return "A sessão de autorização expirou ou veio adulterada (proteção CSRF). Tente conectar novamente sem demorar.";
    case "github_oauth_exchange_failed":
    case "code_exchange_failed":
      return "O GitHub não confirmou a autorização. Tente novamente; se persistir, verifique se sua conta GitHub está ativa.";
    case "insufficient_scopes":
    case "scope_missing":
      return "É necessário conceder os escopos repo e read:org. Tente novamente e mantenha todas as permissões marcadas na tela do GitHub.";
    case "github_identity_mismatch":
    case "identity_mismatch":
      return "A conta GitHub que autorizou é diferente da já vinculada à sua conta Lintty. Faça logout no github.com e tente novamente com a conta certa.";
    case "github_oauth_not_configured":
      return "O servidor não está configurado para GitHub Connect no momento. Avise um admin.";
    default:
      return "Erro inesperado durante a autorização. Tente novamente.";
  }
}

function DismissIcon() {
  return (
    <svg
      width="16"
      height="16"
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
  );
}
