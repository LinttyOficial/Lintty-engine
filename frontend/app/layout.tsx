import type { Metadata, Viewport } from "next";
import { Inter } from "next/font/google";
import "./globals.css";
import { AuthProvider } from "@/lib/auth";

const inter = Inter({
  subsets: ["latin"],
  weight: ["400", "500", "600", "700"],
  display: "swap",
  variable: "--font-inter",
});

export const metadata: Metadata = {
  metadataBase: new URL("https://lintty.com"),
  title: "Lintty — Arquitetura como evidência",
  description:
    "Lintty analisa solutions .NET com Roslyn type-aware e emite um laudo PDF determinístico que vale como evidência de aceite — sem mediação humana, sem opinião subjetiva.",
  icons: {
    icon: "/assets/lintty-icon.png",
  },
};

export const viewport: Viewport = {
  themeColor: "#0f4c3a",
  width: "device-width",
  initialScale: 1,
};

export default function RootLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  return (
    <html lang="pt-BR" className={inter.variable}>
      <body className="bg-paper text-ink antialiased">
        <AuthProvider>{children}</AuthProvider>
      </body>
    </html>
  );
}
