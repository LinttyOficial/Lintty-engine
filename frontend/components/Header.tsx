"use client";

/**
 * The single header component used across every page. Renders one of two
 * variants based on the auth state:
 *
 *   - Anonymous: marketing nav (Como funciona / Inspecionar / CLI / Preços)
 *     plus Entrar + Criar conta CTAs.
 *   - Authenticated: Dashboard / Inspecionar / CLI plus avatar dropdown
 *     with name, email, quick links and Logout.
 *
 * `currentPath` is read from `usePathname()` to mark the active link with
 * `aria-current="page"` and emphasised text — same UX as the legacy
 * pages where each html file hand-coded its own active state.
 */

import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { useEffect, useRef, useState } from "react";
import { Logo } from "./Logo";
import { useAuth } from "@/lib/auth";

interface NavLinkProps {
  href: string;
  label: string;
  active: boolean;
}

function NavLink({ href, label, active }: NavLinkProps) {
  return (
    <Link
      href={href}
      className={
        active
          ? "text-ink font-semibold"
          : "text-neutral-700 hover:text-ink transition"
      }
      aria-current={active ? "page" : undefined}
    >
      {label}
    </Link>
  );
}

function initials(name: string | null | undefined): string {
  if (!name) return "?";
  const parts = String(name).trim().split(/\s+/).filter(Boolean);
  if (parts.length === 0) return "?";
  if (parts.length === 1) return parts[0].charAt(0).toUpperCase();
  return (
    parts[0].charAt(0) + parts[parts.length - 1].charAt(0)
  ).toUpperCase();
}

export function Header() {
  const pathname = usePathname() ?? "/";
  const { isAuthenticated, isLoading, user, currentOrg, logout } = useAuth();
  const router = useRouter();

  // We render the anon header during SSG and during the loading window —
  // the auth/me round-trip happens in the browser, and flipping headers
  // mid-render is jarring. The dashboard header (with avatar) only
  // appears after we know the user is authenticated.

  if (isAuthenticated && !isLoading && user) {
    return (
      <AuthenticatedHeader
        pathname={pathname}
        userName={user.displayName || user.email || "Usuário"}
        userEmail={user.email}
        orgName={currentOrg?.name ?? "sem organização"}
        onLogout={async () => {
          await logout();
          router.push("/");
        }}
      />
    );
  }

  return <AnonymousHeader pathname={pathname} />;
}

function AnonymousHeader({ pathname }: { pathname: string }) {
  const isActive = (path: string): boolean =>
    pathname === path || pathname.startsWith(`${path}/`);

  return (
    <header className="border-b border-neutral-200 bg-white/80 backdrop-blur sticky top-0 z-30">
      <div className="max-w-6xl mx-auto px-6 py-4 flex items-center justify-between">
        <Logo />
        <nav className="hidden md:flex items-center gap-7 text-sm">
          <Link href="/#como-funciona" className="text-neutral-700 hover:text-ink transition">
            Como funciona
          </Link>
          <NavLink
            href="/inspect"
            label="Inspecionar repo"
            active={isActive("/inspect")}
          />
          <NavLink
            href="/cli"
            label="Baixar CLI"
            active={isActive("/cli")}
          />
          <NavLink
            href="/pricing"
            label="Preços"
            active={isActive("/pricing")}
          />
          <NavLink
            href="/login"
            label="Entrar"
            active={isActive("/login")}
          />
          <Link
            href="/signup"
            className="px-4 py-2 rounded-md bg-ink text-white hover:bg-neutral-800 transition"
            aria-current={isActive("/signup") ? "page" : undefined}
          >
            Criar conta
          </Link>
        </nav>
        <Link
          href="/signup"
          className="md:hidden px-3 py-2 rounded-md bg-ink text-white text-sm"
        >
          Criar conta
        </Link>
      </div>
    </header>
  );
}

interface AuthenticatedHeaderProps {
  pathname: string;
  userName: string;
  userEmail: string;
  orgName: string;
  onLogout: () => Promise<void>;
}

