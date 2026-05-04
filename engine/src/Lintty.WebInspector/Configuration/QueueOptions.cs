namespace Lintty.WebInspector.Configuration;

/// <summary>
/// Bounds on the in-process job queue. V0 runs one job at a time; the cap
/// stops new <c>POST /api/jobs</c> from accepting work that would queue
/// behind too many others (returning 503 instead).
/// </summary>
public sealed class QueueOptions
{
    public const string SectionName = "Queue";

    /// <summary>
    /// Maximum number of pending (queued or running) jobs before new POSTs
    /// are rejected with 503 Service Unavailable.
    /// </summary>
    public int MaxLength { get; set; } = 5;

    /// <summary>
    /// How often the worker polls the SQLite jobs table for queued work, in milliseconds.
    /// </summary>
    public int PollIntervalMs { get; set; } = 500;
}
