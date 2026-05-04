// Saint: respects all canon rules
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Saint.Domain;

/// <summary>
/// Aggregate root. Encapsulates state and business invariants.
/// Exposes lines as a read-only collection — no mutable leak (LNTY-003 safe).
/// </summary>
public sealed class Order
{
    private readonly List<OrderLine> _lines = new();

    public OrderId Id { get; }
    public string CustomerName { get; }
    public DateTime PlacedAtUtc { get; }
    public bool IsConfirmed { get; private set; }

    public IReadOnlyList<OrderLine> Lines => new ReadOnlyCollection<OrderLine>(_lines);

    public Order(OrderId id, string customerName, DateTime placedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(customerName))
            throw new ArgumentException("Customer name is required.", nameof(customerName));

        Id = id;
        CustomerName = customerName;
        PlacedAtUtc = placedAtUtc;
        IsConfirmed = false;
    }

    public void AddLine(OrderLine line)
    {
        if (line is null) throw new ArgumentNullException(nameof(line));
        if (IsConfirmed)
            throw new InvalidOperationException("Cannot modify a confirmed order.");

        var existing = _lines.FirstOrDefault(l =>
            string.Equals(l.Sku, line.Sku, StringComparison.Ordinal));

        if (existing is null)
            _lines.Add(line);
        else
            existing.IncreaseQuantity(line.Quantity);
    }

    public Money Total(string currency)
    {
        var total = Money.Zero(currency);
        foreach (var line in _lines)
            total = total.Add(line.LineTotal());
        return total;
    }

    public void Confirm()
    {
        if (_lines.Count == 0)
            throw new InvalidOperationException("Cannot confirm an empty order.");
        IsConfirmed = true;
    }
}
