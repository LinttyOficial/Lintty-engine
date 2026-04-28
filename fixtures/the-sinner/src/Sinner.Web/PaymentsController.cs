// Triggers: (none directly) - this file exists to make Sinner.Infrastructure's
// PaymentClient be consumed *across* assembly boundaries, which is the
// precondition for LNTY-008 to fire on PaymentClient (public type used
// outside its assembly without a Domain port).
using Sinner.Infrastructure;

namespace Sinner.Web;

public sealed class PaymentsController
{
    private readonly PaymentClient _payments;

    public PaymentsController(PaymentClient payments) => _payments = payments;

    public string Pay(decimal amount) => _payments.Charge(amount, "USD");
}
