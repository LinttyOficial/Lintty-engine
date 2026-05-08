/**
 * Typed client wrappers for the Sprint 3 dashboard endpoints.
 *
 * Mirrors the C# DTOs verbatim — field names follow the backend's
 * `[JsonPropertyName("...")]` decoration. The Sprint 3 surfaces use
 * camelCase (`githubUrl`, `publicId`, ...) while the V0 anonymous
 * `/api/jobs/*` flow keeps snake_case; that asymmetry is intentional and
 * preserved here.
 *
 * Every function returns the same `ApiResponse<T>` envelope `apiFetch`
 * already produces. UI is responsible for mapping `!ok` outcomes into
 * messages — this layer never throws on HTTP errors.
 */

import { apiFetch, backendUrl, type ApiResponse } from "./api";

// ── Repos ────────────────────────────────────────────────────────────────

export interface RepoAddedBy {
  id: number;
  displayName: string;
}

export interface RepoSummary {
  id: number;
  githubUrl: string;
  isPrivate: boolean;
  /** ISO-8601 UTC ("o" round-trip format). */
  createdAt: string;
  addedBy: RepoAddedBy;
}

/**
 * The list and detail endpoints return the same shape today
 * (`RepoResponse` in the backend). Kept as a separate alias so a future
 * detail-only field (e.g. last scan summary) can land without touching
 * call sites that only need the summary.
 */
export type RepoDetail = RepoSummary;

export interface RepoCreateRequest {
  githubUrl: string;
}

export interface RepoImportRequest {
  githubOrgLogin: string;
  /** "owner/name" pair, e.g. "acme/engine". */
  repoFullName: string;
}

export function listRepos(): Promise<ApiResponse<RepoSummary[]>> {
  return apiFetch<RepoSummary[]>("/api/repos");
}

export function getRepo(id: number): Promise<ApiResponse<RepoDetail>> {
  return apiFetch<RepoDetail>(`/api/repos/${encodeURIComponent(String(id))}`);
}

export function addRepoManual(
  req: RepoCreateRequest,
): Promise<ApiResponse<RepoDetail>> {
  return apiFetch<RepoDetail>("/api/repos", {
    method: "POST",
    body: JSON.stringify(req),
  });
}

export function importRepo(
  req: RepoImportRequest,
): Promise<ApiResponse<RepoDetail>> {
  return apiFetch<RepoDetail>("/api/repos/import", {
    method: "POST",
    body: JSON.stringify(req),
  });
}

export function softDeleteRepo(id: number): Promise<ApiResponse<void>> {
  return apiFetch<void>(`/api/repos/${encodeURIComponent(String(id))}`, {
    method: "DELETE",
  });
}

export function listRepoScans(
  repoId: number,
): Promise<ApiResponse<ScanSummary[]>> {
  return apiFetch<ScanSummary[]>(
    `/api/repos/${encodeURIComponent(String(repoId))}/scans`,
  );
}

/**
 * Hard cap on how many targets a single trigger expands into. Mirrors
 * `ScanService.MaxTargetsPerTrigger` server-side; surfaced here so the
 * picker can block submission with a friendly inline message instead of
 * relying on the 400. Bump in lock-step if the backend ever raises it.
 */
export const MAX_TARGETS_PER_TRIGGER = 50;

// ── Repo Preflight (Sprint 3 PR S1f) ─────────────────────────────────────

/**
 * Lifecycle stages for the preflight discovery (see
 * `Repos/Preflight/IRepoPreflightService.cs::PreflightStatus`):
 * - `ready`: saved selection or unambiguous auto-detect — scan can run.
 * - `needs_config`: candidates exist but auto-detect can't pick — user
 *   must choose between the discovered files.
 * - `no_dotnet_project`: no `.sln`, `.csproj`, or `lintty.yml` found.
 */
export type PreflightStatus = "ready" | "needs_config" | "no_dotnet_project";

/**
 * Candidate file kind for the picker. PR S2 — yaml was dropped from the
 * user-pickable candidate list because the engine resolver requires a
 * `projects:` block inside the yaml that we can't verify during a tree
 * walk. The backend's `BuildOrderedCandidates` only emits sln + csproj.
 *
 * Auto-detect can still surface a yaml on the `AutoDetected` channel
 * (root `lintty.yml` is the engine's preferred entrypoint when present)
 * — see {@link PreflightAutoDetectedKind}.
 */
export type PreflightCandidateKind = "sln" | "csproj";

/** Wider kind for `PreflightAutoDetected` — auto-detect still reports
 *  yaml when a root `lintty.yml` exists. */
