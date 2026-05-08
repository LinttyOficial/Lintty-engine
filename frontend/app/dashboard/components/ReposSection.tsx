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
  const [toast, setToast] = useState<Toast | null>(null);

  const addBtnRef = useRef<HTMLButtonElement>(null);
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

  function handleDeleted(_repoId: number) {
    flashToast({ kind: "success", message: "Repositório removido." });
    void loadRepos();
  }

  return (
    <section className="max-w-3xl mx-auto" aria-labelledby="repos-heading">
      <p className="text-xs font-semibold tracking-widest text-neutral-500 uppercase mb-3">
        Dashboard &middot; Beta
      </p>
      <div className="flex flex-col sm:flex-row sm:items-end sm:justify-between gap-4">
        <div>
          <h1
            id="repos-heading"
            className="text-3xl md:text-4xl font-bold tracking-tight"
          >
            Repositórios
          </h1>
          <p className="mt-2 text-neutral-700">
            Repositórios de{" "}
            <strong className="text-ink">{orgName}</strong> disponíveis para
            análise.
          </p>
        </div>
        <div className="flex gap-3 shrink-0">
          <button
            ref={addBtnRef}
            type="button"
            onClick={handleOpenModal}
            className="inline-flex items-center justify-center gap-2 px-5 py-2.5 rounded-md bg-saint text-white text-sm font-semibold hover:bg-[#0c3d2e] transition"
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
              ? "mt-6 bg-saint-bg border border-saint/20 text-saint text-sm rounded-md px-4 py-3"
              : "mt-6 bg-neutral-50 border border-neutral-200 text-neutral-700 text-sm rounded-md px-4 py-3"
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
    </section>
  );
}
