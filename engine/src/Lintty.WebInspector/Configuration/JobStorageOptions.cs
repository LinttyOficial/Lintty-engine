namespace Lintty.WebInspector.Configuration;

/// <summary>
/// Where the per-job artifacts (PDF + JSON) live on disk. The job database
/// itself moved to Postgres in ADR 0007 Sprint 1 (see
/// <see cref="PostgresOptions"/>); this section now configures only the
/// filesystem locations the worker writes to.
/// </summary>
/// <remarks>
/// Path is resolved relative to <see cref="System.IO.Directory.GetCurrentDirectory"/>
/// at startup unless an absolute path is supplied. The Web Inspector never
/// writes outside this root.
///
/// In ADR 0007 §3.8 this surface will graduate into <c>IArtifactStore</c>
/// (LocalArtifactStore + S3ArtifactStore) — for Sprint 1 the local layout is
/// preserved so the cross-determinism gate stays valid bit-for-bit.
/// </remarks>
public sealed class JobStorageOptions
{
    public const string SectionName = "JobStorage";

    /// <summary>
    /// Root directory for per-job artifact folders.
    /// Default: <c>var/lintty</c> (relative to the process cwd).
    /// </summary>
    public string Root { get; set; } = "var/lintty";

    /// <summary>
    /// Subdirectory under the root that holds per-job artifact folders.
    /// </summary>
    public string JobsDir { get; set; } = "jobs";
}
