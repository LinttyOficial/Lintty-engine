"use client";

/**
 * Floating pill header — wordmark sai, ícone fica num pill dark glass que
 * flutua centralizado no topo. Inspirado no header da Forge / 21st.dev:
 * gliph + nav inline + LogIn ghost + Signup em pill branco.
 *
 * Duas variantes:
 *   - Anonymous: nav marketing (Manifesto/Como funciona, Inspecionar, CLI,
 *     Preços) + Entrar (ghost) + Criar conta (pill branco).
 *   - Authenticated: nav dashboard (Dashboard, Inspecionar, CLI) + avatar
 *     com dropdown (nome/email + atalhos + sair).
 *
 * Comportamento de scroll: o pill ganha um glow emerald sutil quando o
 * usuário já scrollou (substitui o antigo "frosted-dark fica visível").
 * Como o pill já é sempre opaco, não precisamos mais do estado
 * transparente sobre o hero — ele convive bem com a aurora.
 */

import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { useEffect, useRef, useState } from "react";
import Image from "next/image";
import { useAuth } from "@/lib/auth";

function useScrolled(): boolean {
  const [scrolled, setScrolled] = useState(false);
  useEffect(() => {
    const onScroll = () => setScrolled(window.scrollY > 40);
    onScroll();
    window.addEventListener("scroll", onScroll, { passive: true });
    return () => window.removeEventListener("scroll", onScroll);
  }, []);
  return scrolled;
}

interface NavLinkProps {
  href: string;
  label: string;
  active: boolean;
}

function NavLink({ href, label, active }: NavLinkProps) {
  return (
    <Link
      href={href}
      aria-current={active ? "page" : undefined}
      className={`relative px-3 py-1.5 rounded-full text-sm transition ${
        active
          ? "text-paper font-semibold"
          : "text-neutral-300 hover:text-paper hover:bg-white/5"
      }`}
    >
      {label}
      {active && (
        <span
          aria-hidden="true"
          className="absolute left-1/2 -translate-x-1/2 -bottom-0.5 w-1 h-1 rounded-full bg-emerald-400 shadow-[0_0_6px_rgba(16,185,129,0.75)]"
        />
      )}
    </Link>
  );
}

function PillShell({
  children,
  scrolled,
}: {
  children: React.ReactNode;
  scrolled: boolean;
}) {
  return (
    <header className="sticky top-0 z-30 px-4 pt-4 pb-2 pointer-events-none">
      <div
        className={`lt-pill pointer-events-auto mx-auto flex items-center gap-1.5 rounded-full backdrop-blur-xl pl-2 pr-1.5 py-1.5 w-fit max-w-full ${
          scrolled ? "is-scrolled" : ""
        }`}
      >
        {children}
      </div>
    </header>
  );
}

