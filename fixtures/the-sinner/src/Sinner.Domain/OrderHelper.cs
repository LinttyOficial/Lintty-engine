// Triggers: LNTY-006 (Ubiquitous Language Leak)
// SIN: type name ends with the blacklisted suffix `Helper`. Canon's
// regex `(Manager|Helper|Util|Utils|Utility)$` hits this without LLM.
using System;

namespace Sinner.Domain;

public static class OrderHelper
{
    public static string FormatId(Guid id) => id.ToString("N");
}
