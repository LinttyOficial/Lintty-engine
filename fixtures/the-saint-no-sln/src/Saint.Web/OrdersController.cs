// Saint: respects all canon rules
using System.Threading;
using System.Threading.Tasks;
using Saint.Application;
using Saint.Domain;

namespace Saint.Web;

/// <summary>
/// Minimal controller-equivalent. Doesn't depend on ASP.NET to keep the fixture
/// self-contained — the shape mirrors a real Web layer (composition root, thin
/// dispatch to Application). A real Saint.Api would wire DI and HTTP routes.
/// </summary>
public sealed class OrdersController
{
    private readonly PlaceOrderHandler _placeOrder;

    public OrdersController(PlaceOrderHandler placeOrder)
    {
        _placeOrder = placeOrder;
    }

    public Task<OrderId> Post(PlaceOrderRequest request, CancellationToken ct = default)
        => _placeOrder.HandleAsync(request, ct);
}
