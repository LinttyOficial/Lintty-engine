using System.Text.Json.Serialization;

namespace Lintty.WebInspector.Endpoints;

/// <summary>
/// Request body for <c>POST /api/jobs</c>. Only <see cref="GithubUrl"/> is
/// required; everything else has a sensible default. <see cref="GithubToken"/>
/// is sensitive — the worker never persists it; it lives in memory for the
/// duration of a single clone and is zeroed when the call returns.
/// </summary>
/// <example>
/// {
///   "github_url": "https://github.com/owner/repo"
/// }
/// </example>
public sealed class CreateJobRequest
{
    /// <summary>
    /// HTTPS URL of a public GitHub repository, e.g.
    /// <c>https://github.com/owner/repo</c>. Other hosts are rejected with
    /// <c>400 invalid_url</c>. Trailing <c>.git</c> is allowed and stripped.
    /// </summary>
    /// <example>https://github.com/owner/repo</example>
    [JsonPropertyName("github_url")] public string? GithubUrl { get; set; }

    /// <summary>
    /// Optional branch, tag, or commit SHA. When omitted the repository's
    /// default branch (typically <c>main</c>) is used.
    /// </summary>
    /// <example>main</example>
    [JsonPropertyName("ref")]         public string? Ref { get; set; }

    /// <summary>
    /// Optional GitHub Personal Access Token with <c>repo</c> scope, used only
    /// to look up metadata for private repositories. The token is never
    /// persisted, never logged, and is zeroed at the end of the request. In
    /// V0 the clone itself is public-only; private-repo cloning will arrive
    /// with a future ticket. Clients with private repos should use the local
    /// CLI in the meantime.
    /// </summary>
    [JsonPropertyName("github_token")] public string? GithubToken { get; set; }

    /// <summary>
    /// Optional path (relative to the repo root) of the <c>.sln</c> the engine
    /// should analyze. When omitted, the worker scans the repo root for a
    /// single <c>.sln</c>.
    /// </summary>
    /// <example>src/MyApp.sln</example>
    [JsonPropertyName("solution_path")] public string? SolutionPath { get; set; }
}

/// <summary>
/// Successful <c>202 Accepted</c> response from <c>POST /api/jobs</c>. The
/// caller polls <see cref="PollUrl"/> until <c>status</c> becomes
/// <c>completed</c> or <c>failed</c>.
/// </summary>
/// <example>
/// {
///   "job_id": "01HZX5Y7N3K9JZ8E4Q2W6F1A0P",
///   "status": "queued",
///   "poll_url": "/api/jobs/01HZX5Y7N3K9JZ8E4Q2W6F1A0P"
/// }
/// </example>
public sealed class CreateJobResponse
{
    /// <summary>26-char ULID, Crockford Base32. Use it as the <c>{id}</c>
    /// path parameter for the polling and download endpoints.</summary>
    [JsonPropertyName("job_id")]   public string JobId { get; set; } = string.Empty;

    /// <summary>Always <c>queued</c> on a freshly created job.</summary>
    [JsonPropertyName("status")]   public string Status { get; set; } = string.Empty;

    /// <summary>Relative URL to poll for status. Equivalent to
    /// <c>/api/jobs/{job_id}</c>.</summary>
    [JsonPropertyName("poll_url")] public string PollUrl { get; set; } = string.Empty;
}

/// <summary>
/// Stable error envelope used for every <c>4xx</c> and <c>5xx</c> response
/// from the API. <see cref="Error"/> is a machine-friendly snake_case code
/// (e.g. <c>invalid_url</c>, <c>rate_limited</c>, <c>repo_too_large</c>);
/// <see cref="Message"/> is a human-friendly explanation safe to show in UI.
/// </summary>
public sealed class ErrorResponse
{
    /// <summary>Machine-readable error code (snake_case).</summary>
    /// <example>invalid_url</example>
    [JsonPropertyName("error")] public string Error { get; set; } = string.Empty;

    /// <summary>Human-readable message safe to display in client UI.</summary>
    [JsonPropertyName("message")] public string Message { get; set; } = string.Empty;
}

