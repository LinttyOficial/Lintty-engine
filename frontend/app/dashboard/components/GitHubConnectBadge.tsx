"use client";

/**
 * GitHub Connect status pill rendered inside the dashboard.
 *
 * Three states:
 *   - loading  → skeleton, no actions.
 *   - !connected → neutral pill + "Conectar GitHub" button. Clicking
 *     bounces the browser to the backend's /start endpoint, which 302s
 *     to github.com/login/oauth/authorize. apiFetch is intentionally
 *     NOT used — that would only fetch the redirect, not honour it.
 *   - connected  → saint pill (login + scopes hint) + "Desconectar".
 *
 * The "opcional" hint is a deliberate editorial choice: F2 lets users
 * add public repos without a GitHub token, so we explicitly tell them
 * Connect is only required for private repos / org browse. Removing
 * that line makes the empty state feel like a forced funnel.
 *
 * After a successful disconnect we surface the upstream revoke URL
 * inline (`https://github.com/settings/applications`). The local
 * revocation does NOT touch GitHub — only marking `revoked_at` on our
 * own row — so the user MUST visit GitHub to fully cut the grant.
 */

import { useRef, useState } from "react";
import {
  disconnectGitHub,
  gitHubConnectStartUrl,
  parseApiError,
  type GitHubConnectDeleteResult,
} from "@/lib/dashboard-api";
import { useGitHubConnect } from "@/lib/github-connect";
import { GitHubDisconnectModal } from "./GitHubDisconnectModal";

interface AfterDisconnectInfo {
  upstreamRevokeUrl: string;
  alreadyRevoked: boolean;
}

export function GitHubConnectBadge() {
  const { status, isLoading, networkError, refresh } = useGitHubConnect();

  const [modalOpen, setModalOpen] = useState(false);
  const [pending, setPending] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [afterDisconnect, setAfterDisconnect] =
    useState<AfterDisconnectInfo | null>(null);

  const disconnectBtnRef = useRef<HTMLButtonElement>(null);

  function handleConnect() {
    // Full-page redirect — GitHub's authorize page replaces the SPA.
    window.location.href = gitHubConnectStartUrl();
  }

  function openDisconnectModal() {
    setError(null);
    setAfterDisconnect(null);
    setModalOpen(true);
  }

  function closeModal() {
    if (pending) return;
    setModalOpen(false);
  }

  async function handleConfirmDisconnect() {
    setPending(true);
    setError(null);
    try {
      const res = await disconnectGitHub();
      if (res.ok && res.body) {
        const body = res.body as GitHubConnectDeleteResult;
        setAfterDisconnect({
          upstreamRevokeUrl: body.upstreamRevokeUrl,
          alreadyRevoked: body.alreadyRevoked,
        });
        setModalOpen(false);
        // Re-fetch — backend should now return connected=false. Done
        // after closing the modal so the badge re-renders with the
        // new state in one paint.
        await refresh();
      } else {
        const apiErr = parseApiError(res);
        setError(
          apiErr?.message ??
            `Erro ${res.status} ao desconectar. Tente novamente.`,
        );
      }
    } catch (err) {
      console.error("DELETE /api/auth/github/connect falhou", err);
      setError(
        "Não foi possível contatar o servidor. Verifique sua conexão.",
      );
    } finally {
      setPending(false);
    }
  }

  // Loading skeleton — same shimmer language as ReposSection.
  if (isLoading) {
    return (
      <div
        className="mt-6 flex items-center gap-3"
        aria-busy="true"
        aria-label="Verificando conexão com o GitHub"
      >
        <div className="lt-skeleton h-7 w-56 rounded-full" />
      </div>
    );
  }

  if (networkError) {
    // Don't block the dashboard — render a quiet inline notice so the
    // user can still see their repos. Retry hits the same context.
    return (
      <div
        className="mt-6 flex flex-wrap items-center gap-3 text-sm"
        role="status"
        aria-live="polite"
      >
        <span className="inline-flex items-center gap-2 px-3 py-1.5 rounded-full bg-white/5 border border-neutral-700/60 text-neutral-300">
          <span
            className="w-2 h-2 rounded-full bg-neutral-500"
            aria-hidden="true"
          />
          Status do GitHub indisponível
        </span>
        <button
          type="button"
          onClick={() => void refresh()}
          className="text-neutral-300 hover:text-paper underline underline-offset-2"
        >
          Tentar novamente
        </button>
      </div>
    );
  }

  const connected = status?.connected === true;

  return (
    <>
      <div className="mt-6 flex flex-wrap items-center gap-3">
        {connected ? (
          <ConnectedPill scopes={status?.scopes} />
        ) : (
          <NotConnectedPill />
        )}

        {connected ? (
          <button
            ref={disconnectBtnRef}
            type="button"
            onClick={openDisconnectModal}
            className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-md text-sm text-neutral-300 hover:text-red-300 hover:bg-red-500/10 border border-neutral-800/60 hover:border-red-500/40 transition"
          >
            Desconectar
          </button>
        ) : (
          <button
            type="button"
            onClick={handleConnect}
            className="inline-flex items-center gap-2 px-4 py-1.5 rounded-md bg-saint text-white text-sm font-semibold hover:bg-emerald-600 transition shadow-md shadow-emerald-900/40"
          >
            <GitHubGlyph />
            Conectar GitHub
          </button>
        )}
      </div>

      {!connected && (
        <p className="mt-2 text-xs text-neutral-500 max-w-prose-tight">
          Opcional para repositórios públicos. Necessário para listar e
          importar repositórios privados das suas organizações no
          GitHub.
        </p>
      )}

      {error && (
        <div
          className="mt-3 lt-alert-danger"
          role="alert"
          aria-live="polite"
        >
          {error}
        </div>
      )}

      {afterDisconnect && (
        <AfterDisconnectBanner
          upstreamRevokeUrl={afterDisconnect.upstreamRevokeUrl}
          alreadyRevoked={afterDisconnect.alreadyRevoked}
          onDismiss={() => setAfterDisconnect(null)}
        />
      )}

      <GitHubDisconnectModal
        open={modalOpen}
        pending={pending}
        onCancel={closeModal}
        onConfirm={() => void handleConfirmDisconnect()}
        triggerRef={disconnectBtnRef}
      />
    </>
  );
}

