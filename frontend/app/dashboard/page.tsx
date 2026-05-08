"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useEffect } from "react";
import { Header } from "@/components/Header";
import { Footer } from "@/components/Footer";
import { SkipLink } from "@/components/SkipLink";
import { useAuth } from "@/lib/auth";
import { ReposSection } from "./components/ReposSection";

export default function DashboardPage() {
  const router = useRouter();
  const { currentOrg, isLoading, isAuthenticated, networkError } = useAuth();

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
        {!isLoading && !networkError && isAuthenticated && (
          <ReposSection orgName={currentOrg?.name ?? "sua organização"} />
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
