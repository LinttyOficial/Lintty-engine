# Lintty Frontend

Next.js 15 + TypeScript + Tailwind 3, static-exported to `out/`. Replaces the
old `landing/*.html` setup that drifted between sessions because each page
hand-coded its own header, footer and JS.

Build outputs to `frontend/out/`. The Web Inspector ASP.NET host (under
`engine/src/Lintty.WebInspector/`) serves that directory verbatim in dev
so you keep same-origin fetches against `localhost:5180/api/*`. In
production, this `out/` deploys separately to Cloudflare Pages and the
backend serves only `/api/*`.

## Pre-requisites

- **Node.js >= 20** (CI uses 22; package.json has no engines block but the
  Next.js 15 baseline requires 18.18+).
- **npm >= 10**.

## Common commands

```bash
cd frontend
npm install            # one-time, populates node_modules
npm run dev            # http://localhost:3000 with hot reload
npm run build          # static export to ./out
npm run lint           # eslint via next-lint
npm run start          # serve the export with `next start` (rare; usually
                       # you want the ASP.NET host or Cloudflare Pages)
```

## Local integration with the backend

The frontend talks to `POST /api/jobs`, `GET /api/jobs/{id}`,
`POST /api/auth/{signup,login,logout}` and `GET /api/auth/me`. `lib/api.ts`
auto-detects:

- Browser at `localhost`/`127.0.0.1` → backend at `http://localhost:5180`
  (auth cookies travel with `credentials: "include"`).
- Browser at any other host → same-origin fetches.

Two ways to run end-to-end locally:

### 1. Hot reload (recommended for frontend work)

```bash
# terminal A
cd frontend && npm run dev          # serves http://localhost:3000

# terminal B
cd engine && dotnet run --project src/Lintty.WebInspector
                                    # serves http://localhost:5180/api/*
```

Cookies set by the backend on `localhost:5180` are scoped to that origin
(SameSite=Lax). Cross-origin POSTs from `:3000` to `:5180` are allowed
because the backend's CORS policy permits localhost development origins.

### 2. Same-origin (recommended for cross-determinism QA)

```bash
cd frontend && npm run build        # produces frontend/out/
cd ../engine && dotnet run --project src/Lintty.WebInspector
# now http://localhost:5180/ serves the exported pages and /api/*
```

`engine/src/Lintty.WebInspector/Program.cs::ResolveLandingRoot` resolves to
`../../../../frontend/out` from the WebInspector content root, so the
exported pages are served same-origin without extra config. If the folder
does not exist (e.g. in a CI box that didn't run `npm run build`), the
middleware no-ops — the API still works.

## Routing map

| Path | Source | Type |
|---|---|---|
| `/` | `app/(marketing)/page.tsx` | SSG |
| `/cli` | `app/(marketing)/cli/page.tsx` | SSG |
| `/pricing` | `app/(marketing)/pricing/page.tsx` | SSG |
| `/privacidade` | `app/(marketing)/privacidade/page.tsx` | SSG |
| `/inspect` | `app/inspect/page.tsx` | client-rendered (polling) |
| `/login` | `app/login/page.tsx` | client-rendered |
| `/signup` | `app/signup/page.tsx` | client-rendered |
| `/dashboard` | `app/dashboard/page.tsx` | client-rendered (auth-gated) |

`/inspect` accepts a dev-only `?state=form|running|completed|failed` query
parameter (only honored on `localhost`/`127.0.0.1`/`file:`) to inspect each
of the four UX states without going through the backend.

## Component map

- `components/Header.tsx` — anonymous + authenticated variants, picks one
  via `useAuth()`.
- `components/Footer.tsx` — single source of truth for the footer.
- `components/Logo.tsx`, `components/SkipLink.tsx` — small primitives reused
  in every layout.
- `lib/api.ts` — fetch wrapper that always sets `credentials: "include"`
  and resolves `BASE_URL` based on `window.location.hostname`.
- `lib/auth.tsx` — `<AuthProvider>` + `useAuth()`. One `/api/auth/me` call
  on mount, cached in React context. Includes `signup`, `login`, `logout`,
  `refresh` and a `networkError` flag so the dashboard can show its
  "backend indisponível" copy without false positives.

## Deploy to Cloudflare Pages (V1+, when public)

The build artifact at `frontend/out/` is a fully static folder. Cloudflare
Pages can be wired with:

- **Build command**: `cd frontend && npm install && npm run build`
- **Build output directory**: `frontend/out`
- **Root directory**: repo root
- **Node version**: `20` or `22`

Configure a Worker route or a custom rule on Pages to forward `/api/*` to
the ASP.NET host (Cloud Run / Fly / VM hosting `Lintty.WebInspector`) so
the frontend can keep using same-origin fetches in prod. Cookies must be
issued by the same registrable domain as the Pages site for SameSite=Lax
cookies to flow.

## Conventions

- TypeScript `strict: true`. No `any` unless commented and unavoidable.
- Tailwind config in `tailwind.config.ts`; component-specific CSS lives
  in `app/globals.css` under labelled sections (skip link, badge, modal,
  etc).
- Palette is single-source-of-truth in `tailwind.config.ts`:
  - `ink` (`#0a0a0a`), `paper` (`#fafaf9`)
  - `saint` (`#0f4c3a`) / `saint-bg` (`#ecf5f0`)
  - `sinner` (`#7a1f1f`) / `sinner-bg` (`#f8eded`)
- No state library. `useState` + Context covers V0; revisit when the
  dashboard gets multi-screen forms (Sprint 3).
- No component library (no shadcn, no headlessui). Roll the few primitives
  by hand to keep the visual identity tight.
