namespace Lintty.Engine.Core.Model;

/// <summary>
/// Architectural layer classification used by every analyzer that needs to
/// know "is this Domain code?" before raising a hard lock. The order mirrors
/// the canon's layered model: Domain at the centre, Presentation at the edge.
/// </summary>
public enum Layer
{
    Unknown = 0,
    Domain = 1,
    DomainAbstractions = 2,
    Application = 3,
    Infrastructure = 4,
    Presentation = 5,
}

/// <summary>String tags used in the JSON report (snake_case, lower).</summary>
public static class LayerTagStrings
{
    public static string ToTag(this Layer layer) => layer switch
    {
        Layer.Domain => "domain",
        Layer.DomainAbstractions => "domain.abstractions",
        Layer.Application => "application",
        Layer.Infrastructure => "infrastructure",
        Layer.Presentation => "presentation",
        _ => "unknown",
    };
}