export type PreflightAutoDetectedKind = "sln" | "csproj" | "yaml";

export interface PreflightCandidate {
  kind: PreflightCandidateKind;
  path: string;
}

export interface PreflightAutoDetected {
  kind: PreflightAutoDetectedKind;
  path: string;
}

export interface PreflightResult {
  status: PreflightStatus;
  /** Present only when `status === "ready"` AND no saved selection — what
   *  auto-detect would pick. Null when a saved selection exists (the
   *  selection wins, no auto-detect happens). */
  autoDetected: PreflightAutoDetected | null;
  /** Currently saved selection. Null/empty means auto-detect. */
  scanProjects: string[] | null;
  /** Every user-pickable candidate the discovery returned, in stable
   *  order (sln alpha first, then csproj alpha). Always populated so
   *  the dashboard can offer "change target" even when ready. yaml
   *  files are not included here — see {@link PreflightCandidateKind}. */
  candidates: PreflightCandidate[];
  /** True when discovery hit GitHub's per-tree limit. */
  truncated: boolean;
  /** Human-readable explanation for `needs_config`/`no_dotnet_project`.
   *  Null on `ready`. */
  reason: string | null;
}

export function getRepoPreflight(
  repoId: number,
): Promise<ApiResponse<PreflightResult>> {
  return apiFetch<PreflightResult>(
    `/api/repos/${encodeURIComponent(String(repoId))}/preflight`,
  );
}

/**
 * Persist the user's curated scan target. Empty list clears the saved
 * selection and returns the repo to auto-detect.
 *
 * PR S2 relaxed the backend validation: any combination of `.sln` +
 * `.csproj` paths is accepted (each entry becomes one independent scan
 * at trigger time). Yaml entries are still rejected with
 * `invalid_scan_projects` because the engine resolver fails on yamls
 * without a `projects:` block. Hard cap of 50 entries per request.
 *
 * Returns 204 on success (envelope `{ ok: true, body: null }`),
 * 400 with an `{ error, message }` body on combination errors, 404 on
 * cross-tenant.
 */
export function setRepoScanTarget(
  repoId: number,
  projects: string[],
): Promise<ApiResponse<void>> {
  return apiFetch<void>(
    `/api/repos/${encodeURIComponent(String(repoId))}/scan-target`,
    {
      method: "PUT",
      body: JSON.stringify({ projects }),
    },
  );
}

// ── Scans ────────────────────────────────────────────────────────────────

/**
 * Lifecycle stages allowed on `Scan.Status` (see
 * `Persistence/Entities/Scan.cs::ScanStatus`). Persisted as free-form
 * strings server-side; the union narrows callers to the values the CHECK
 * constraint enforces.
 */
export type ScanStatus =
  | "queued"
  | "running"
  | "completed"
  | "failed"
  | "cancelled";

export interface ScanRepoSummary {
  id: number;
  githubUrl: string;
}

export interface ScanSummary {
  publicId: string;
  status: ScanStatus;
  /** ISO-8601 UTC. */
  queuedAt: string;
  startedAt: string | null;
  completedAt: string | null;
  ref: string | null;
  canonVersion: string;
  /** "sha256:..." once the scan completes; null otherwise. */
  hashContent: string | null;
  error: string | null;
  triggeredByUserId: number;
  repo: ScanRepoSummary;
  /** Repo-relative path of the scan target this run analysed (e.g.
   *  `src/Foo/Foo.csproj`). Null when the trigger fell back to
   *  auto-detect (no `targets`, no saved `scan_projects`). */
  target: string | null;
}

/**
 * The trigger / poll endpoints return the same shape today. Aliased so
 * UI code can read intent ("I asked for a detail") without committing
 * the field set in stone.
 */
export type ScanDetail = ScanSummary;

export interface ScanTriggerRequest {
  repoId: number;
  /** Optional branch / tag / sha. Null = use repo default branch. */
  ref?: string | null;
  /**
   * Optional per-request override for the targets to expand into scans.
   * Each entry becomes one independent scan (N targets = N PDFs).
   *
   * Resolution order (server-side, see `ScanService.cs`):
   *   1. `targets` non-empty → use as-is.
   *   2. `targets` null/empty → fall back to the repo's saved
   *      `scan_projects` (set via {@link setRepoScanTarget}).
   *   3. Both empty → 1 auto-detect scan.
   *
   * Hard cap of 50 entries per trigger.
   */
  targets?: string[] | null;
}

/**
 * Response of {@link triggerScan}. PR S2 unified the single- and
 * multi-target flows: callers always receive an array (length 1 for the
 * common case, length N when expanding multiple targets).
 */
