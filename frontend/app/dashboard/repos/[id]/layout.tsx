/**
 * Server-side companion to `[id]/page.tsx` (which is `"use client"`).
 *
 * Holds `generateStaticParams` because Next 15 with `output: "export"`
 * refuses to build a dynamic segment without one, AND
 * `generateStaticParams` cannot live in a client component.
 *
 * Repo ids are DB-driven and unknown at build time, so we emit a single
 * placeholder folder. The hosting layer (Cloudflare Pages: implicit
 * trailing-slash fallback; ASP.NET: explicit rewrite, see PR notes) maps
 * any real `/dashboard/repos/<n>/` request to the placeholder html, and
 * the client-side `useParams()` reads the actual numeric id at runtime.
 */

export function generateStaticParams() {
  return [{ id: "_" }];
}

export default function RepoDetailLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  return <>{children}</>;
}
