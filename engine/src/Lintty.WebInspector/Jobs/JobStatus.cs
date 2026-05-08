namespace Lintty.WebInspector.Jobs;

/// <summary>
/// Status string constants used in the SQLite jobs table and HTTP responses.
/// Kept as constants (not enum) because they cross the wire as JSON strings
/// and we want grep-ability to be the same on both sides.
/// </summary>
public static class JobStatus
{
    public const string Queued = "queued";
    public const string Running = "running";
    public const string Completed = "completed";
    public const string Failed = "failed";
}

/// <summary>
/// Sub-state of a <see cref="JobStatus.Running"/> job for UX (so the polling
/// frontend can render "cloning..." vs "analyzing..."). Mirrors §5 of
/// <c>docs/13-web-inspector.md</c>.
/// </summary>
public static class JobStage
{
    public const string Cloning = "cloning";
    public const string Restoring = "restoring";
    public const string Analyzing = "analyzing";
    public const string Rendering = "rendering";
}

/// <summary>
/// Stable error_code values for failed jobs (§7 of the spec).
///
/// ADR 0006 added the target-resolution codes (<see cref="NoTarget"/>,
/// <see cref="AmbiguousTarget"/>, <see cref="TargetNotFound"/>,
/// <see cref="InvalidConfig"/>). <see cref="NoSln"/> is kept as a legacy
/// alias of <see cref="NoTarget"/> for the specific case of "0 .sln in the
/// clone, no lintty.yml, and the .csproj count is not exactly 1" — see
/// ADR 0006 §8.2. Removed in v1 of the HTTP contract.
/// </summary>
public static class JobErrorCode
{
    public const string CloneFailed = "clone_failed";
    public const string NoSln = "no_sln";
    public const string NoTarget = "no_target";
    public const string AmbiguousTarget = "ambiguous_target";
    public const string TargetNotFound = "target_not_found";
    public const string InvalidConfig = "invalid_config";
    public const string CompileFailed = "compile_failed";
    public const string LayerTaggingError = "layer_tagging_error";
    public const string Timeout = "timeout";
    public const string InternalError = "internal_error";

    /// <summary>
    /// ADR 0007 Apêndice E §E.9. Org-bound scans only. The user who added
    /// a private repo (<c>repos.added_by_user_id</c>) has no active row in
    /// <c>github_user_tokens</c> — token absent or soft-revoked. The scan
    /// can't authenticate to GitHub for the clone, so it fails fast with
    /// this code so the frontend can offer a "reconnect GitHub" CTA.
    ///
    /// <para>
    /// The <c>scans</c> schema (PR 1) keeps a single nullable
    /// <c>error</c> column rather than splitting code/message; the worker
    /// prefixes the message with this literal so callers / tests can match
    /// it via <c>error.Contains(JobErrorCode.GithubTokenRevoked)</c>.
    /// </para>
    /// </summary>
    public const string GithubTokenRevoked = "GITHUB_TOKEN_REVOKED";
}