export interface BatchTriggerResponse {
  publicIds: string[];
}

export function triggerScan(
  req: ScanTriggerRequest,
): Promise<ApiResponse<BatchTriggerResponse>> {
  return apiFetch<BatchTriggerResponse>("/api/scans", {
    method: "POST",
    body: JSON.stringify(req),
  });
}

export function getScan(publicId: string): Promise<ApiResponse<ScanDetail>> {
  return apiFetch<ScanDetail>(
    `/api/scans/${encodeURIComponent(publicId)}`,
  );
}

/**
 * Absolute backend URL for the laudo PDF. Use as `<a href>` / `window.open`
 * — `apiFetch` is the wrong tool for binary streams.
 */
export function laudoPdfUrl(publicId: string): string {
  return backendUrl(`/api/scans/${encodeURIComponent(publicId)}/laudo.pdf`);
}

/** Absolute backend URL for the engine JSON report. Same usage pattern as `laudoPdfUrl`. */
export function reportJsonUrl(publicId: string): string {
  return backendUrl(`/api/scans/${encodeURIComponent(publicId)}/report.json`);
}

// ── GitHub Connect ───────────────────────────────────────────────────────

export interface GitHubConnectStatus {
  connected: boolean;
  /** Granted scopes when connected; absent when `connected=false`. */
  scopes?: string[];
  /** ISO-8601 UTC of the original grant. */
  grantedAt?: string;
  /** ISO-8601 UTC of the most recent successful API call with this token. */
  lastUsedAt?: string | null;
}

export interface GitHubConnectDeleteResult {
  revoked: boolean;
  alreadyRevoked: boolean;
  /** URL on github.com where the user can fully revoke the OAuth grant. */
  upstreamRevokeUrl: string;
}

export function getGitHubConnectStatus(): Promise<
  ApiResponse<GitHubConnectStatus>
> {
  return apiFetch<GitHubConnectStatus>("/api/auth/github/connect");
}

export function disconnectGitHub(): Promise<
  ApiResponse<GitHubConnectDeleteResult>
> {
  return apiFetch<GitHubConnectDeleteResult>("/api/auth/github/connect", {
    method: "DELETE",
  });
}

/**
 * Absolute URL the user should be navigated to (full-page redirect) to
 * start the elevated OAuth flow. Cannot be `apiFetch`d — the endpoint
 * issues a 302 to github.com.
 */
export function gitHubConnectStartUrl(): string {
  return backendUrl("/api/auth/github/connect/start");
}

// ── GitHub Orgs / Repos (read-only) ──────────────────────────────────────

export interface GitHubOrgSummary {
  id: number;
  login: string;
  avatarUrl: string | null;
}

export interface GitHubRepoSummary {
  id: number;
  name: string;
  fullName: string;
  /**
   * Wire field is named `private` (matches GitHub's REST envelope).
   * `private` is a reserved word in some JS contexts but is valid as an
   * object key.
   */
  private: boolean;
  defaultBranch: string;
  htmlUrl: string;
}

export function listGitHubOrgs(): Promise<ApiResponse<GitHubOrgSummary[]>> {
  return apiFetch<GitHubOrgSummary[]>("/api/github/orgs");
}

export function listGitHubOrgRepos(
  login: string,
): Promise<ApiResponse<GitHubRepoSummary[]>> {
  return apiFetch<GitHubRepoSummary[]>(
    `/api/github/orgs/${encodeURIComponent(login)}/repos`,
  );
}

// ── Error envelope ───────────────────────────────────────────────────────

/**
 * Backend error envelope (see `Endpoints/JobRequest.cs::ErrorResponse`).
 * The field name is `error`, not `code` — preserved verbatim. Extra
 * fields (e.g. `upstream_revoke_url` on some flows) ride in the index
 * signature.
 */
export interface ApiErrorBody {
  error: string;
  message: string;
  [key: string]: unknown;
}

export function isApiError(body: unknown): body is ApiErrorBody {
  if (typeof body !== "object" || body === null) return false;
  const b = body as Record<string, unknown>;
  return typeof b.error === "string" && typeof b.message === "string";
}

/**
 * Returns the typed error body when the response is non-OK and the body
 * matches the envelope; null otherwise (success, or 5xx with no body, or
 * a body shape we don't recognise).
 */
export function parseApiError(
  res: ApiResponse<unknown>,
): ApiErrorBody | null {
  if (res.ok) return null;
  return isApiError(res.body) ? res.body : null;
}
