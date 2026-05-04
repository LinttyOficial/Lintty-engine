namespace Lintty.WebInspector.Configuration;

/// <summary>
/// Per-IP daily quota on <c>POST /api/jobs</c>. Set <c>JobsPerIpPerDay</c> to
/// <c>0</c> (or any non-positive value) to disable the cap entirely — useful
/// while the service isn't public-facing. In production the worker runs jobs
/// serially, so a positive cap should be re-enabled before exposing the API
/// to anonymous traffic to prevent a single abusive IP from starving real users.
/// </summary>
public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimit";

    public int JobsPerIpPerDay { get; set; } = 0;
}
