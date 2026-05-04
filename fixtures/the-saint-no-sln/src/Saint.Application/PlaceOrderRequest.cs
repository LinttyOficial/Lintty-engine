// Saint: respects all canon rules
using System.Collections.Generic;

namespace Saint.Application;

public sealed record PlaceOrderRequest(
    string CustomerName,
    string Currency,
    IReadOnlyList<PlaceOrderLine> Lines);

public sealed record PlaceOrderLine(string Sku, int Quantity, decimal UnitPrice);
