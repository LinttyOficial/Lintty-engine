"use client";

/**
 * Auth context + hook.
 *
 * Single fetch to /api/auth/me on mount; every component that needs the
 * current user reads from React context. Mirrors the manual `loadMe()`
 * call that used to live in dashboard.html.
 *
 * No external state library — `useState` + Context covers V0 cleanly.
 */

import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useState,
  type ReactNode,
} from "react";
import { apiFetch } from "./api";

export interface User {
  id?: string;
  email: string;
  displayName?: string | null;
}

export interface Org {
  id?: string;
  name: string;
  slug?: string;
}

export interface Membership {
  org?: Org;
  role?: string;
  name?: string;
}

export interface AuthMeResponse {
  user: User;
  currentOrg?: Org | null;
  memberships?: Membership[];
}

export interface SignupPayload {
  displayName: string;
  email: string;
  password: string;
  orgName: string;
}

export interface LoginPayload {
  email: string;
  password: string;
}

interface AuthContextValue {
  user: User | null;
  currentOrg: Org | null;
  memberships: Membership[];
  isLoading: boolean;
  isAuthenticated: boolean;
  /** True when the initial /api/auth/me fetch failed with a network error. */
  networkError: boolean;
  refresh: () => Promise<void>;
  signup: (payload: SignupPayload) => Promise<{ ok: boolean; status: number; body: unknown }>;
  login: (payload: LoginPayload) => Promise<{ ok: boolean; status: number; body: unknown }>;
  logout: () => Promise<void>;
}

const AuthContext = createContext<AuthContextValue | undefined>(undefined);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<User | null>(null);
  const [currentOrg, setCurrentOrg] = useState<Org | null>(null);
  const [memberships, setMemberships] = useState<Membership[]>([]);
  const [isLoading, setLoading] = useState(true);
  const [networkError, setNetworkError] = useState(false);

  const refresh = useCallback(async () => {
    setLoading(true);
    setNetworkError(false);
    try {
      const res = await apiFetch<AuthMeResponse>("/api/auth/me");
      if (res.status === 401) {
        setUser(null);
        setCurrentOrg(null);
        setMemberships([]);
      } else if (res.ok && res.body) {
        setUser(res.body.user ?? null);
        setCurrentOrg(res.body.currentOrg ?? null);
        setMemberships(res.body.memberships ?? []);
      } else {
        // 5xx etc. — treat as network error so the UI can surface it.
        setNetworkError(true);
      }
    } catch {
      setNetworkError(true);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  const signup = useCallback<AuthContextValue["signup"]>(
    async (payload) => {
      const res = await apiFetch("/api/auth/signup", {
        method: "POST",
        body: JSON.stringify(payload),
      });
      if (res.status === 201) {
        await refresh();
      }
      return { ok: res.status === 201, status: res.status, body: res.body };
    },
    [refresh],
  );

  const login = useCallback<AuthContextValue["login"]>(
    async (payload) => {
      const res = await apiFetch("/api/auth/login", {
        method: "POST",
        body: JSON.stringify(payload),
      });
      if (res.status === 200) {
        await refresh();
      }
      return { ok: res.status === 200, status: res.status, body: res.body };
    },
    [refresh],
  );

  const logout = useCallback(async () => {
    try {
      await apiFetch("/api/auth/logout", { method: "POST" });
    } catch {
      // Even if the call failed, drop the local state so the UI reflects
      // "logged out" regardless.
    }
    setUser(null);
    setCurrentOrg(null);
    setMemberships([]);
  }, []);

  const value = useMemo<AuthContextValue>(
    () => ({
      user,
      currentOrg,
      memberships,
      isLoading,
      isAuthenticated: user !== null,
      networkError,
      refresh,
      signup,
      login,
      logout,
    }),
    [
      user,
      currentOrg,
      memberships,
      isLoading,
      networkError,
      refresh,
      signup,
      login,
      logout,
    ],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
  const ctx = useContext(AuthContext);
  if (!ctx) {
    throw new Error("useAuth must be used inside <AuthProvider>");
  }
  return ctx;
}