/// <summary>
/// Polling response for <c>GET /api/jobs/{id}</c>. The shape is a single
/// envelope with optional fields — different statuses populate different
/// subsets:
/// <list type="bullet">
///   <item><description><c>queued</c>: only <c>job_id</c> and <c>status</c>.</description></item>
///   <item><description><c>running</c>: also <c>stage</c> (one of <c>cloning|restoring|analyzing|rendering</c>).</description></item>
///   <item><description><c>completed</c>: also <c>score</c>, <c>grade</c>, <c>canon_version</c>,
///   <c>violation_count</c>, <c>hard_locks_open</c>, <c>pdf_url</c>, <c>json_url</c>,
///   <c>expires_at</c>.</description></item>
///   <item><description><c>failed</c>: also <c>error_code</c> and <c>error_message</c>.</description></item>
/// </list>
/// </summary>
/// <remarks>
/// Example shapes:
///
/// queued:
/// <code>{ "job_id": "01HZX...", "status": "queued" }</code>
///
/// running:
/// <code>{ "job_id": "01HZX...", "status": "running", "stage": "analyzing" }</code>
///
/// completed:
/// <code>
/// {
///   "job_id": "01HZX...",
///   "status": "completed",
///   "score": 75,
///   "grade": "B",
///   "canon_version": "1.0",
///   "violation_count": 4,
///   "hard_locks_open": 0,
///   "pdf_url": "/api/jobs/01HZX.../laudo.pdf",
///   "json_url": "/api/jobs/01HZX.../report.json",
///   "expires_at": "2026-05-01T12:00:00.0000000Z"
/// }
/// </code>
///
/// failed:
/// <code>
/// {
///   "job_id": "01HZX...",
///   "status": "failed",
///   "error_code": "no_sln",
///   "error_message": "No .sln file found in the repository."
/// }
/// </code>
/// </remarks>
public sealed class JobStatusResponse
{
    /// <summary>The ULID echoed back from the original POST.</summary>
    [JsonPropertyName("job_id")] public string JobId { get; set; } = string.Empty;

    /// <summary>One of <c>queued</c>, <c>running</c>, <c>completed</c>, <c>failed</c>.</summary>
    [JsonPropertyName("status")] public string Status { get; set; } = string.Empty;

    /// <summary>For <c>running</c> jobs only: <c>cloning</c>, <c>restoring</c>,
    /// <c>analyzing</c>, or <c>rendering</c>. Null otherwise.</summary>
    [JsonPropertyName("stage")]  public string? Stage { get; set; }

    /// <summary>Final score 0–100, rounded to nearest 5. Completed jobs only.</summary>
    [JsonPropertyName("score")]            public int?    Score { get; set; }

    /// <summary>Letter grade <c>A</c>–<c>F</c>. Completed jobs only.</summary>
    [JsonPropertyName("grade")]            public string? Grade { get; set; }

    /// <summary>Canon version applied (e.g. <c>1.0</c>). Completed jobs only.</summary>
    [JsonPropertyName("canon_version")]    public string? CanonVersion { get; set; }

    /// <summary>Total number of violations across all severities. Completed jobs only.</summary>
    [JsonPropertyName("violation_count")]  public int?    ViolationCount { get; set; }

    /// <summary>Open hard-lock violations (LNTY-001, LNTY-002, LNTY-007). Any
    /// non-zero value forces grade <c>F</c>. Completed jobs only.</summary>
    [JsonPropertyName("hard_locks_open")]  public int?    HardLocksOpen { get; set; }

    /// <summary>Relative URL to download the deterministic PDF. Completed jobs only.</summary>
    [JsonPropertyName("pdf_url")]          public string? PdfUrl { get; set; }

    /// <summary>Relative URL to download the JSON report (the bytes whose
    /// SHA-256 equals <c>hash_content</c> in the PDF footer). Completed jobs only.</summary>
    [JsonPropertyName("json_url")]         public string? JsonUrl { get; set; }

    /// <summary>ISO-8601 UTC timestamp after which both artifacts (PDF and
    /// JSON) are deleted. Completed jobs only.</summary>
    [JsonPropertyName("expires_at")]       public string? ExpiresAt { get; set; }

    /// <summary>Stable snake_case error code. Failed jobs only. See
    /// <c>JobErrorCode</c> for the full list.</summary>
    [JsonPropertyName("error_code")]    public string? ErrorCode { get; set; }

    /// <summary>Human-readable failure reason. Failed jobs only.</summary>
    [JsonPropertyName("error_message")] public string? ErrorMessage { get; set; }
}
