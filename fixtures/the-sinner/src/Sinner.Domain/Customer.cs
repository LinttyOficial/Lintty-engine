// Triggers: LNTY-002 (Persistence Contamination - Hard Lock)
// SIN: Domain code assembles a SQL statement inline. Even if not executed
// here, the literal alone is contamination — LNTY-002 hard-locks on the
// presence of SQL grammar in a Domain symbol.
using System;

namespace Sinner.Domain;

public sealed class Customer
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;

    public string BuildLookupSql(int customerId)
    {
        // Plain SELECT literal in the Domain — no constant folding required.
        var sql = "SELECT * FROM Customers WHERE Id = " + customerId;
        return sql;
    }

    public string BuildAuditSql(int customerId)
    {
        // Concatenation form — Lintty's BinaryExpressionSyntax pass + regex
        // hits this even though the literal pieces are split.
        return "SELECT name, email " + "FROM Customers " + "WHERE Id = " + customerId;
    }
}
