// Triggers: LNTY-002 (Persistence Contamination - Hard Lock) via constant folding
// Three variants of hidden SQL. None of them appear as a complete SQL string
// in any single literal. A naive regex scanner over LiteralExpressionSyntax
// passes the file as clean. Lintty's Roslyn pass uses
// SemanticModel.GetConstantValue on BinaryExpressionSyntax(+) and on
// InterpolatedStringExpressionSyntax to fold the parts at compile time and
// then matches the SQL grammar regex against the resolved value.

namespace Ninja01.Domain;

public sealed class ProductRepository
{
    // Helper constant the third variant pieces back together via interpolation.
    private const string Prefix = "SE";

    /// <summary>
    /// Variant 1: classical concat-with-runtime-arg.
    /// Folds at compile time to: "SELECT * FROM products WHERE id = " + id.
    /// </summary>
    public string FindByIdSql(int id)
    {
        var sql = "SE" + "LECT" + " * FROM products WHERE id = " + id;
        return sql;
    }

    /// <summary>
    /// Variant 2: string.Concat of two string literals with no runtime args.
    /// Folds to "SELECT * FROM x".
    /// </summary>
    public string AllSql()
    {
        return string.Concat("SEL", "ECT * FROM x");
    }

    /// <summary>
    /// Variant 3: interpolated string built from a const fragment.
    /// Roslyn's GetConstantValue resolves $"{Prefix}LECT * FROM y" to
    /// "SELECT * FROM y" because every interpolated argument is a constant.
    /// </summary>
    public string AllYSql()
    {
        return $"{Prefix}LECT * FROM y";
    }
}
