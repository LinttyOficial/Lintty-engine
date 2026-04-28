// Triggers: LNTY-008 (Ports at Boundaries)
// SIN: PaymentClient is `public`, consumed in Sinner.Web (cross-assembly),
// but no `IPaymentClient` interface is declared in Sinner.Domain. Lintty's
// SymbolFinder.FindReferencesAsync sees the cross-assembly ref + missing
// I<TypeName> in Domain -> violation.
using System;

namespace Sinner.Infrastructure;

public sealed class PaymentClient
{
    public string Charge(decimal amount, string currency)
    {
        // Pretend we hit Stripe/PagSeguro/etc.
        return $"AUTH-{Guid.NewGuid():N}-{amount}-{currency}";
    }
}
