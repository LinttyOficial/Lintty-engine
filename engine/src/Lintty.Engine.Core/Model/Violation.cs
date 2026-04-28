using System.Collections.Generic;

namespace Lintty.Engine.Core.Model;

/// <summary>
/// Factual finding emitted by an analyzer. Immutable; ordering and serialization
/// are stable by construction (see Output/JsonReport.cs).
/// </summary>
public sealed record Violation(
    string RuleId,
    Severity Severity,
    bool IsHardLock,
    string File,
    int Line,
    int Column,
    string SymbolFqn,
    string CodeSnippet,
    string AstKind,
    IReadOnlyDictionary<string, string> AdditionalContext);
