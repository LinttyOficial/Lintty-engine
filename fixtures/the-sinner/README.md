# the-sinner

Sinner. Espere grade F, 9+ violacoes, 3 hard locks (LNTY-001 / LNTY-002 / LNTY-007).

Cada arquivo dispara uma regra cirurgicamente:

- `Domain/Order.cs` -> LNTY-001 (Hard Lock): `using System.Data.SqlClient` + `SqlConnection` no Domain.
- `Domain/Customer.cs` -> LNTY-002 (Hard Lock): SQL inline (`SELECT ... FROM ...`) no Domain, literal e via concat.
- `Domain/Inventory.cs` -> LNTY-003: agregado expoe `List<T>` mutavel + instancia tipo nao-puro.
- `Domain/OrderHelper.cs` -> LNTY-006: nome com sufixo `Helper` no Domain (regex blacklist).
- `Application/IOrderRepository.cs` -> LNTY-006 (per spec): port definido em Application, deveria estar em Domain.
- `Application/OrderService.cs` -> LNTY-003: `new OrderRepository()` da Infrastructure.
- `Application/AnemicOrderModel.cs` -> LNTY-005: anemic model puro (LNTY-005 ligada via `lintty.yml`).
- `Infrastructure/OrderRepository.cs` -> LNTY-004 (LLM) + LNTY-008: regra de negocio no repositorio + sem porta em Domain.
- `Infrastructure/_SmuggledIntoApplicationNamespace.cs` + `Infrastructure/OrderProcessor.cs` -> LNTY-007 (Hard Lock): Infrastructure declara um tipo no namespace `Sinner.Application` e o consome localmente, formando ciclo no grafo de bounded-contexts (Application <-> Infrastructure). NuGet impede ciclo de `<ProjectReference>`, entao a sin esta no segundo pass do canon (grafo de namespaces / SCC via Tarjan), exatamente como Ninja #3.
- `Infrastructure/PaymentClient.cs` -> LNTY-008: classe public consumida em Sinner.Web sem `IPaymentClient` em Domain.
- `Infrastructure/MegaRepository.cs` -> LNTY-009: metodo `DoEverything()` excede budget de tokens do slice semantico.
- `Application/SuppressedDiscountService.cs` -> @lintty-ignore VALIDO (>=30 chars de justificativa em LNTY-003).
- `Application/InvalidSuppressionService.cs` -> @lintty-ignore INVALIDO (justificativa "ok") -- engine deve rejeitar e contar a violacao.

`lintty.yml` liga LNTY-005 explicitamente (default OFF no canon).
