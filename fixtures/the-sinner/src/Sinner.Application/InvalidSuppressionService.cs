// Triggers: LNTY-003 (INVALID suppression - justification too short / generic)
// This file shows the *invalid* suppression path: the justification is "ok"
// (3 chars, generic) so Lintty MUST reject the suppression and keep the
// violation counting toward the score.
using Sinner.Infrastructure;

namespace Sinner.Application;

public sealed class InvalidSuppressionService
{
    public string QuickCharge(decimal amount)
    {
        // @lintty-ignore: LNTY-003 reason="ok"
        var client = new PaymentClient();
        return client.Charge(amount, "BRL");
    }
}
