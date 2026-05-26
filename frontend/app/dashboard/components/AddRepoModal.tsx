"use client";

/**
 * Modal "Adicionar repositório" — input único de URL do GitHub que chama
 * `addRepoManual({ githubUrl })` no backend.
 *
 * Acessibilidade:
 *   - `role="dialog" aria-modal="true"` num <div> sobreposto (escolha em
 *     vez de <dialog> nativo: o backdrop click-to-close + scroll lock são
 *     mais previsíveis com div + nossa CSS de `.lt-modal-backdrop`, que
 *     já existe no projeto desde a tela de login).
 *   - Foco entra no input quando o modal abre.
 *   - Foco volta para o trigger quando fecha (via `triggerRef` injetado
 *     pelo pai).
 *   - ESC fecha.
 *   - Click no backdrop fecha. Click dentro do card não propaga.
 *
 * Idempotência: o backend trata POST /api/repos como idempotente em
 * (org, github_url). Portanto 201 (criado) e 409 (já existe) são ambos
 * "sucesso" do ponto de vista do usuário — fechamos o modal e damos
 * refresh na lista. A mensagem de toast diferencia os dois casos.
 */

import { useEffect, useRef, useState, type FormEvent } from "react";
import { addRepoManual, parseApiError } from "@/lib/dashboard-api";

const GITHUB_URL_RE = /^https?:\/\/github\.com\/[^/\s]+\/[^/\s]+\/?$/i;

export type AddRepoOutcome =
  | { kind: "created"; githubUrl: string }
  | { kind: "already_existed"; githubUrl: string };

interface AddRepoModalProps {
  open: boolean;
  onClose: () => void;
  onSuccess: (outcome: AddRepoOutcome) => void;
  /** Reaberto a foco quando o modal fecha. */
  triggerRef: React.RefObject<HTMLButtonElement | null>;
}

export function AddRepoModal({
  open,
  onClose,
  onSuccess,
  triggerRef,
}: AddRepoModalProps) {
  const [githubUrl, setGithubUrl] = useState("");
  const [urlError, setUrlError] = useState<string | null>(null);
  const [submitError, setSubmitError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  const inputRef = useRef<HTMLInputElement>(null);
  const cardRef = useRef<HTMLDivElement>(null);

  // ESC fecha; foco entra no input ao abrir; foco volta para o trigger no
  // unmount/close. Limpa o estado ao fechar para a próxima abertura ficar
  // virgem.
  useEffect(() => {
    if (!open) return undefined;

    setUrlError(null);
    setSubmitError(null);
    setSubmitting(false);
    setGithubUrl("");

    // delay 1 frame para o input já estar no DOM
    const id = window.setTimeout(() => inputRef.current?.focus(), 0);

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
    // devolve foco ao botão que abriu
    window.setTimeout(() => triggerRef.current?.focus(), 0);
  }

  async function handleSubmit(ev: FormEvent<HTMLFormElement>) {
    ev.preventDefault();
    setUrlError(null);
    setSubmitError(null);

    const trimmed = githubUrl.trim();
    if (!trimmed) {
      setUrlError("Informe a URL do repositório.");
      return;
    }
    if (!GITHUB_URL_RE.test(trimmed)) {
      setUrlError("URL inválida. Use https://github.com/owner/repo.");
      return;
    }

    setSubmitting(true);
    try {
      const res = await addRepoManual({ githubUrl: trimmed });

      if (res.status === 201 && res.body) {
        onSuccess({ kind: "created", githubUrl: trimmed });
        handleClose();
        return;
      }

      // 409 — backend é idempotente em (org, github_url). Tratamos como
      // sucesso silencioso: o repo já estava na lista da org, recarregar
      // basta. Mostramos uma mensagem de toast diferente para o usuário
      // saber que não foi um cadastro novo.
      if (res.status === 409) {
        onSuccess({ kind: "already_existed", githubUrl: trimmed });
        handleClose();
        return;
      }

      const apiErr = parseApiError(res);
      if (res.status === 400 && apiErr) {
        // erros de validação do servidor — mostra inline no campo
        setUrlError(apiErr.message);
        return;
      }
      setSubmitError(
        apiErr?.message ?? `Erro ${res.status}. Tente novamente.`,
      );
    } catch (err) {
      console.error("POST /api/repos falhou", err);
      setSubmitError(
        "Não foi possível contatar o servidor. Verifique sua conexão e tente novamente.",
      );
    } finally {
      setSubmitting(false);
    }
  }

  if (!open) return null;

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
        aria-labelledby="add-repo-title"
        className="lt-modal"
        onClick={(ev) => ev.stopPropagation()}
      >
        <div className="flex items-start justify-between gap-4">
          <h2
            id="add-repo-title"
            className="text-lg font-bold tracking-tight text-paper"
          >
            Adicionar repositório
          </h2>
          <button
            type="button"
            onClick={handleClose}
            className="text-neutral-400 hover:text-paper transition -mt-1 -mr-1 p-1"
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
        <p className="mt-2 text-sm text-neutral-400">
          Cole a URL do repositório no GitHub. Você poderá disparar scans
          depois pela tela do repositório.
        </p>

        <form
          noValidate
          onSubmit={handleSubmit}
          className="mt-5 space-y-4"
        >
          <div>
            <label
              htmlFor="add-repo-url"
              className="block text-sm font-semibold mb-2 text-paper"
            >
              URL do GitHub{" "}
              <span className="text-red-300" aria-hidden="true">
                *
              </span>
            </label>
            <input
              ref={inputRef}
              id="add-repo-url"
              name="githubUrl"
              type="url"
              required
              autoComplete="off"
              placeholder="https://github.com/owner/repo"
              value={githubUrl}
              onChange={(e) => {
                setGithubUrl(e.target.value);
                setUrlError(null);
                setSubmitError(null);
              }}
              aria-invalid={urlError ? "true" : undefined}
              aria-describedby={urlError ? "add-repo-url-err" : undefined}
              className="lt-input font-mono"
            />
            {urlError && (
              <p
                id="add-repo-url-err"
                className="field-error"
                role="alert"
                aria-live="polite"
              >
                {urlError}
              </p>
            )}
          </div>

          {submitError && (
            <div
              className="lt-alert-danger"
              role="alert"
              aria-live="polite"
            >
              {submitError}
            </div>
          )}

          <div className="flex flex-col-reverse sm:flex-row sm:justify-end gap-2 pt-2">
            <button
              type="button"
              onClick={handleClose}
              disabled={submitting}
              className="lt-btn-secondary text-sm px-4 py-2 disabled:opacity-50"
            >
              Cancelar
            </button>
            <button
              type="submit"
              disabled={submitting}
              className="lt-btn-primary text-sm px-5 py-2"
            >
              {submitting ? "Adicionando..." : "Adicionar"}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}