function AuthenticatedHeader({
  pathname,
  userName,
  userEmail,
  orgName,
  onLogout,
}: AuthenticatedHeaderProps) {
  const [open, setOpen] = useState(false);
  const btnRef = useRef<HTMLButtonElement>(null);
  const menuRef = useRef<HTMLDivElement>(null);
  const firstName = String(userName).split(/\s+/)[0] || userName;

  // Close on outside click + Escape
  useEffect(() => {
    if (!open) return undefined;

    const handleClick = (ev: MouseEvent) => {
      const target = ev.target as Node;
      if (
        btnRef.current?.contains(target) ||
        menuRef.current?.contains(target)
      ) {
        return;
      }
      setOpen(false);
    };
    const handleKey = (ev: KeyboardEvent) => {
      if (ev.key === "Escape") {
        setOpen(false);
        btnRef.current?.focus();
      }
    };

    document.addEventListener("click", handleClick);
    document.addEventListener("keydown", handleKey);
    return () => {
      document.removeEventListener("click", handleClick);
      document.removeEventListener("keydown", handleKey);
    };
  }, [open]);

  const isActive = (path: string): boolean =>
    pathname === path || pathname.startsWith(`${path}/`);

  return (
    <header className="border-b border-neutral-200 bg-white/80 backdrop-blur sticky top-0 z-30">
      <div className="max-w-6xl mx-auto px-6 py-4 flex items-center justify-between">
        <Logo />
        <nav className="flex items-center gap-4 md:gap-7 text-sm text-neutral-700">
          <Link
            href="/dashboard"
            className={
              isActive("/dashboard")
                ? "hidden md:inline text-ink font-semibold"
                : "hidden md:inline hover:text-ink"
            }
            aria-current={isActive("/dashboard") ? "page" : undefined}
          >
            Dashboard
          </Link>
          <Link
            href="/inspect"
            className={
              isActive("/inspect")
                ? "hidden md:inline text-ink font-semibold"
                : "hidden md:inline hover:text-ink"
            }
          >
            Inspecionar repo
          </Link>
          <Link
            href="/cli"
            className={
              isActive("/cli")
                ? "hidden md:inline text-ink font-semibold"
                : "hidden md:inline hover:text-ink"
            }
          >
            CLI
          </Link>

          <div className="lt-dropdown">
            <button
              ref={btnRef}
              type="button"
              onClick={(ev) => {
                ev.stopPropagation();
                setOpen((v) => !v);
              }}
              className="flex items-center gap-3 px-2 py-1 rounded-md hover:bg-neutral-100 transition"
              aria-haspopup="true"
              aria-expanded={open}
            >
              <span className="lt-avatar" aria-hidden="true">
                {initials(userName)}
              </span>
              <span className="hidden sm:flex flex-col items-start leading-tight text-left">
                <span className="text-sm font-semibold text-ink">{firstName}</span>
                <span className="text-xs text-neutral-500">{orgName}</span>
              </span>
              <svg
                width="14"
                height="14"
                viewBox="0 0 20 20"
                fill="none"
                aria-hidden="true"
              >
                <path
                  d="M5 8l5 5 5-5"
                  stroke="currentColor"
                  strokeWidth="1.6"
                  strokeLinecap="round"
                  strokeLinejoin="round"
                />
              </svg>
            </button>
            <div
              ref={menuRef}
              className={`lt-dropdown-menu${open ? " is-open" : ""}`}
              role="menu"
            >
              <div className="px-3 py-2">
                <p className="text-sm font-semibold text-ink leading-tight">{userName}</p>
                <p className="text-xs text-neutral-500 leading-tight mt-0.5">
                  {userEmail}
                </p>
              </div>
              <div className="lt-dropdown-divider" />
              <Link href="/inspect" className="lt-dropdown-item" role="menuitem">
                Inspecionar repo
              </Link>
              <Link href="/cli" className="lt-dropdown-item" role="menuitem">
                Baixar CLI
              </Link>
              <Link href="/pricing" className="lt-dropdown-item" role="menuitem">
                Planos
              </Link>
              <div className="lt-dropdown-divider" />
              <button
                type="button"
                className="lt-dropdown-item"
                role="menuitem"
                onClick={() => {
                  setOpen(false);
                  void onLogout();
                }}
              >
                <svg
                  width="16"
                  height="16"
                  viewBox="0 0 20 20"
                  fill="none"
                  aria-hidden="true"
                >
                  <path
                    d="M13 5V3a1 1 0 0 0-1-1H4a1 1 0 0 0-1 1v14a1 1 0 0 0 1 1h8a1 1 0 0 0 1-1v-2M16 10H8m0 0l3-3m-3 3l3 3"
                    stroke="currentColor"
                    strokeWidth="1.5"
                    strokeLinecap="round"
                    strokeLinejoin="round"
                  />
                </svg>
                Sair
              </button>
            </div>
          </div>
        </nav>
      </div>
    </header>
  );
}
