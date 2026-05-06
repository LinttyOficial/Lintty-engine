using Acme.PaymentProcessor;
using Acme.SharedKernel;

namespace Acme.Webhooks;

/// <summary>
/// Inbound-webhook handler. Same Layer.Unknown classification as the rest
/// of the Foreigner fixture; the file demonstrates a cross-project edge
/// (Webhooks → PaymentProcessor) so LNTY-007's bounded-context graph still
/// has work to do on Unknown projects.
/// </summary>
public sealed class WebhookHandler
{
    private readonly PaymentService _payments;

    public WebhookHandler(PaymentService payments)
    {
        _payments = payments;
    }

    public Money Handle(Money amount) => _payments.Charge(amount);
}
