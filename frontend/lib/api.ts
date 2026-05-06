/**
 * Thin fetch wrapper that:
 *   1. Picks the right BASE_URL (localhost → :5180, prod → same origin).
 *   2. Always sends `credentials: "include"` so the auth cookie travels.
 *   3. Sets sensible defaults for JSON requests.
 *
 * Mirrors the legacy `BASE_URL` snippet that was duplicated across each
 * landing/*.html file. Keep this as the only place that decides where
 * the API lives.
 */

export function getBaseUrl(): string {
  if (typeof window === "undefined") {
    // SSG/prerender: relative URLs only — the page hydrates and the
    // real fetch happens in the browser.
    return "";
  }
  const { hostname } = window.location;
  if (hostname === "localhost" || hostname === "127.0.0.1") {
    return "http://localhost:5180";
  }
  return "";
}

export interface ApiResponse<T = unknown> {
  status: number;
  ok: boolean;
  body: T | null;
  headers: Headers;
}

export async function apiFetch<T = unknown>(
  path: string,
  init?: RequestInit,
): Promise<ApiResponse<T>> {
  const baseUrl = getBaseUrl();
  const url = path.startsWith("http") ? path : `${baseUrl}${path}`;

  const headers = new Headers(init?.headers);
  if (!headers.has("Accept")) {
    headers.set("Accept", "application/json");
  }
  if (init?.body && !headers.has("Content-Type")) {
    headers.set("Content-Type", "application/json");
  }

  const res = await fetch(url, {
    ...init,
    headers,
    credentials: "include",
  });

  let body: T | null = null;
  try {
    const text = await res.text();
    if (text) {
      body = JSON.parse(text) as T;
    }
  } catch {
    body = null;
  }

  return {
    status: res.status,
    ok: res.ok,
    body,
    headers: res.headers,
  };
}

/**
 * Build an absolute URL pointing at the backend (used for `<a href>` like
 * the GitHub OAuth start link or the laudo PDF download).
 */
export function backendUrl(path: string): string {
  return `${getBaseUrl()}${path}`;
}
