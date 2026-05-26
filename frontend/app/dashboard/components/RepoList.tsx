"use client";

/**
 * Lista de repositórios da org atual. Renderiza estados loading / error /
 * empty / ready a partir de props — quem busca os dados é o pai
 * (`ReposSection`). Esse split mantém o componente "burro" reutilizável
 * caso futuramente apareça outro lugar onde a mesma lista precise existir
 * (filtro, search, etc.).
 *
 * Cada linha:
 *   - Click no card → /dashboard/repos/[id] (página entra em PR F5).
 *   - Botão "Remover" no canto direito → soft delete (com confirm
 *     nativo). O click do botão NÃO propaga o click do card (Link
 *     wrapper).
 *
 * Tenant isolation:
 *   - Se `softDeleteRepo` retornar 404, tratamos como sucesso silencioso
 *     (o backend já considera "fora da org atual" como inexistente).
 */

import Link from "next/link";
import { useState } from "react";
import {
  softDeleteRepo,
  parseApiError,
  type RepoSummary,
} from "@/lib/dashboard-api";

interface RepoListProps {
  status: "loading" | "ready" | "empty" | "error";
  repos: RepoSummary[];
  /** Mensagem do erro do backend. Só consultada quando status === "error". */
  errorMessage: string | null;
  onRetry: () => void;
  onDeleted: (repoId: number) => void;
  onAddRepo: () => void;
}

export function RepoList({
  status,
  repos,
  errorMessage,
  onRetry,
  onDeleted,
  onAddRepo,
}: RepoListProps) {
  if (status === "loading") {
    return <RepoListSkeleton />;
  }
  if (status === "error") {
    return <RepoListError message={errorMessage} onRetry={onRetry} />;
  }
  if (status === "empty") {
    return <RepoListEmpty onAddRepo={onAddRepo} />;
  }

  return (
    <ul
      className="space-y-3"
      aria-label="Lista de repositórios da organização"
    >
      {repos.map((repo) => (
        <li key={repo.id}>
          <RepoListItem repo={repo} onDeleted={onDeleted} />
        </li>
      ))}
    </ul>
  );
}

// ── Subcomponents ────────────────────────────────────────────────────────

function RepoListSkeleton() {
  return (
    <ul
      className="space-y-3"
      aria-busy="true"
      aria-label="Carregando repositórios"
    >
      {[0, 1, 2].map((i) => (
        <li
          key={i}
          className="lt-card-form p-5"
        >
          <div className="lt-skeleton h-5 w-1/3 mb-3" />
          <div className="lt-skeleton h-3 w-2/3" />
        </li>
      ))}
    </ul>
  );
}

function RepoListError({
  message,
  onRetry,
}: {
  message: string | null;
  onRetry: () => void;
}) {
  return (
    <div
      className="lt-card-form p-6"
      style={{ borderColor: "rgba(220, 38, 38, 0.35)" }}
      role="alert"
      aria-live="polite"
    >
      <p className="text-red-300 font-semibold">Não foi possível carregar a lista</p>
      <p className="mt-2 text-sm text-neutral-300">
        {message ??
          "Não conseguimos contatar o servidor para listar seus repositórios."}
      </p>
      <button
        type="button"
        onClick={onRetry}
        className="mt-4 lt-btn-primary text-sm"
      >
        Tentar novamente
      </button>
    </div>
  );
}

function RepoListEmpty({ onAddRepo }: { onAddRepo: () => void }) {
  return (
    <div className="bg-white/[0.02] border border-dashed border-neutral-700/60 rounded-xl p-10 text-center">
      <p className="text-base font-semibold text-paper">
        Nenhum repositório ainda
      </p>
      <p className="mt-2 text-sm text-neutral-400 max-w-md mx-auto">
        Adicione um repositório do GitHub para disparar scans
        org-bound. O histórico fica compartilhado com toda a sua equipe.
      </p>
      <button
        type="button"
        onClick={onAddRepo}
        className="mt-5 lt-btn-primary px-5 py-2.5"
      >
        Adicionar repositório
      </button>
    </div>
  );
}

interface RepoListItemProps {
  repo: RepoSummary;
  onDeleted: (repoId: number) => void;
}

