import Link from "next/link";
import Image from "next/image";

interface LogoProps {
  size?: "sm" | "md";
}

/**
 * Wordmark used in the header + footer. Same iconography as the legacy
 * `<a href="/"><img .../>Lintty</a>` block, factored out so the markup
 * never drifts between pages.
 */
export function Logo({ size = "md" }: LogoProps) {
  const dim = size === "md" ? 32 : 24;
  return (
    <Link
      href="/"
      className="flex items-center gap-2 font-semibold tracking-tight text-ink"
      style={{ fontSize: size === "md" ? "1.125rem" : "1rem" }}
    >
      <Image
        src="/assets/lintty-icon.png"
        alt="Lintty"
        width={dim}
        height={dim}
        className={size === "md" ? "w-8 h-8 object-contain" : "w-6 h-6 object-contain"}
        priority={size === "md"}
      />
      <span>Lintty</span>
    </Link>
  );
}
