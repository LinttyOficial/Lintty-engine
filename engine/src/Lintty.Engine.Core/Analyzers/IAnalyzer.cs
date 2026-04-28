using System.Collections.Generic;
using System.Threading.Tasks;
using Lintty.Engine.Core.Model;

namespace Lintty.Engine.Core.Analyzers;

/// <summary>
/// Contract for every LNTY-XXX analyzer. Receives a fully built
/// <see cref="AnalysisContext"/> (compilations + layer map + project graph)
/// and returns factual violations. Analyzers MUST NOT know about each other.
/// </summary>
public interface IAnalyzer
{
    string RuleId { get; }
    Task<IReadOnlyList<Violation>> AnalyzeAsync(AnalysisContext context);
}
