using System;

namespace Lintty.WebInspector.Jobs;

/// <summary>
/// Snapshot of a job row. Mutable POCO because the worker reads, mutates, and
/// writes it back in stages (queued → running → completed/failed).
///
/// Same type is used by both Dapper (PostgresJobStore — hot path:
/// UPDATE ... RETURNING; FOR UPDATE SKIP LOCKED) and EF (LinttyDbContext owns
/// the schema in Sprint 2). Property names are PascalCase; column names map to
/// snake_case via <c>EFCore.NamingConventions</c> for EF and via Dapper's
/// case-insensitive binding for Npgsql commands.
///
/// Sprint 2 keeps the anonymous V0 contract bit-for-bit (no <c>org_id</c>
/// required, no auth required for POST). Tenant-bound scans land in Sprint 3.
/// </summary>
public sealed class Job
{
    public string Id { get; set; } = string.Empty;
    public string GithubUrl { get; set; } = string.Empty;
    public string? Ref { get; set; }
    public string? SolutionPath { get; set; }
    public string Status { get; set; } = JobStatus.Queued;
    public string? Stage { get; set; }
    public DateTime CreatedAt { get; set; }
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
