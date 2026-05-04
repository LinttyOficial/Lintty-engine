// Saint: respects all canon rules
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Saint.Domain;

namespace Saint.Infrastructure;

/// <summary>
/// Adapter that implements the Domain port.
/// In the Saint, persistence is a thin in-memory store — pure mechanics, zero
/// business logic. (LNTY-004 safe.) A real implementation would use EF Core or
/// Dapper here; the shape stays the same.
/// </summary>
public sealed class OrderRepository : IOrderRepository
{
    private readonly ConcurrentDictionary<OrderId, Order> _store = new();

    public Task<Order?> GetByIdAsync(OrderId id, CancellationToken ct = default)
    {
        _store.TryGetValue(id, out var order);
        return Task.FromResult(order);
    }

    public Task SaveAsync(Order order, CancellationToken ct = default)
    {
        _store[order.Id] = order;
        return Task.CompletedTask;
    }
}
