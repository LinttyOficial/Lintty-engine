using System;
using Microsoft.CodeAnalysis;

namespace Lintty.Engine.Core.Workspace;

/// <summary>
/// Identifies projects that contain test code rather than production code.
///
/// Decision (2026-05-06 self-scan): test code escapes <b>only</b> LNTY-009 —
/// long Arrange/Act/Assert blocks are legitimate in xUnit / NUnit and don't
/// signal Single Responsibility violations. Other rules (LNTY-001 domain
/// isolation, LNTY-002 persistence leaks, LNTY-007 cycles, etc.) still
/// apply to test projects because their architectural concerns don't change
/// based on what kind of code is calling them. If LNTY-006 / LNTY-008 ever
/// produce noisy false positives in tests, opt them in here individually —
/// <b>do not</b> generalize this filter into a global skip.
///
/// Detection is convention-based, deterministic, and cheap:
/// <list type="bullet">
///   <item>csproj filename ends in <c>.Tests.csproj</c> (case-insensitive); or</item>
///   <item>project assembly name ends in <c>.Tests</c>.</item>
/// </list>
/// We deliberately do <b>not</b> sniff for xunit/nunit/mstest in
/// <see cref="Project.MetadataReferences"/> because the manual workspace
/// loader fallback (used when MSBuildLocator can't register, e.g. inside the
/// test runner host) shares the host process's TRUSTED_PLATFORM_ASSEMBLIES
/// across every loaded project — xunit ends up in every project's reference
/// set, including production fixtures. The naming convention is the safer
/// signal and matches what teams actually do.
/// </summary>
public static class TestProjectFilter
{
    public static bool IsTestProject(Project project)
    {
        if (project is null) return false;

        var filePath = project.FilePath ?? string.Empty;
        if (filePath.EndsWith(".Tests.csproj", StringComparison.OrdinalIgnoreCase))
            return true;

        var name = project.Name ?? string.Empty;
        if (name.EndsWith(".Tests", StringComparison.Ordinal))
            return true;

        return false;
    }
}
