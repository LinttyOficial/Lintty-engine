namespace ProjA.Domain;

/// <summary>
/// Trivial value object — exists to give the engine something compilable
/// to scan inside the Domain layer. The contents don't trigger any rule.
/// </summary>
public readonly record struct Money(decimal Amount, string Currency);
