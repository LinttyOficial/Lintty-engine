// Triggers: LNTY-006 (interface defined in wrong layer)
// SIN: per the user fixture spec, this port lives in Application but should
// live in Domain so that Infrastructure-side adapters depend on a Domain
// abstraction. Defining it here puts the dependency arrow backwards.
using System.Threading;
using System.Threading.Tasks;
using Sinner.Domain;

namespace Sinner.Application;

public interface IOrderRepository
{
    Task<Order?> GetByIdAsync(System.Guid id, CancellationToken ct = default);
    Task SaveAsync(Order order, CancellationToken ct = default);
}
