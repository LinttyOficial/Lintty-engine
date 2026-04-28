// Triggers: LNTY-005 (Anemic Domain Model) - opt-in, requires lintty.yml toggle.
// SIN: pure container of getters/setters with zero behaviour. When the
// project's lintty.yml turns LNTY-005 ON, the LLM pre-filter ships this
// to the model and it should rule VIOLATION.
using System;

namespace Sinner.Domain.Anemic;

public sealed class AnemicOrderModel
{
    public Guid Id { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public DateTime PlacedAt { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public int LineCount { get; set; }
    public string Notes { get; set; } = string.Empty;
}
