"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { Suspense, useEffect } from "react";
import { Header } from "@/components/Header";
import { Footer } from "@/components/Footer";
import { SkipLink } from "@/components/SkipLink";
import { AuroraPageHeader } from "@/components/HeroAurora";
import { useAuth } from "@/lib/auth";
import { GitHubConnectProvider } from "@/lib/github-connect";
import { ReposSection } from "./components/ReposSection";
import { GitHubConnectBadge } from "./components/GitHubConnectBadge";
import { GitHubConnectCallback } from "./components/GitHubConnectCallback";

export default function DashboardPage() {
  const router = useRouter();
  const { currentOrg, isLoading, isAuthenticated, networkError } = useAuth();

  // Auth gate — anonymous users get bounced to /login.
  useEffect(() => {
    if (!isLoading && !isAuthenticated && !networkError) {
      router.replace("/login");
    }
  }, [isAuthenticated, isLoading, networkError, router]);

  const orgLabel = currentOrg?.name ?? "sua organização";

  return (
    <>
      <SkipLink />
      <Header />
      <AuroraPageHeader
        eyebrow="Workspace"
        title="Dashboard"
        subtitle={
          isAuthenticated && !isLoading
            ? `Repositórios e laudos da organização ${orgLabel}.`
            : "Repositórios e laudos da sua organização."
        }
      />
      <main id="main" className="lt-dark-glow lt-noise px-6 py-12 md:py-16 text-paper">
        {isLoading && <LoadingSkeleton />}
        {!isLoading && networkError && <NetworkErrorState />}
        {!isLoading && !networkError && isAuthenticated && (
          <GitHubConnectProvider>
            <section className="max-w-3xl mx-auto">
              {/* Suspense boundary required by Next.js 15 for any client
                  component using useSearchParams() under static export. */}
              <Suspense fallback={null}>
                <GitHubConnectCallback />
              </Suspense>
            </section>
            <ReposSection orgName={currentOrg?.name ?? "sua organização"} />
            <section className="max-w-3xl mx-auto">
              <GitHubConnectBadge />
            </section>
          </GitHubConnectProvider>
        )}
      </main>
      <Footer />
    </>
  );
}

function LoadingSkeleton() {
  return (
    <section className="max-w-3xl mx-auto">
      <div className="lt-card-form p-8">
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
      <div className="lt-card-form p-6 md:p-8" style={{ borderColor: "rgba(220, 38, 38, 0.35)" }}>
        <h1 className="text-2xl font-bold tracking-tight text-red-300">
          Backend indisponível
        </h1>
        <p className="mt-3 text-neutral-300 text-sm">
          Não conseguimos contatar o servidor para verificar sua sessão.
        </p>
        <p className="mt-3 text-neutral-300 text-sm">
          Em desenvolvimento, suba o backend com:
        </p>
        <pre className="mt-2 bg-black/50 border border-neutral-800/60 text-paper rounded-md p-3 text-xs font-mono overflow-x-auto">
          <code>dotnet run --project engine/src/Lintty.WebInspector</code>
        </pre>
        <p className="mt-3 text-neutral-300 text-sm">
          Em produção, tente recarregar em alguns segundos.
        </p>
        <div className="mt-6 flex flex-wrap gap-3">
          <button
            type="button"
            onClick={() => window.location.reload()}
            className="lt-btn-primary text-sm"
          >
            Tentar novamente
          </button>
          <Link
            href="/inspect"
            className="lt-btn-secondary text-sm"
          >
            Web Inspector anônimo
          </Link>
          <Link
            href="/cli"
            className="lt-btn-secondary text-sm"
          >
            Baixar CLI
          </Link>
        </div>
      </div>
    </section>
  );
}
