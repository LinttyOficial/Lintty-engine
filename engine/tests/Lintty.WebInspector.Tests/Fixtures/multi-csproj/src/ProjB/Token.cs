namespace ProjB.Domain;

/// <summary>
/// Trivial value object — sibling to ProjA.Money, exists to give the
/// engine something compilable to scan inside ProjB's Domain layer.
/// </summary>
public readonly record struct Token(string Value);
