# the-ninja-01

Ninja #1. Linter regex passaria isso. Lintty detecta via Roslyn `SemanticModel.GetConstantValue`.

Tres variantes de SQL escondido em `ProductRepository.cs`:

1. `"SE" + "LECT" + " * FROM products WHERE id = " + id` -- concat folding classico.
2. `string.Concat("SEL", "ECT * FROM x")` -- via API de runtime que Roslyn ainda resolve em compile time.
3. `$"{Prefix}LECT * FROM y"` com `const string Prefix = "SE"` -- interpolacao com argumento constante.

Esperado: 3 violacoes LNTY-002 (Critica, Hard Lock), grade F. Linter regex sobre `LiteralExpressionSyntax` passa as tres como limpas porque nenhum literal isolado contem `SELECT ... FROM`. Lintty diferencia.
