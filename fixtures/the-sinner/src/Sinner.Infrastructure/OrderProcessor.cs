// Triggers: LNTY-007 (Dependency cycle - Hard Lock)
// SIN: OrderProcessor lives in Sinner.Infrastructure but references types from
// the Sinner.Application namespace (SmuggledOrderHook, declared in the same
// assembly via _SmuggledIntoApplicationNamespace.cs). The bounded-context
// graph sees Sinner.Infrastructure -> Sinner.Application as a direct symbol
// reference; combined with Sinner.Application -> Sinner.Infrastructure (real
// using directive in OrderService.cs), Tarjan SCC flags the cycle.
using System;
using Sinner.Application; // imports the smuggled namespace inside this assembly

namespace Sinner.Infrastructure;

public sealed class OrderProcessor
{
    public string Process()
    {
        // Direct symbol reference to a type that *appears* to live in
        // Sinner.Application -- this is the back-edge for LNTY-007.
        var hook = new SmuggledOrderHook { Name = "audit" };
        return hook.Name;
    }
}
