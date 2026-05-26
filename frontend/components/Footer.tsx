import Link from "next/link";
import Image from "next/image";

interface FooterProps {
  /**
   * Some pages (privacidade) used a tighter `max-w-4xl`. The default
   * `max-w-6xl` matches index/cli/inspect.
   */
  variant?: "default" | "narrow";
}

export function Footer({ variant = "default" }: FooterProps) {
  const maxWidthClass =
    variant === "narrow" ? "max-w-4xl" : "max-w-6xl";

  return (
    <footer className="lt-noise bg-ink text-paper">
      <div
        className={`${maxWidthClass} mx-auto px-6 py-10 flex flex-col md:flex-row items-start md:items-center justify-between gap-6`}
      >
        <div className="flex items-center gap-2 text-sm text-neutral-400">
          <Image
            src="/assets/lintty-icon.png"
            alt="Lintty"
            width={24}
            height={24}
            className="w-6 h-6 object-contain"
            suppressHydrationWarning
          />
          <span className="font-semibold text-paper">Lintty</span>
          <span className="ml-3">© 2026. Todos os direitos reservados.</span>
        </div>
        <nav className="flex items-center gap-6 text-sm text-neutral-400">
          <Link href="/privacidade" className="hover:text-paper transition">
            Privacidade
          </Link>
          <a href="mailto:contato@lintty.com" className="hover:text-paper transition">
            contato@lintty.com
          </a>
        </nav>
      </div>
    </footer>
  );
}
