"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useEffect } from "react";
import { Header } from "@/components/Header";
import { Footer } from "@/components/Footer";
import { SkipLink } from "@/components/SkipLink";
import { useAuth } from "@/lib/auth";

export default function DashboardPage() {
  const router = useRouter();
  const { user, currentOrg, memberships, isLoading, isAuthenticated, networkError } =
    useAuth();

  // Auth gate — anonymous users get bounced to /login.
  useEffect(() => {
    if (!isLoading && !isAuthenticated && !networkError) {
      router.replace("/login");
    }
  }, [isAuthenticated, isLoading, networkError, router]);

  return (
    <>
      <SkipLink />
      <Header />
      <main id="main" className="px-6 py-12 md:py-16">
        {isLoading && <LoadingSkeleton />}
        {!isLoading && networkError && <NetworkErrorState />}
        {!isLoading && !networkError && isAuthenticated && user && (
          <ReadyState
            displayName={user.displayName ?? user.email ?? "Usuário"}
            orgName={currentOrg?.name ?? "sem organização"}
            currentOrgId={currentOrg?.id}
            memberships={memberships}
          />
        )}
      </main>
      <Footer />
    </>
  );
}

function LoadingSkeleton() {
  return (
    <section className="max-w-3xl mx-auto">
      <div className="bg-white border border-neutral-200 rounded-xl p-8 shadow-sm">
        <div className="lt-skeleton h-7 w-1/2 mb-4" />
        <div className="lt-skeleton h-4 w-2/3 mb-2" />
        <div className="lt-skeleton h-4 w-1/3" />
      </div>
    </section>
  );
}

function NetworkErrorState() {
  return (
    <section className="max-w-3xl mx-auto">
      <div className="bg-sinner-bg border border-sinner/20 rounded-xl p-6 md:p-8">
        <h1 className="text-2xl font-bold tracking-tight text-sinner">
          Backend indisponível
        </h1>
        <p className="mt-3 text-neutral-700 text-sm">
          Não conseguimos contatar o servidor para verificar sua sessão.
        </p>
        <p className="mt-3 text-neutral-700 text-sm">
          Em desenvolvimento, suba o backend com:
        </p>
        <pre className="mt-2 bg-ink text-paper rounded-md p-3 text-xs font-mono overflow-x-auto">
          <code>dotnet run --project engine/src/Lintty.WebInspector</code>
        </pre>
        <p className="mt-3 text-neutral-700 text-sm">
          Em produção, tente recarregar em alguns segundos.
        </p>
        <div className="mt-6 flex flex-wrap gap-3">
          <button
            type="button"
            onClick={() => window.location.reload()}
            className="inline-flex items-center gap-2 px-4 py-2 rounded-md bg-ink text-white text-sm font-semibold hover:bg-neutral-800 transition"
          >
            Tentar novamente
          </button>
          <Link
            href="/inspect"
            className="inline-flex items-center gap-2 px-4 py-2 rounded-md border border-neutral-300 text-neutral-800 text-sm font-medium hover:bg-neutral-100 transition"
          >
            Web Inspector anônimo
          </Link>
          <Link
            href="/cli"
            className="inline-flex items-center gap-2 px-4 py-2 rounded-md border border-neutral-300 text-neutral-800 text-sm font-medium hover:bg-neutral-100 transition"
          >
            Baixar CLI
          </Link>
        </div>
      </div>
    </section>
  );
}

interface ReadyStateProps {
  displayName: string;
  orgName: string;
  currentOrgId?: string;
  memberships: Array<{ org?: { id?: string; name?: string }; role?: string; name?: string }>;
}

