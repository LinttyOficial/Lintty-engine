// Saint: respects all canon rules
using System;
using System.Threading;
using System.Threading.Tasks;
using Saint.Domain;

namespace Saint.Application;

/// <summary>
/// Application use case. Orchestrates the Domain via the IOrderRepository port.
/// No persistence knowledge here — collaborates with the abstraction.
/// </summary>
public sealed class PlaceOrderHandler
{
    private readonly IOrderRepository _orders;

    public PlaceOrderHandler(IOrderRepository orders)
    {
        _orders = orders ?? throw new ArgumentNullException(nameof(orders));
    }

    public async Task<OrderId> HandleAsync(PlaceOrderRequest request, CancellationToken ct = default)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));

        var order = new Order(OrderId.New(), request.CustomerName, DateTime.UtcNow);

        foreach (var line in request.Lines)
        {
            var price = new Money(line.UnitPrice, request.Currency);
            order.AddLine(new OrderLine(line.Sku, line.Quantity, price));
        }

        order.Confirm();

        await _orders.SaveAsync(order, ct).ConfigureAwait(false);
        return order.Id;
    }
}
