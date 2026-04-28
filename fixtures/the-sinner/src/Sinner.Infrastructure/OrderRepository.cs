// Triggers: LNTY-004 (Business logic in repository) + LNTY-008 (no port in Domain)
// SIN: This adapter does real business reasoning (discount calculation) inside
// what should be a thin persistence boundary -- LNTY-004 LLM pre-filter hits.
// Also: there is no IOrderRepository declared in Sinner.Domain (the port lives
// in Sinner.Application instead), so Infrastructure exposes a public type used
// outside the assembly without a Domain port -> LNTY-008.
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Sinner.Domain;

namespace Sinner.Infrastructure;

public sealed class OrderRepository
{
    private readonly ConcurrentDictionary<Guid, Order> _store = new();

    public Task<Order?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        _store.TryGetValue(id, out var order);
        return Task.FromResult(order);
    }

    /// <summary>
    /// Repository contains business reasoning (loyalty discount, tier branching)
    /// instead of pure persistence. Cyclomatic complexity > 3, plenty of
    /// comparisons -- LNTY-004 pre-filter Roslyn pass picks this up and ships
    /// it to the LLM, which should rule VIOLATION.
    /// </summary>
    public Task SaveAsync(Order order, CancellationToken ct = default)
    {
        // Business rules buried in Infrastructure:
        if (order.Total > 1000m)
        {
            order.Total *= 0.90m; // 10% loyalty discount
        }
        else if (order.Total > 500m)
        {
            order.Total *= 0.95m; // 5% mid-tier
        }
        else if (order.Total > 100m)
        {
            order.Total *= 0.98m; // 2% retention
        }

        if (string.IsNullOrEmpty(order.CustomerName))
            throw new InvalidOperationException("Customer is required.");

        _store[order.Id] = order;
        return Task.CompletedTask;
    }
}
