import type { Metadata } from "next";

export const metadata: Metadata = {
  title: "Entrar — Lintty",
  description:
    "Entre na sua conta Lintty. Dashboard multi-tenant para auditoria determinística de entregas .NET.",
  alternates: { canonical: "https://lintty.com/login" },
  openGraph: {
    title: "Entrar — Lintty",
    description: "Acesse seu dashboard Lintty.",
    url: "https://lintty.com/login",
    type: "website",
    images: [{ url: "/assets/og-image.svg" }],
  },
  twitter: { card: "summary_large_image" },
};

export default function LoginLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  return <>{children}</>;
}