// ── Subcomponents ──────────────────────────────────────────────────────────

function ConnectedPill({ scopes }: { scopes?: string[] }) {
  // We show the scope summary (e.g. "repo · read:org") rather than the
  // login because the backend's status endpoint deliberately omits the
  // GitHub login from the response — login is held in `external_logins`,
  // not in the privacy-console projection. The pill stays informative
  // either way: scopes are what actually grants the user power.
  const scopeLabel =
    scopes && scopes.length > 0 ? scopes.join(" · ") : "repo · read:org";
  return (
    <span
      className="inline-flex items-center gap-2 px-3 py-1.5 rounded-full bg-emerald-500/10 border border-emerald-400/30 text-emerald-200 text-sm font-semibold"
      aria-label={`GitHub conectado com escopos ${scopeLabel}`}
    >
      <GitHubGlyph />
      <span>GitHub conectado</span>
      <span className="font-normal text-emerald-300/70 hidden sm:inline">
        ({scopeLabel})
      </span>
    </span>
  );
}

function NotConnectedPill() {
  return (
    <span
      className="inline-flex items-center gap-2 px-3 py-1.5 rounded-full bg-white/5 border border-neutral-700/60 text-neutral-300 text-sm font-semibold"
      aria-label="GitHub não conectado"
    >
      <span
        className="w-2 h-2 rounded-full bg-neutral-500"
        aria-hidden="true"
      />
      GitHub não conectado
    </span>
  );
}

function AfterDisconnectBanner({
  upstreamRevokeUrl,
  alreadyRevoked,
  onDismiss,
}: {
  upstreamRevokeUrl: string;
  alreadyRevoked: boolean;
  onDismiss: () => void;
}) {
  return (
    <div
      className="mt-4 lt-alert-info"
      role="status"
      aria-live="polite"
    >
      <div className="flex items-start gap-3">
        <p className="flex-1 leading-relaxed">
          {alreadyRevoked
            ? "GitHub já estava desconectado deste lado."
            : "GitHub desconectado localmente."}{" "}
          Para revogar <strong className="text-paper">completamente</strong> a autorização do
          Lintty no GitHub, abra{" "}
          <a
            href={upstreamRevokeUrl}
            target="_blank"
            rel="noopener noreferrer"
            className="underline underline-offset-2 font-semibold text-emerald-200 hover:text-emerald-100"
          >
            github.com/settings/applications
          </a>
          .
        </p>
        <button
          type="button"
          onClick={onDismiss}
          className="text-neutral-400 hover:text-paper p-1 -m-1"
          aria-label="Fechar aviso"
        >
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
        </button>
      </div>
    </div>
  );
}

function GitHubGlyph() {
  return (
    <svg
      width="14"
      height="14"
      viewBox="0 0 24 24"
      fill="currentColor"
      aria-hidden="true"
    >
      <path d="M12 .5C5.65.5.5 5.65.5 12c0 5.08 3.29 9.39 7.86 10.91.57.1.78-.25.78-.55 0-.27-.01-1-.02-1.96-3.2.69-3.87-1.54-3.87-1.54-.52-1.32-1.27-1.67-1.27-1.67-1.04-.71.08-.7.08-.7 1.15.08 1.76 1.18 1.76 1.18 1.02 1.75 2.68 1.25 3.34.96.1-.74.4-1.25.72-1.54-2.55-.29-5.24-1.28-5.24-5.7 0-1.26.45-2.29 1.18-3.1-.12-.29-.51-1.46.11-3.05 0 0 .96-.31 3.16 1.18a10.97 10.97 0 0 1 5.76 0c2.2-1.49 3.16-1.18 3.16-1.18.62 1.59.23 2.76.11 3.05.74.81 1.18 1.84 1.18 3.1 0 4.43-2.69 5.41-5.26 5.69.41.36.78 1.06.78 2.13 0 1.54-.01 2.78-.01 3.16 0 .31.21.66.79.55C20.21 21.39 23.5 17.07 23.5 12 23.5 5.65 18.35.5 12 .5z" />
    </svg>
  );
}