function ReadyState({
  displayName,
  orgName,
  currentOrgId,
  memberships,
}: ReadyStateProps) {
  const firstName = String(displayName).split(/\s+/)[0] || displayName;
  const showMemberships = memberships.length > 1;

  return (
    <section className="max-w-3xl mx-auto">
      <p className="text-xs font-semibold tracking-widest text-neutral-500 uppercase mb-3">
        Dashboard &middot; Beta
      </p>
      <h1 className="text-3xl md:text-4xl font-bold tracking-tight">
        Olá, <span>{firstName}</span>.
      </h1>
      <p className="mt-3 text-neutral-700">
        Você está em <strong className="text-ink">{orgName}</strong>.
      </p>

      <article className="mt-10 bg-white border border-neutral-200 rounded-xl p-6 md:p-8 shadow-sm">
        <p className="text-xs font-semibold tracking-widest text-saint uppercase">Roadmap</p>
        <h2 className="mt-2 text-xl font-bold tracking-tight">Em construção — Sprint 3</h2>
        <p className="mt-3 text-neutral-700 text-sm leading-relaxed">
          Este dashboard ainda é um placeholder. As próximas entregas incluem:
        </p>
        <ul className="mt-4 space-y-2 text-sm text-neutral-700">
          <li className="flex gap-3">
            <span className="text-saint mt-0.5" aria-hidden="true">→</span>
            <span>
              Scans org-bound — laudos vinculados à sua organização, com membros vendo o
              histórico compartilhado.
            </span>
          </li>
          <li className="flex gap-3">
            <span className="text-saint mt-0.5" aria-hidden="true">→</span>
            <span>
              Gestão de repositórios — conecte o GitHub via OAuth e dispare scans direto
              pela UI.
            </span>
          </li>
          <li className="flex gap-3">
            <span className="text-saint mt-0.5" aria-hidden="true">→</span>
            <span>
              Histórico de laudos — todos os PDFs anteriores acessíveis com hash auditável.
            </span>
          </li>
          <li className="flex gap-3">
            <span className="text-saint mt-0.5" aria-hidden="true">→</span>
            <span>Convite de membros e roles (owner, admin, member, viewer).</span>
          </li>
        </ul>

        <div className="mt-8 border-t border-neutral-200 pt-6">
          <p className="text-sm font-semibold text-ink">Por enquanto, você pode:</p>
          <div className="mt-4 grid sm:grid-cols-2 gap-3">
            <Link
              href="/inspect"
              className="block bg-paper border border-neutral-200 rounded-lg p-4 hover:border-saint hover:bg-saint-bg transition"
            >
              <p className="font-semibold text-ink">Web Inspector anônimo</p>
              <p className="mt-1 text-xs text-neutral-600">
                Cole uma URL pública do GitHub e receba o laudo PDF.
              </p>
            </Link>
            <Link
              href="/cli"
              className="block bg-paper border border-neutral-200 rounded-lg p-4 hover:border-saint hover:bg-saint-bg transition"
            >
              <p className="font-semibold text-ink">CLI local</p>
              <p className="mt-1 text-xs text-neutral-600">
                Roda no seu equipamento. Código nunca sai da sua máquina.
              </p>
            </Link>
          </div>
        </div>
      </article>

      {showMemberships && (
        <article className="mt-6 bg-white border border-neutral-200 rounded-xl p-6 md:p-8 shadow-sm">
          <p className="text-xs font-semibold tracking-widest text-neutral-500 uppercase">
            Suas organizações
          </p>
          <ul className="mt-3 divide-y divide-neutral-100">
            {memberships.map((m, idx) => {
              const mOrgName = m.org?.name ?? m.name ?? "—";
              const role = m.role ?? "member";
              const isCurrent = m.org?.id === currentOrgId;
              return (
                <li
                  key={(m.org?.id ?? mOrgName) + idx}
                  className="flex items-center justify-between py-2"
                >
                  <div>
                    <p className="text-sm font-semibold text-ink">
                      {mOrgName}
                      {isCurrent && (
                        <span className="ml-1 text-xs text-saint">(ativa)</span>
                      )}
                    </p>
                    <p className="text-xs text-neutral-500">{role}</p>
                  </div>
                </li>
              );
            })}
          </ul>
          <p className="mt-4 text-xs text-neutral-500">
            Trocar de organização ativa entra no Sprint 3.
          </p>
        </article>
      )}
    </section>
  );
}
