import type { Config } from "tailwindcss";

/**
 * Single source of truth for the Lintty palette + typography.
 *
 * Mirrors the previous `landing/*.html` Tailwind CDN config bit-for-bit so
 * the visual identity does not drift in the migration:
 *   - ink (#0a0a0a) — primary text, dark CTAs
 *   - paper (#fafaf9) — body background
 *   - saint (#0f4c3a) / saint-bg — positive / brand accent
 *   - sinner (#7a1f1f) / sinner-bg — destructive / error
 *
 * Inter is loaded via next/font in app/layout.tsx — no remote <link> on
 * each page like the legacy HTML did.
 */
const config: Config = {
  content: [
    "./app/**/*.{ts,tsx}",
    "./components/**/*.{ts,tsx}",
    "./lib/**/*.{ts,tsx}",
  ],
  theme: {
    extend: {
      colors: {
        ink: "#0a0a0a",
        paper: "#fafaf9",
        saint: "#0f4c3a",
        "saint-bg": "#ecf5f0",
        sinner: "#7a1f1f",
        "sinner-bg": "#f8eded",
      },
      fontFamily: {
        sans: [
          "var(--font-inter)",
          "Inter",
          "system-ui",
          "-apple-system",
          "Segoe UI",
          "Roboto",
          "sans-serif",
        ],
        mono: [
          "var(--font-jetbrains)",
          "JetBrains Mono",
          "ui-monospace",
          "SFMono-Regular",
          "Menlo",
          "Consolas",
          "monospace",
        ],
      },
      maxWidth: {
        "prose-tight": "60ch",
      },
    },
  },
  plugins: [],
};

export default config;
