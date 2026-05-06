import type { Metadata } from "next";

export const metadata: Metadata = {
  title: "Lintty Web Inspector — Cole a URL do GitHub, receba o laudo",
  description:
    "Cole uma URL pública do GitHub e o Lintty Web Inspector roda o motor determinístico no nosso backend, devolve o laudo PDF e descarta o clone em até 60 segundos.",
  alternates: { canonical: "https://lintty.com/inspect" },
  openGraph: {
    title: "Lintty Web Inspector — Cole a URL do GitHub, receba o laudo",
    description: "Mesmo motor determinístico do CLI. Clone efêmero, PDF em minutos.",
    url: "https://lintty.com/inspect",
    type: "website",
    images: [{ url: "/assets/og-image.svg" }],
  },
  twitter: { card: "summary_large_image" },
};

export default function InspectLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  return <>{children}</>;
}
