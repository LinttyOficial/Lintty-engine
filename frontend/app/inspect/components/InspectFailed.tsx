"use client";

/**
 * Estado D do Web Inspector — falha em qualquer estágio do job.
 *
 * Cada `error_code` mapeia para um título humano + mensagem específica.
 * **Toda mensagem de erro tem CTA de fallback para o CLI local** —
 * princípio editorial declarado no spec do agente frontend-dev.
 */

import Link from "next/link";

const ERROR_TITLES: Record<string, string> = {
  no_target: "Não foi possível identificar o que analisar",
  ambiguous_target: "Múltiplos alvos encontrados no repositório",
  target_not_found: "Alvo declarado em lintty.yml não existe",
  invalid_config: "lintty.yml inválido",
  no_sln: "Solution não encontrada",
  clone_failed: "Falha ao clonar o repositório",
  compile_failed: "Falha de compilação (dotnet build)",
  layer_tagging_error: "Não conseguimos classificar os projetos",
  timeout: "Tempo limite excedido (15 min)",
  internal_error: "Erro interno",
};

function buildErrorMessage(code: string, detail: string | null): string {
  switch (code) {
    case "no_target":
      return (
        "Não encontramos `.sln`, `.csproj` único, ou `lintty.yml` com `projects:` na raiz. " +
        "Adicione um `lintty.yml` ou rode o CLI localmente: " +
        "`lintty-engine analyze --target <path>`"
      );
    case "ambiguous_target":
      return (
        "Encontramos múltiplas `.sln` ou `.csproj` no repo. " +
        "Adicione um `lintty.yml` com `projects:` na raiz para declarar o escopo. " +
        "Ou rode o CLI localmente."
      );
    case "target_not_found":
      return (
        "Um caminho declarado em `projects:` não existe no repo. " +
        "Verifique o `lintty.yml`. Ou rode o CLI localmente."
      );
    case "invalid_config":
      return "`lintty.yml` inválido. Verifique o schema. Ou rode o CLI localmente.";
    case "no_sln":
      return (
        "Não encontramos uma `.sln` na raiz. Adicione um `lintty.yml` com `projects:` " +
        "ou rode o CLI localmente."
      );
    case "clone_failed":
      return (
        "Falha ao clonar o repositório. Verifique se a URL e o branch estão corretos." +
        (detail ? ` Mensagem do git: ${detail}` : "")
      );
    case "compile_failed":
      return (
        "Falha no `dotnet build`. Configure `nuget.config` se o repo precisa de NuGet " +
        "privado, ou use o CLI local." +
        (detail ? ` Detalhe: ${detail}` : "")
      );
    case "layer_tagging_error":
      return (
        "Não conseguimos classificar os projetos. Adicione um `lintty.yml` na raiz com " +
        "`explicit_map`."
      );
    case "timeout":
      return (
        "Análise excedeu 15 min. Para projetos maiores, use o CLI local sem limite de tempo."
      );
    case "internal_error":
    default:
      return (
        "Erro interno. Tente novamente ou use o CLI local." +
        (detail ? ` Detalhe: ${detail}` : "")
      );
  }
}

interface InspectFailedProps {
  errorCode: string;
  errorDetail: string | null;
  onRetry: () => void;
}

export function InspectFailed({
  errorCode,
  errorDetail,
  onRetry,
}: InspectFailedProps) {
  const title = ERROR_TITLES[errorCode] ?? ERROR_TITLES.internal_error;
  const message = buildErrorMessage(errorCode, errorDetail);
  const showRawDetail =
    errorDetail !== null &&
    errorDetail !== "" &&
    (errorCode === "compile_failed" ||
      errorCode === "clone_failed" ||
      errorCode === "internal_error");

  return (
    <section className="mt-10" aria-labelledby="failed-heading">
      <div className="bg-white border border-sinner/20 rounded-xl p-6 md:p-8 shadow-sm">
        <p className="text-xs font-semibold tracking-widest text-sinner uppercase">
          Falha na análise
        </p>
        <h2 id="failed-heading" className="mt-2 text-2xl font-bold tracking-tight">
          <span>{title}</span>
        </h2>
        <p className="mt-3 text-sm text-neutral-700">{message}</p>
        {showRawDetail && (
          <p className="mt-3 text-xs font-mono text-neutral-500 bg-neutral-50 border border-neutral-200 rounded px-3 py-2 whitespace-pre-wrap break-words">
            {errorDetail}
          </p>
        )}

        <div className="mt-6 bg-saint-bg border border-saint/20 rounded-md px-4 py-3 text-sm text-saint">
          <strong>Fallback recomendado:</strong> rode o motor localmente, sem limite de
          tempo e sem expor o repositório.{" "}
          <Link href="/cli" className="font-semibold underline">
            Baixar CLI local →
          </Link>
        </div>

        <div className="mt-6 flex flex-wrap gap-3">
          <button
            type="button"
            onClick={onRetry}
            className="inline-flex items-center gap-2 px-6 py-3 rounded-md bg-ink text-white font-semibold hover:bg-neutral-800 transition"
          >
            Tentar novamente
          </button>
          <Link
            href="/cli"
            className="inline-flex items-center gap-2 px-6 py-3 rounded-md border border-neutral-300 text-neutral-800 font-medium hover:bg-neutral-100 transition"
          >
            Baixar CLI local
          </Link>
        </div>
      </div>
    </section>
  );
}
