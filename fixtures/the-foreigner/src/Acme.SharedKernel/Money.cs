namespace Acme.SharedKernel;

/// <summary>
/// Money value object — fixture demo for the Foreigner case (project name
/// matches no convention, no lintty.yml). Engine should classify this
/// project as Layer.Unknown, skip layer-aware rules on it, and still run
/// LNTY-007 / LNTY-009.
/// </summary>
public readonly record struct Money(decimal Amount, string Currency)
{
    public static Money Zero(string currency) => new(0m, currency);

    public Money Plus(Money other)
    {
        if (!string.Equals(Currency, other.Currency, System.StringComparison.Ordinal))
            throw new System.InvalidOperationException("Currency mismatch.");
        return new Money(Amount + other.Amount, Currency);
    }
}
