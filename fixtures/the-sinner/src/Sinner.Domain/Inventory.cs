// Triggers: LNTY-003 (Forbidden Instantiation / aggregate leak)
// SIN: aggregate exposes a mutable List<T> directly. External code can
// mutate inventory without going through the aggregate's invariants.
// (Aggregate boundary is broken — Lintty flags this as a state-leak case
// of LNTY-003's "instantiate-and-leak" pattern.)
using System;
using System.Collections.Generic;

namespace Sinner.Domain;

public sealed class Inventory
{
    public Guid WarehouseId { get; set; }

    // Aggregate-leak: caller can do `inventory.Items.Clear()` from outside.
    public List<InventoryItem> Items { get; set; } = new List<InventoryItem>();

    public void Add(InventoryItem item)
    {
        // Even worse: we instantiate an Infrastructure-owned helper from Domain.
        var leaker = new InventoryAuditLogger();
        leaker.Log(item);
        Items.Add(item);
    }
}

public sealed class InventoryItem
{
    public string Sku { get; set; } = string.Empty;
    public int Count { get; set; }
}

// Stand-in for an Infrastructure-flavored type that Domain shouldn't `new`.
internal sealed class InventoryAuditLogger
{
    public void Log(InventoryItem item) { /* pretend we write to disk */ }
}
