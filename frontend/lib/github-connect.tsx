"use client";

/**
 * GitHub Connect status, scoped to the /dashboard subtree.
 *
 * The badge in the dashboard header, the future org/repo browse (F4) and
 * the trigger-scan flow (F5) all need the same answer to "is GitHub
 * connected for this user?". Rather than each fetching independently,
 * they share a single in-memory cache via this provider.
 *
 * Intentionally NOT promoted to the global AuthProvider — anonymous and
 * marketing pages have no business calling /api/auth/github/connect.
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
import {
  getGitHubConnectStatus,
  type GitHubConnectStatus,
} from "./dashboard-api";

interface GitHubConnectContextValue {
  status: GitHubConnectStatus | null;
  isLoading: boolean;
  /** True when the last fetch failed at the network layer. */
  networkError: boolean;
  refresh: () => Promise<void>;
}

const GitHubConnectContext =
  createContext<GitHubConnectContextValue | undefined>(undefined);

export function GitHubConnectProvider({ children }: { children: ReactNode }) {
  const [status, setStatus] = useState<GitHubConnectStatus | null>(null);
  const [isLoading, setLoading] = useState(true);
  const [networkError, setNetworkError] = useState(false);

  const refresh = useCallback(async () => {
    setLoading(true);
    setNetworkError(false);
    try {
      const res = await getGitHubConnectStatus();
      if (res.ok && res.body) {
        setStatus(res.body);
      } else if (res.status === 401) {
        // Anonymous — leave as "not connected"; the auth gate above will
        // already redirect to /login, so the badge never renders.
        setStatus({ connected: false });
      } else {
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

  const value = useMemo<GitHubConnectContextValue>(
    () => ({ status, isLoading, networkError, refresh }),
    [status, isLoading, networkError, refresh],
  );

  return (
    <GitHubConnectContext.Provider value={value}>
      {children}
    </GitHubConnectContext.Provider>
  );
}

export function useGitHubConnect(): GitHubConnectContextValue {
  const ctx = useContext(GitHubConnectContext);
  if (!ctx) {
    throw new Error(
      "useGitHubConnect must be used inside <GitHubConnectProvider>",
    );
  }
  return ctx;
}