function RepoListItem({ repo, onDeleted }: RepoListItemProps) {
  const [deleting, setDeleting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const label = repoLabelFromUrl(repo.githubUrl);

  async function handleRemove(ev: React.MouseEvent<HTMLButtonElement>) {
    ev.preventDefault();
    ev.stopPropagation();
    if (deleting) return;

    const ok = window.confirm(
      "Remover este repositório? O histórico de scans é preservado.",
    );
    if (!ok) return;

    setDeleting(true);
    setError(null);
    try {
      const res = await softDeleteRepo(repo.id);
      // 204: ok. 404: também ok (já não está na org atual — tratamos como
      // sucesso silencioso para não vazar existência via UI).
      if (res.status === 204 || res.status === 404) {
        onDeleted(repo.id);
        return;
      }
      const apiErr = parseApiError(res);
      setError(apiErr?.message ?? `Erro ${res.status}. Tente novamente.`);
    } catch (err) {
      console.error("DELETE /api/repos/{id} falhou", err);
      setError("Falha de rede ao remover. Tente novamente.");
    } finally {
      setDeleting(false);
    }
  }

  return (
    <article className="lt-card-soft bg-neutral-900/40 rounded-xl transition">
      <div className="flex items-center justify-between gap-4 p-5">
        <Link
          href={`/dashboard/repos/${repo.id}`}
          className="flex-1 min-w-0 group"
        >
          <div className="flex items-center gap-2">
            <p className="font-semibold text-paper truncate group-hover:underline decoration-emerald-400/40 underline-offset-2">
              {label}
            </p>
            {repo.isPrivate && (
              <span
                className="inline-flex items-center text-[10px] font-semibold uppercase tracking-wider text-neutral-300 bg-white/5 border border-neutral-700/60 rounded px-1.5 py-0.5"
                title="Repositório privado"
              >
                privado
              </span>
            )}
          </div>
          <p className="mt-1 text-xs text-neutral-500 truncate font-mono">
            {repo.githubUrl}
          </p>
          <p className="mt-1.5 text-xs text-neutral-500">
            Adicionado por {repo.addedBy.displayName}
          </p>
        </Link>

        <div className="flex items-center gap-2 shrink-0">
          <a
            href={repo.githubUrl}
            target="_blank"
            rel="noopener noreferrer"
            onClick={(ev) => ev.stopPropagation()}
            className="inline-flex items-center justify-center w-9 h-9 rounded-md border border-neutral-800/60 text-neutral-400 hover:text-paper hover:bg-white/5 transition"
            aria-label={`Abrir ${label} no GitHub`}
            title="Abrir no GitHub"
          >
            <svg
              width="16"
              height="16"
              viewBox="0 0 24 24"
              fill="currentColor"
              aria-hidden="true"
            >
              <path d="M12 .5C5.65.5.5 5.65.5 12c0 5.08 3.29 9.39 7.86 10.91.57.1.78-.25.78-.55 0-.27-.01-1-.02-1.96-3.2.69-3.87-1.54-3.87-1.54-.52-1.32-1.27-1.67-1.27-1.67-1.04-.71.08-.7.08-.7 1.15.08 1.76 1.18 1.76 1.18 1.02 1.75 2.68 1.25 3.34.96.1-.74.4-1.25.72-1.54-2.55-.29-5.24-1.28-5.24-5.7 0-1.26.45-2.29 1.18-3.1-.12-.29-.51-1.46.11-3.05 0 0 .96-.31 3.16 1.18a10.97 10.97 0 0 1 5.76 0c2.2-1.49 3.16-1.18 3.16-1.18.62 1.59.23 2.76.11 3.05.74.81 1.18 1.84 1.18 3.1 0 4.43-2.69 5.41-5.26 5.69.41.36.78 1.06.78 2.13 0 1.54-.01 2.78-.01 3.16 0 .31.21.66.79.55C20.21 21.39 23.5 17.07 23.5 12 23.5 5.65 18.35.5 12 .5z" />
            </svg>
          </a>
          <button
            type="button"
            onClick={handleRemove}
            disabled={deleting}
            className="inline-flex items-center px-3 h-9 rounded-md border border-neutral-800/60 text-sm text-neutral-300 hover:text-red-300 hover:border-red-500/40 hover:bg-red-500/10 transition disabled:opacity-50"
            aria-label={`Remover ${label}`}
          >
            {deleting ? "Removendo..." : "Remover"}
          </button>
        </div>
      </div>
      {error && (
        <p
          className="px-5 pb-4 -mt-1 text-sm text-red-300"
          role="alert"
          aria-live="polite"
        >
          {error}
        </p>
      )}
    </article>
  );
}

/**
 * Best-effort "owner/repo" extraído de `https://github.com/owner/repo`.
 * Cai para a URL crua se não for parseável — não vamos quebrar a UI por
 * causa de uma string esquisita.
 */
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
