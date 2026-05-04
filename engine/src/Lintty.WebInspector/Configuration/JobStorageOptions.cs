namespace Lintty.WebInspector.Configuration;

/// <summary>
/// Where the SQLite job database and per-job artifacts (PDF + JSON) live.
/// Path is resolved relative to <see cref="System.IO.Directory.GetCurrentDirectory"/>
/// at startup unless an absolute path is supplied. The Web Inspector never
/// writes outside this root.
/// </summary>
public sealed class JobStorageOptions
{
    public const string SectionName = "JobStorage";

    /// <summary>
    /// Root directory for the SQLite database and per-job artifact folders.
    /// Default: <c>var/lintty</c> (relative to the process cwd).
    /// </summary>
    public string Root { get; set; } = "var/lintty";

    /// <summary>
    /// SQLite file path under the root.
    /// </summary>
    public string DatabaseFile { get; set; } = "jobs.sqlite";

    /// <summary>
    /// Subdirectory under the root that holds per-job artifact folders.
    /// </summary>
    public string JobsDir { get; set; } = "jobs";
}
