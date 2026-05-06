import type { Metadata } from "next";

export const metadata: Metadata = {
  title: "Dashboard — Lintty",
  description:
    "Dashboard Lintty — em construção. Sprint 3 traz scans org-bound, gestão de repositórios e histórico de laudos.",
  // dashboard é área autenticada, não indexa em busca
  robots: { index: false, follow: false },
};

export default function DashboardLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  return <>{children}</>;
}
