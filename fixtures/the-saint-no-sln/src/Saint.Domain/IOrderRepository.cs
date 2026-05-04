// Saint: respects all canon rules
using System.Threading;
using System.Threading.Tasks;

namespace Saint.Domain;

/// <summary>
/// Port (in the Hexagonal sense) declared in Domain.
/// Infrastructure provides the adapter — Domain knows nothing about persistence.
/// </summary>
public interface IOrderRepository
{
    Task<Order?> GetByIdAsync(OrderId id, CancellationToken ct = default);
    Task SaveAsync(Order order, CancellationToken ct = default);
}
