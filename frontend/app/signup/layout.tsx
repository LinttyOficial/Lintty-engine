import type { Metadata } from "next";

export const metadata: Metadata = {
  title: "Criar conta — Lintty",
  description:
    "Crie sua conta Lintty. Tenha laudos byte-determinísticos da sua equipe inteira, com gestão multi-tenant.",
  alternates: { canonical: "https://lintty.com/signup" },
  openGraph: {
    title: "Criar conta — Lintty",
    description: "Tenha laudos byte-determinísticos da sua equipe inteira.",
    url: "https://lintty.com/signup",
    type: "website",
    images: [{ url: "/assets/og-image.svg" }],
  },
  twitter: { card: "summary_large_image" },
};

export default function SignupLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  return <>{children}</>;
}
