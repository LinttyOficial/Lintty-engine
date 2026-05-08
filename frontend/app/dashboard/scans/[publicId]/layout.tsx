/**
 * Server-side companion to `[publicId]/page.tsx` (which is `"use client"`).
 *
 * Same shape as `app/dashboard/repos/[id]/layout.tsx` (PR F5): Next 15 with
 * `output: "export"` refuses to build a dynamic segment without
 * `generateStaticParams`, and `generateStaticParams` cannot live in a
 * client component.
 *
 * Scan public ids are UUIDs minted by Postgres at trigger time and
 * therefore unknown at build time. We emit a single placeholder folder
 * (`_`) and let the hosting layer (Cloudflare Pages: implicit
 * trailing-slash fallback; ASP.NET: explicit rewrite) map any real
 * `/dashboard/scans/<uuid>/` request to the placeholder html. The
 * client-side `useParams()` reads the actual id at runtime and validates
 * the UUID v4 shape before it touches the backend.
 */

export function generateStaticParams() {
  return [{ publicId: "_" }];
}

export default function ScanDetailLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  return <>{children}</>;
}
