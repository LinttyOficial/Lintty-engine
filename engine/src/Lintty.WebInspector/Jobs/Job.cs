using System;

namespace Lintty.WebInspector.Jobs;

/// <summary>
/// Snapshot of a job row in SQLite. Mutable POCO because the worker reads,
/// mutates, and writes it back in stages (queued → running → completed/failed).
/// </summary>
public sealed class Job
{
    public string Id { get; init; } = string.Empty;
    public string GithubUrl { get; init; } = string.Empty;
    public string? Ref { get; set; }
    public string? SolutionPath { get; set; }
    public string Status { get; set; } = JobStatus.Queued;
    public string? Stage { get; set; }
    public DateTime CreatedAt { get; init; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public string? PdfPath { get; set; }
    public string? JsonPath { get; set; }
    public int? Score { get; set; }
    public string? Grade { get; set; }
    public string? CanonVersion { get; set; }
    public int? ViolationCount { get; set; }
    public int? HardLocksOpen { get; set; }
}
