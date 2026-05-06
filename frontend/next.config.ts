import type { NextConfig } from "next";

/**
 * Static export so the build is deploy-anywhere (Cloudflare Pages, Vercel,
 * or served by the Web Inspector ASP.NET host in dev).
 *
 * - `output: "export"` produces a fully static `out/` folder; no SSR, no
 *   Image Optimization runtime, no server-only routes. Perfect for V0.
 * - `trailingSlash: true` makes every page emit as `route/index.html`,
 *   which plays nicely with both Cloudflare Pages and ASP.NET's
 *   UseStaticFiles + UseDefaultFiles middleware.
 * - `images.unoptimized` is required for `output: export`.
 */
const nextConfig: NextConfig = {
  output: "export",
  trailingSlash: true,
  images: {
    unoptimized: true,
  },
  // The dashboard / signup / login /inspect pages are client-rendered with
  // the same `app/` directory; static export still works because they
  // hydrate on the client and don't rely on per-request server props.
  reactStrictMode: true,
};

export default nextConfig;
