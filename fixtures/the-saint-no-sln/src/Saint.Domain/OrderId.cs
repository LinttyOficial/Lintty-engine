// Saint: respects all canon rules
using System;

namespace Saint.Domain;

/// <summary>
/// Strongly-typed identifier for an Order aggregate. Value object, immutable.
/// </summary>
public readonly record struct OrderId(Guid Value)
{
    public static OrderId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString("D");
}
