// Triggers: LNTY-003 (forbidden instantiation of Infrastructure type)
// SIN: Application directly news an Infrastructure adapter instead of
// receiving the port via DI. Lintty's ObjectCreationExpressionSyntax visitor
// resolves the type to Sinner.Infrastructure.OrderRepository and flags it.
using System;
using System.Threading;
using System.Threading.Tasks;
using Sinner.Domain;
using Sinner.Infrastructure;

namespace Sinner.Application;

public sealed class OrderService
{
    private readonly OrderRepository _repo;

    public OrderService()
    {
        // Direct `new` of an Infrastructure adapter -> LNTY-003.
        _repo = new OrderRepository();
    }

    public Task<Order?> GetAsync(Guid id, CancellationToken ct = default)
        => _repo.GetByIdAsync(id, ct);
}
