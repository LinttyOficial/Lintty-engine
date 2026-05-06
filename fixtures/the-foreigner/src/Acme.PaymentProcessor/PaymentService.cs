using Acme.SharedKernel;

namespace Acme.PaymentProcessor;

/// <summary>
/// Synchronous payment service stub. Layer.Unknown by design — name matches
/// no convention pattern; the engine's permissive layer-tagging routes this
/// project through layer-agnostic rules only (LNTY-007 cycles, LNTY-009
/// method size). LNTY-001 and LNTY-008 do NOT fire here.
/// </summary>
public sealed class PaymentService
{
    public Money Charge(Money amount)
    {
        // Trivially short on purpose: stay below LNTY-009's 60 LoC threshold
        // so the Foreigner laudo demonstrates "Unknown but otherwise clean".
        return amount.Plus(Money.Zero(amount.Currency));
    }
}
