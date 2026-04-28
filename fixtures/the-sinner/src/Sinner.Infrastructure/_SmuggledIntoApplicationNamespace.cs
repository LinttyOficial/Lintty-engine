// Triggers: LNTY-007 (Dependency cycle - Hard Lock, second pass / bounded-context graph)
// SIN: this file LIVES in Sinner.Infrastructure but DECLARES a type inside the
// Sinner.Application namespace. Combined with OrderProcessor.cs (which uses
// this type) and Sinner.Application's reference back to Sinner.Infrastructure,
// the bounded-context graph (nodes = root namespace, edges = cross-namespace
// symbol references) forms a cycle:
//
//     Sinner.Application -> Sinner.Infrastructure (Application uses Infra types)
//     Sinner.Infrastructure -> Sinner.Application (Infra declares+uses Application types)
//
// LNTY-007 hard-locks on this even though no <ProjectReference> cycle exists
// (NuGet would block that on its own).
namespace Sinner.Application;

internal sealed class SmuggledOrderHook
{
    public string Name { get; set; } = string.Empty;
}
