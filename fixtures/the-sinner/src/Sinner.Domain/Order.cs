// Triggers: LNTY-001 (Domain Layer Isolation - Hard Lock)
// SIN: Domain assembly imports System.Data.SqlClient and uses SqlConnection.
// Lintty resolves the symbol via SemanticModel.GetSymbolInfo and finds the
// containing assembly is classified as persistence -> hard-lock violation.
using System;
using System.Data.SqlClient;

namespace Sinner.Domain;

public sealed class Order
{
    public Guid Id { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public decimal Total { get; set; }

    /// <summary>
    /// The Domain has no business hitting the database — this method makes
    /// LNTY-001 unambiguous via a direct SqlConnection symbol use.
    /// </summary>
    public void RefreshFromDatabase(string connectionString)
    {
        using var conn = new SqlConnection(connectionString);
        conn.Open();
        // ... pretend we read state into the aggregate
        conn.Close();
    }
}
