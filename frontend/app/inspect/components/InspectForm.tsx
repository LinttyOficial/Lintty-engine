"use client";

/**
 * Estado A do Web Inspector — formulário para o usuário colar a URL do
 * GitHub. Replica o `<section data-state="form">` do legacy `inspect.html`
 * sem mudar copy, validação ou autocomplete.
 *
 * Submit dispara o callback `onSubmit` que recebe o payload já validado;
 * o pai (`InspectClient`) é quem fala com o backend e troca de estado.
 */

import Link from "next/link";
import { useState, type FormEvent } from "react";

const GITHUB_URL_RE = /^https?:\/\/github\.com\/[^/\s]+\/[^/\s]+\/?$/i;

export interface InspectFormPayload {
  github_url: string;
  ref?: string;
  solution_path?: string;
  github_token?: string;
}

interface InspectFormProps {
  /** Mensagem de erro global vinda do POST. Limpa quando usuário digita. */
  globalError: string | null;
  /** True enquanto a request está em voo. */
  submitting: boolean;
  /** Erro específico do campo URL (ex: 400 invalid_url). */
  urlError: string | null;
  /** Chamado após validação client-side. */
  onSubmit: (payload: InspectFormPayload) => void;
  /** Limpa erros do pai quando usuário começa a digitar de novo. */
  onClearErrors: () => void;
}

