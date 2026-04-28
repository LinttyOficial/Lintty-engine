// Triggers: LNTY-003 (suppressed) - VALID suppression, justified.
// This file shows the *valid* suppression path: a >= 30-character business
// justification on a Medium-severity rule. The violation should drop out of
// the score and appear in the PDF's "Sumario de Excecoes" instead.
using System;
using Sinner.Infrastructure;

namespace Sinner.Application;

public sealed class SuppressedDiscountService
{
    public string Quote(decimal amount)
    {
        // @lintty-ignore: LNTY-003 reason="Composition root bootstrap during legacy migration; DI container will own this in sprint 14 once the IoC adapter ships."
        var client = new PaymentClient();
        return client.Charge(amount, "USD");
    }
}
