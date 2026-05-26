"use client";

/**
 * Orquestra a tela de repositórios:
 *   - puxa `listRepos()` no mount + retry,
 *   - controla o modal "Adicionar repositório",
 *   - mostra um toast inline (auto-dismiss em 5s) após sucesso de
 *     adição/remoção.
 *
 * Este componente assume que o pai já passou pelo auth gate
 * (`isAuthenticated && currentOrg`). Ele não re-checa auth — só conversa
 * com a API multi-tenant.
 */

import { useCallback, useEffect, useRef, useState } from "react";
import {
  listRepos,
  parseApiError,
  type RepoSummary,
} from "@/lib/dashboard-api";
import { AddRepoModal, type AddRepoOutcome } from "./AddRepoModal";
import { ImportFromGithubModal } from "./ImportFromGithubModal";
import { RepoList } from "./RepoList";

type ListStatus = "loading" | "ready" | "empty" | "error";

interface ReposSectionProps {
  orgName: string;
}

interface Toast {
  kind: "success" | "info";
  message: string;
}

export function ReposSection({ orgName }: ReposSectionProps) {
  const [status, setStatus] = useState<ListStatus>("loading");
  const [repos, setRepos] = useState<RepoSummary[]>([]);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  const [modalOpen, setModalOpen] = useState(false);
  const [importModalOpen, setImportModalOpen] = useState(false);
  const [toast, setToast] = useState<Toast | null>(null);

  const addBtnRef = useRef<HTMLButtonElement>(null);
  const importBtnRef = useRef<HTMLButtonElement>(null);
  const toastTimerRef = useRef<number | null>(null);

  const loadRepos = useCallback(async () => {
    setStatus("loading");
    setErrorMessage(null);
    try {
      const res = await listRepos();
      if (res.ok && Array.isArray(res.body)) {
        if (res.body.length === 0) {
          setRepos([]);
          setStatus("empty");
        } else {
          setRepos(res.body);
          setStatus("ready");
        }
        return;
      }
      // Não-OK: tenta extrair mensagem do envelope de erro do backend.
      const apiErr = parseApiError(res);
      setErrorMessage(
        apiErr?.message ?? `Erro ${res.status} ao listar repositórios.`,
      );
      setStatus("error");
    } catch (err) {
      console.error("GET /api/repos falhou", err);
      setErrorMessage(
        "Não foi possível contatar o servidor. Verifique sua conexão.",
      );
      setStatus("error");
    }
  }, []);

  useEffect(() => {
    void loadRepos();
  }, [loadRepos]);

  // Limpa timer do toast no unmount.
  useEffect(() => {
    return () => {
      if (toastTimerRef.current !== null) {
        window.clearTimeout(toastTimerRef.current);
      }
    };
  }, []);

  function flashToast(t: Toast) {
    if (toastTimerRef.current !== null) {
      window.clearTimeout(toastTimerRef.current);
    }
    setToast(t);
    toastTimerRef.current = window.setTimeout(() => {
      setToast(null);
      toastTimerRef.current = null;
    }, 5000);
  }

  function handleOpenModal() {
    setModalOpen(true);
  }

  function handleOpenImportModal() {
    setImportModalOpen(true);
  }

  function handleAddSuccess(outcome: AddRepoOutcome) {
    if (outcome.kind === "created") {
      flashToast({ kind: "success", message: "Repositório adicionado." });
    } else {
      // 409 — backend é idempotente; comunica claramente que não houve
      // novo cadastro, para o usuário não ficar confuso.
      flashToast({
        kind: "info",
        message: "Repositório já estava cadastrado.",
      });
    }
    void loadRepos();
  }

  function handleImported() {
    // Não fechamos o modal — o user pode importar várias repos da
    // mesma org em sequência. Só atualizamos o toast e a lista lá embaixo.
    flashToast({ kind: "success", message: "Repositório importado." });
    void loadRepos();
  }

  function handleDeleted(_repoId: number) {
    flashToast({ kind: "success", message: "Repositório removido." });
    void loadRepos();
  }

  return (
    <section className="max-w-3xl mx-auto" aria-labelledby="repos-heading">
      <p className="text-xs font-semibold tracking-widest text-emerald-300/80 uppercase mb-3 font-mono">
        Dashboard &middot; Beta
      </p>
      <div className="flex flex-col sm:flex-row sm:items-end sm:justify-between gap-4">
        <div>
          <h1
            id="repos-heading"
            className="text-3xl md:text-4xl font-bold tracking-tight text-paper"
          >
            Repositórios
          </h1>
          <p className="mt-2 text-neutral-300">
            Repositórios de{" "}
            <strong className="text-paper">{orgName}</strong> disponíveis para
            análise.
          </p>
        </div>
        <div className="flex flex-wrap gap-3 shrink-0">
          <button
            ref={importBtnRef}
            type="button"
            onClick={handleOpenImportModal}
            className="lt-btn-secondary px-5 py-2.5"
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
            Importar do GitHub
          </button>
          <button
            ref={addBtnRef}
            type="button"
            onClick={handleOpenModal}
            className="lt-btn-primary px-5 py-2.5"
          >
            <span aria-hidden="true">+</span>
            Adicionar repositório
          </button>
        </div>
      </div>

      {toast && (
        <div
          className={
            toast.kind === "success"
              ? "mt-6 lt-alert-info"
              : "mt-6 bg-white/5 border border-neutral-800/60 text-neutral-300 text-sm rounded-md px-4 py-3"
          }
          role="status"
          aria-live="polite"
        >
          {toast.message}
        </div>
      )}

      <div className="mt-8">
        <RepoList
          status={status}
          repos={repos}
          errorMessage={errorMessage}
          onRetry={() => void loadRepos()}
          onDeleted={handleDeleted}
          onAddRepo={handleOpenModal}
        />
      </div>

      <AddRepoModal
        open={modalOpen}
        onClose={() => setModalOpen(false)}
        onSuccess={handleAddSuccess}
        triggerRef={addBtnRef}
      />

      <ImportFromGithubModal
        open={importModalOpen}
        onClose={() => setImportModalOpen(false)}
        onImported={handleImported}
        triggerRef={importBtnRef}
      />
    </section>
  );
}