function PillLogo() {
  return (
    <Link
      href="/"
      aria-label="Lintty — voltar à página inicial"
      className="flex items-center gap-2 pl-1 pr-2.5 py-1 rounded-full hover:bg-white/5 transition shrink-0 group"
    >
      <Image
        src="/assets/lintty-icon.png"
        alt=""
        width={22}
        height={22}
        className="w-[22px] h-[22px] object-contain"
        priority
        suppressHydrationWarning
      />
      <span className="text-paper font-semibold tracking-tight text-[15px] hidden sm:inline">
        Lintty
      </span>
      <span
        className="hidden md:inline-flex items-center text-[9px] font-mono font-semibold tracking-[0.12em] uppercase text-emerald-300/80 bg-emerald-500/10 border border-emerald-400/20 rounded px-1.5 py-0.5 leading-none"
        aria-label="Versão zero, beta"
      >
        V0
      </span>
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
  const scrolled = useScrolled();

  if (isAuthenticated && !isLoading && user) {
    return (
      <AuthenticatedHeader
        pathname={pathname}
        scrolled={scrolled}
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

  return <AnonymousHeader pathname={pathname} scrolled={scrolled} />;
}

function AnonymousHeader({
  pathname,
  scrolled,
}: {
  pathname: string;
  scrolled: boolean;
}) {
  const isActive = (path: string): boolean =>
    pathname === path || pathname.startsWith(`${path}/`);

  return (
    <PillShell scrolled={scrolled}>
      <PillLogo />

      <span aria-hidden="true" className="hidden md:block h-5 w-px bg-neutral-800/80 mx-1" />

      <nav className="hidden md:flex items-center gap-0.5 px-1">
        <NavLink
          href="/#como-funciona"
          label="Como funciona"
          active={false}
        />
        <NavLink
          href="/inspect"
          label="Inspecionar"
          active={isActive("/inspect")}
        />
        <NavLink
          href="/cli"
          label="CLI"
          active={isActive("/cli")}
        />
        <NavLink
          href="/pricing"
          label="Preços"
          active={isActive("/pricing")}
        />
      </nav>

      <span aria-hidden="true" className="hidden md:block h-5 w-px bg-neutral-800/80 mx-1" />

      <div className="flex items-center gap-1.5">
        <Link
          href="/login"
          aria-current={isActive("/login") ? "page" : undefined}
          className="hidden sm:inline-flex items-center px-4 py-1.5 rounded-full text-sm font-medium text-neutral-300 hover:text-paper hover:bg-white/5 transition"
        >
          Entrar
        </Link>
        <Link
          href="/signup"
          aria-current={isActive("/signup") ? "page" : undefined}
          className="inline-flex items-center px-4 py-1.5 rounded-full text-sm font-semibold bg-saint text-white hover:bg-emerald-600 transition shadow-[0_6px_20px_-6px_rgba(16,185,129,0.55)]"
        >
          Criar conta
        </Link>
      </div>
    </PillShell>
  );
}

interface AuthenticatedHeaderProps {
  pathname: string;
  scrolled: boolean;
  userName: string;
  userEmail: string;
  orgName: string;
  onLogout: () => Promise<void>;
}

function AuthenticatedHeader({
  pathname,
  scrolled,
  userName,
  userEmail,
  orgName,
  onLogout,
}: AuthenticatedHeaderProps) {
  const [open, setOpen] = useState(false);
  const btnRef = useRef<HTMLButtonElement>(null);
  const menuRef = useRef<HTMLDivElement>(null);
  const firstName = String(userName).split(/\s+/)[0] || userName;

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
    <PillShell scrolled={scrolled}>
      <PillLogo />

      <span aria-hidden="true" className="hidden md:block h-5 w-px bg-neutral-800/80 mx-1" />

      <nav className="hidden md:flex items-center gap-0.5 px-1">
        <NavLink
          href="/dashboard"
          label="Dashboard"
          active={isActive("/dashboard")}
        />
        <NavLink
          href="/inspect"
          label="Inspecionar"
          active={isActive("/inspect")}
        />
        <NavLink
          href="/cli"
          label="CLI"
          active={isActive("/cli")}
        />
      </nav>

      <span aria-hidden="true" className="hidden md:block h-5 w-px bg-neutral-800/80 mx-1" />

      <div className="lt-dropdown">
        <button
          ref={btnRef}
          type="button"
          onClick={(ev) => {
            ev.stopPropagation();
            setOpen((v) => !v);
          }}
          className="flex items-center gap-2 px-1.5 py-1 rounded-full hover:bg-white/5 transition"
          aria-haspopup="true"
          aria-expanded={open}
        >
          <span className="lt-avatar" aria-hidden="true" style={{ width: 28, height: 28, fontSize: 12 }}>
            {initials(userName)}
          </span>
          <span className="hidden sm:flex flex-col items-start leading-tight text-left">
            <span className="text-xs font-semibold text-paper">{firstName}</span>
            <span className="text-[10px] text-neutral-400">{orgName}</span>
          </span>
          <svg
            width="12"
            height="12"
            viewBox="0 0 20 20"
            fill="none"
            aria-hidden="true"
            className="text-neutral-400 mr-1"
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
            <p className="text-sm font-semibold text-paper leading-tight">{userName}</p>
            <p className="text-xs text-neutral-400 leading-tight mt-0.5">
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
    </PillShell>
  );
}
