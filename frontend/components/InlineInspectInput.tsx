"use client";

import { useRouter } from "next/navigation";
import { useState, type FormEvent } from "react";

const GITHUB_URL_RE = /^https?:\/\/github\.com\/[^/\s]+\/[^/\s]+\/?$/i;

/**
 * Mini-form embutido no hero da landing. O usuário cola uma URL e o submit
 * navega direto pra `/inspect?url=...`, onde o InspectForm já está
 * pre-preenchido. Reduz o atrito "clicar pra ir ver" — o produto roda na
 * própria home.
 */
export function InlineInspectInput() {
  const router = useRouter();
  const [url, setUrl] = useState("");
  const [error, setError] = useState<string | null>(null);

  function handleSubmit(ev: FormEvent<HTMLFormElement>) {
    ev.preventDefault();
    const trimmed = url.trim();
    if (!trimmed) {
      setError("Cole a URL do repositório GitHub.");
      return;
    }
    if (!GITHUB_URL_RE.test(trimmed)) {
      setError("URL inválida. Use https://github.com/owner/repo.");
      return;
    }
    setError(null);
    router.push(`/inspect?url=${encodeURIComponent(trimmed)}`);
  }

  return (
    <form
      onSubmit={handleSubmit}
      className="mt-8 max-w-xl"
      aria-label="Analisar repositório agora"
    >
      <div className="flex flex-col sm:flex-row items-stretch gap-2 rounded-full bg-neutral-900/60 backdrop-blur-md p-1.5 shadow-inner shadow-black/40 focus-within:shadow-emerald-500/30 focus-within:shadow-lg transition-shadow">
        <input
          type="url"
          value={url}
          onChange={(e) => {
            setUrl(e.target.value);
            if (error) setError(null);
          }}
          placeholder="https://github.com/owner/repo"
          aria-label="URL do repositório GitHub"
          className="flex-1 bg-transparent px-4 py-2.5 text-sm font-mono text-paper placeholder:text-neutral-500 focus:outline-none"
        />
        <button
          type="submit"
          className="inline-flex items-center justify-center gap-2 px-5 py-2.5 rounded-full bg-saint text-white font-semibold text-sm hover:bg-emerald-600 transition shadow-md shadow-emerald-900/40 whitespace-nowrap"
        >
          Analisar agora
          <span aria-hidden="true">→</span>
        </button>
      </div>
      {error ? (
        <p className="mt-2 text-sm text-red-300" role="alert" aria-live="polite">
          {error}
        </p>
      ) : (
        <p className="mt-2 text-xs text-neutral-500">
          Clone efêmero · descartado em 60s · zero persistência
        </p>
      )}
    </form>
  );
}
