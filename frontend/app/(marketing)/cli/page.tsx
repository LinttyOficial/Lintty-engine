import type { Metadata } from "next";
import Link from "next/link";
import { AuroraPageHeader } from "@/components/HeroAurora";

export const metadata: Metadata = {
  title: "Baixar CLI Lintty — Análise determinística no seu equipamento",
  description:
    "CLI Lintty: motor Roslyn determinístico empacotado como binário único. Distribuição via GitHub Releases em finalização. Enquanto isso, use o Web Inspector ou o Concierge.",
  alternates: { canonical: "https://lintty.com/cli" },
  openGraph: {
    title: "Baixar CLI Lintty",
    description:
      "Motor determinístico no seu equipamento. Código nunca sai da sua máquina.",
    url: "https://lintty.com/cli",
    type: "website",
    images: [{ url: "/assets/og-image.svg" }],
  },
  twitter: { card: "summary_large_image" },
};

export default function CliPage() {
  return (
    <>
      <AuroraPageHeader
        eyebrow="Distribuição · V0"
        title={
          <>
            CLI Lintty &mdash; análise determinística no seu equipamento.
          </>
        }
        subtitle={
          <>
            Binário único, sem instalar SDK. Mesmo motor Roslyn type-aware do Web Inspector,
            rodando local. <strong className="text-paper">O código nunca sai da sua máquina.</strong>
          </>
        }
      />
      <main id="main" className="lt-dark-glow lt-noise px-6 py-12 md:py-16 text-paper">
        <div className="max-w-3xl mx-auto">
          {/* Em construção */}
        <section aria-labelledby="em-construcao" className="mt-10">
          <div className="lt-card-soft rounded-xl bg-neutral-900/40 p-6 md:p-8">
            <div className="flex items-center gap-3 mb-3">
              <span className="badge badge-neutral">Em construção</span>
              <span className="text-xs font-mono text-neutral-500">v0.x</span>
            </div>
            <h2 id="em-construcao" className="text-xl font-bold tracking-tight text-paper">
              Distribuição via GitHub Releases em finalização
            </h2>
            <p className="mt-3 text-sm text-neutral-300 leading-relaxed">
              O CLI será publicado no repositório{" "}
              <code className="font-mono text-xs bg-white/5 text-paper px-1.5 py-0.5 rounded">
                lintty/lintty-engine
              </code>{" "}
              em GitHub Releases assinadas, com sha256 publicado para validação. Estamos
              finalizando a esteira de release (build reprodutível, smoke tests por target,
              e o repositório público). Não publicamos um link de download antes disso porque{" "}
              <strong className="text-paper">o argumento do produto é determinismo</strong> &mdash; e isso começa
              por você baixar exatamente o binário que esperamos que você baixe.
            </p>
            <p className="mt-3 text-sm text-neutral-300 leading-relaxed">
              Quer ser avisado quando sair? Mande um email rápido para{" "}
              <a
                href="mailto:contato@lintty.com?subject=Avise-me%20sobre%20o%20CLI%20Lintty"
                className="underline decoration-emerald-400/50 underline-offset-2 font-medium text-emerald-200 hover:text-emerald-100"
              >
                contato@lintty.com
              </a>{" "}
              com o assunto &ldquo;Avise-me sobre o CLI&rdquo;. Sem mailing list, sem
              tracking.
            </p>
          </div>
        </section>

        {/* Especificação técnica */}
        <section aria-labelledby="spec-tecnica" className="mt-10">
          <h2 id="spec-tecnica" className="text-xl font-bold tracking-tight text-paper">
            Especificação técnica
          </h2>
          <p className="mt-3 text-sm text-neutral-300">
            O CLI é um binário <strong className="text-paper">self-contained</strong> publicado por target,
            gerado com{" "}
            <code className="font-mono text-xs bg-white/5 text-paper px-1.5 py-0.5 rounded">
              dotnet publish --self-contained -p:PublishSingleFile=true
            </code>
            . Não exige .NET SDK instalado na máquina alvo.
          </p>

          <div className="mt-5 lt-card-soft rounded-xl bg-neutral-900/40 overflow-x-auto">
            <table className="targets w-full">
              <thead>
                <tr>
                  <th>Plataforma</th>
                  <th>RID</th>
                  <th>Tamanho aprox.</th>
                  <th>Status</th>
                </tr>
              </thead>
              <tbody>
                <tr>
                  <td>Windows x64</td>
                  <td className="mono">win-x64</td>
                  <td>~95 MB</td>
                  <td>
                    <span className="badge badge-saint">P0</span>
                  </td>
                </tr>
                <tr>
                  <td>Linux x64</td>
                  <td className="mono">linux-x64</td>
                  <td>~90 MB</td>
                  <td>
                    <span className="badge badge-saint">P0</span>
                  </td>
                </tr>
                <tr>
                  <td>macOS Intel</td>
                  <td className="mono">osx-x64</td>
                  <td>~100 MB</td>
                  <td>
                    <span className="badge badge-neutral">P1</span>
                  </td>
                </tr>
                <tr>
                  <td>macOS Apple Silicon</td>
                  <td className="mono">osx-arm64</td>
                  <td>~95 MB</td>
                  <td>
                    <span className="badge badge-neutral">P1</span>
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
          <p className="mt-3 text-xs text-neutral-500">
            P0: targets bloqueantes para o lançamento da V0. P1: targets entregues na
            sequência, sem bloquear a V0.
          </p>

          <p className="mt-6 text-sm text-neutral-300">
            O comando esperado, uma vez instalado:
          </p>
          <pre
            className="snippet-dark mt-3"
            aria-label="Exemplo de invocação do CLI"
          >
            <code>
              <span className="prompt">$ </span>
              {`lintty-engine analyze \\
    --solution ./MyApp.sln \\
    --pdf laudo.pdf

# Saída padrão (stdout): JSON do laudo
# Logs e diagnóstico: stderr
# Exit codes: 0 = grade ok | 1 = grade abaixo do mínimo
#             2 = erro de execução | 3 = erro de uso`}
            </code>
          </pre>

          <p className="mt-4 text-sm text-neutral-300">
            Flags úteis:{" "}
            <code className="font-mono text-xs bg-white/5 text-paper px-1.5 py-0.5 rounded">
              --canon-version 1.0
            </code>
            ,{" "}
            <code className="font-mono text-xs bg-white/5 text-paper px-1.5 py-0.5 rounded">
              --fail-on-grade D
            </code>
            ,{" "}
            <code className="font-mono text-xs bg-white/5 text-paper px-1.5 py-0.5 rounded">
              --output pretty
            </code>
            . Spec completa entra junto da release no{" "}
            <code className="font-mono text-xs text-paper">--help</code>.
          </p>
        </section>

        {/* Caminhos disponíveis hoje */}
        <section aria-labelledby="caminhos-hoje" className="mt-12">
          <h2 id="caminhos-hoje" className="text-xl font-bold tracking-tight text-paper">
            Caminhos disponíveis hoje
          </h2>
          <p className="mt-3 text-sm text-neutral-300">
            Enquanto a release pública não sai, dois caminhos rodam o mesmo motor e devolvem
            o mesmo PDF:
          </p>

          <div className="mt-6 grid md:grid-cols-2 gap-5">
            <article className="lt-card-saint rounded-xl bg-neutral-900/40 p-6">
              <p className="text-xs font-semibold tracking-widest text-emerald-300/80 uppercase mb-2">
                Web Inspector
              </p>
              <h3 className="font-semibold text-lg text-paper">
                Cole a URL do GitHub e receba o PDF agora.
              </h3>
              <p className="mt-3 text-sm text-neutral-300">
                Mesmo motor. Mesmo Canon. Mesmo PDF. Clone efêmero no nosso backend,
                descartado em até 60 segundos. Limite de 3 jobs por dia por IP no V0.
              </p>
              <p className="mt-3 text-xs text-neutral-500">
                Útil para repositórios públicos e validações rápidas.
              </p>
              <Link
                href="/inspect"
                className="mt-5 inline-flex items-center gap-2 px-5 py-2.5 rounded-md bg-saint text-white text-sm font-semibold hover:bg-emerald-600 transition shadow-lg shadow-emerald-900/40"
              >
                Abrir Web Inspector
                <span aria-hidden="true">&rarr;</span>
              </Link>
            </article>

            <article className="lt-card-soft rounded-xl bg-neutral-900/40 p-6">
              <p className="text-xs font-semibold tracking-widest text-neutral-400 uppercase mb-2">
                Concierge
              </p>
              <h3 className="font-semibold text-lg text-paper">
                Mande sua <span className="font-mono text-base">.sln</span> por email.
              </h3>
              <p className="mt-3 text-sm text-neutral-300">
                Rodamos no nosso lado, devolvemos o laudo PDF. Útil para repos privados ou
                monorepos grandes enquanto o CLI público não sai. NDA padrão sob demanda.
              </p>
              <p className="mt-3 text-xs text-neutral-500">
                Resposta em até 1 dia útil. Sem custo durante o V0 piloto.
              </p>
              <a
                href="mailto:contato@lintty.com?subject=Concierge%20Lintty&body=Empresa:%0AContato:%0ARepo%20(zip%20ou%20link%20privado):%0ABranch%2Fcommit:%0A"
                className="mt-5 lt-btn-secondary"
              >
                Solicitar Concierge
                <span aria-hidden="true">&rarr;</span>
              </a>
            </article>
          </div>

          <p className="mt-8 text-xs text-neutral-500 leading-relaxed border-t border-neutral-800/60 pt-5">
            <strong className="text-neutral-300">Quando o CLI sair</strong>, este endereço (
            <code className="font-mono text-neutral-300">/cli</code>) hospeda o link de download por target e
            o sha256 oficial para validação. Sem dependência externa: o binário fica no
            GitHub Releases, o hash fica nesta página e no release notes. Você confere o
            hash localmente (<code className="font-mono text-neutral-300">sha256sum</code> em Linux/macOS,{" "}
            <code className="font-mono text-neutral-300">Get-FileHash -Algorithm SHA256</code> em Windows) e
            roda.
          </p>
          </section>
        </div>
      </main>
    </>
  );
}