export function InspectForm({
  globalError,
  submitting,
  urlError,
  onSubmit,
  onClearErrors,
}: InspectFormProps) {
  const [githubUrl, setGithubUrl] = useState("");
  const [ref, setRef] = useState("");
  const [solutionPath, setSolutionPath] = useState("");
  const [githubToken, setGithubToken] = useState("");
  const [localUrlError, setLocalUrlError] = useState<string | null>(null);

  const effectiveUrlError = urlError ?? localUrlError;

  function handleSubmit(ev: FormEvent<HTMLFormElement>) {
    ev.preventDefault();
    setLocalUrlError(null);
    onClearErrors();

    const trimmedUrl = githubUrl.trim();
    if (!trimmedUrl) {
      setLocalUrlError("Informe a URL do repositório.");
      return;
    }
    if (!GITHUB_URL_RE.test(trimmedUrl)) {
      setLocalUrlError("URL inválida. Use https://github.com/owner/repo.");
      return;
    }

    const payload: InspectFormPayload = { github_url: trimmedUrl };
    const trimmedRef = ref.trim();
    const trimmedSolution = solutionPath.trim();
    const trimmedToken = githubToken.trim();
    if (trimmedRef) payload.ref = trimmedRef;
    if (trimmedSolution) payload.solution_path = trimmedSolution;
    if (trimmedToken) payload.github_token = trimmedToken;

    onSubmit(payload);
  }

  return (
    <section className="mt-10" aria-labelledby="form-heading">
      <h2 id="form-heading" className="sr-only">
        Formulário de análise
      </h2>

      <form
        noValidate
        onSubmit={handleSubmit}
        className="bg-white border border-neutral-200 rounded-xl p-6 md:p-8 shadow-sm space-y-6"
      >
        {/* URL do GitHub */}
        <div>
          <label htmlFor="github_url" className="block text-sm font-semibold mb-2">
            URL do GitHub
            <span className="text-sinner" aria-hidden="true">
              {" *"}
            </span>
          </label>
          <input
            id="github_url"
            name="github_url"
            type="url"
            required
            autoComplete="off"
            placeholder="https://github.com/owner/repo"
            value={githubUrl}
            onChange={(e) => {
              setGithubUrl(e.target.value);
              setLocalUrlError(null);
              onClearErrors();
            }}
            aria-invalid={effectiveUrlError ? "true" : undefined}
            className="w-full px-4 py-3 border border-neutral-300 rounded-md font-mono text-sm bg-white focus:border-saint focus:outline-none transition"
          />
          <p className="mt-2 text-xs text-neutral-500">
            Apenas <code className="font-mono">github.com</code>. Self-hosted Enterprise /
            GitLab / Bitbucket entram em V1.
          </p>
          {effectiveUrlError && (
            <p className="field-error" role="alert" aria-live="polite">
              {effectiveUrlError}
            </p>
          )}
        </div>

        {/* Branch */}
        <div>
          <label htmlFor="ref" className="block text-sm font-semibold mb-2">
            Branch ou commit{" "}
            <span className="text-neutral-500 font-normal">(opcional)</span>
          </label>
          <input
            id="ref"
            name="ref"
            type="text"
            autoComplete="off"
            placeholder="main"
            value={ref}
            onChange={(e) => setRef(e.target.value)}
            className="w-full px-4 py-3 border border-neutral-300 rounded-md font-mono text-sm bg-white focus:border-saint focus:outline-none transition"
          />
          <p className="mt-2 text-xs text-neutral-500">
            Default: branch principal do repositório.
          </p>
        </div>

        {/* Solution path */}
        <div>
          <label htmlFor="solution_path" className="block text-sm font-semibold mb-2">
            Caminho da solution{" "}
            <span className="text-neutral-500 font-normal">(opcional)</span>
          </label>
          <input
            id="solution_path"
            name="solution_path"
            type="text"
            autoComplete="off"
            placeholder="src/MyApp.sln"
            value={solutionPath}
            onChange={(e) => setSolutionPath(e.target.value)}
            className="w-full px-4 py-3 border border-neutral-300 rounded-md font-mono text-sm bg-white focus:border-saint focus:outline-none transition"
          />
          <p className="mt-2 text-xs text-neutral-500">
            Em branco, descobrimos automaticamente:{" "}
            <code className="font-mono">.sln</code> na raiz,{" "}
            <code className="font-mono">lintty.yml</code> com{" "}
            <code className="font-mono">projects:</code>, ou um{" "}
            <code className="font-mono">.csproj</code> único.
          </p>
        </div>

        {/* Token GitHub (V0: aviso) */}
        <details className="border border-neutral-200 rounded-md">
          <summary className="cursor-pointer select-none px-4 py-3 text-sm font-medium text-neutral-700 hover:bg-neutral-50">
            Repositório privado &middot; token GitHub
          </summary>
          <div className="px-4 pb-4 pt-1 space-y-3">
            <div className="bg-sinner-bg border border-sinner/20 text-sinner text-sm rounded-md px-4 py-3">
              <strong>V0:</strong> o campo é validado mas o clone privado ainda não está
              habilitado no Web Inspector. Para repo privado, baixe o{" "}
              <Link href="/cli" className="underline font-semibold">
                CLI local
              </Link>{" "}
              — o código fica na sua máquina.
            </div>
            <div>
              <label
                htmlFor="github_token"
                className="block text-sm font-semibold mb-2"
              >
                Personal Access Token{" "}
                <span className="text-neutral-500 font-normal">
                  (opcional, V0 sem efeito)
                </span>
              </label>
              <input
                id="github_token"
                name="github_token"
                type="password"
                autoComplete="off"
                placeholder="ghp_..."
                value={githubToken}
                onChange={(e) => setGithubToken(e.target.value)}
                className="w-full px-4 py-3 border border-neutral-300 rounded-md font-mono text-sm bg-white focus:border-saint focus:outline-none transition"
              />
              <p className="mt-2 text-xs text-neutral-500">
                Use PAT com escopo <code className="font-mono">repo</code> e expiração de
                24h. Após o scan, revogue.
              </p>
            </div>
          </div>
        </details>

        {/* Erro global */}
        {globalError && (
          <div
            className="bg-sinner-bg border border-sinner/20 text-sinner text-sm rounded-md px-4 py-3"
            role="alert"
            aria-live="polite"
          >
            {globalError}
          </div>
        )}

        {/* CTA */}
        <div>
          <button
            type="submit"
            disabled={submitting}
            className="w-full inline-flex items-center justify-center gap-2 px-6 py-3 rounded-md bg-ink text-white font-semibold hover:bg-neutral-800 transition disabled:opacity-50 disabled:cursor-not-allowed"
          >
            <span>{submitting ? "Enviando..." : "Analisar arquitetura"}</span>
            <span aria-hidden="true">→</span>
          </button>
          <p className="mt-4 text-xs text-neutral-500 leading-relaxed">
            Clonamos shallow no backend, rodamos o mesmo motor do CLI, devolvemos o PDF.{" "}
            <strong>Clone descartado em até 60 segundos.</strong> PDF e JSON ficam 24h e
            expiram. Código fonte não persiste.{" "}
            <Link href="/privacidade" className="underline">
              Política de privacidade
            </Link>
            .
          </p>
        </div>
      </form>

      <p className="mt-6 text-sm text-neutral-600 text-center">
        Para uso recorrente ou repo privado,{" "}
        <Link
          href="/cli"
          className="font-semibold underline decoration-neutral-400 underline-offset-2 hover:text-ink hover:decoration-ink"
        >
          baixe o CLI →
        </Link>
      </p>
    </section>
  );
}
