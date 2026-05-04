// Saint: respects all canon rules
using System;

namespace Saint.Domain;

/// <summary>
/// Entity inside the Order aggregate. Behaviour-rich, validated on construction.
/// </summary>
public sealed class OrderLine
{
    public string Sku { get; }
    public int Quantity { get; private set; }
    public Money UnitPrice { get; }

    public OrderLine(string sku, int quantity, Money unitPrice)
    {
        if (string.IsNullOrWhiteSpace(sku))
            throw new ArgumentException("SKU is required.", nameof(sku));
        if (quantity <= 0)
            throw new ArgumentException("Quantity must be positive.", nameof(quantity));

        Sku = sku;
        Quantity = quantity;
        UnitPrice = unitPrice;
    }

    public Money LineTotal() => UnitPrice.Multiply(Quantity);

    internal void IncreaseQuantity(int by)
    {
        if (by <= 0)
            throw new ArgumentException("Increment must be positive.", nameof(by));
        Quantity += by;
    }
}
