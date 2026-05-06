using System.Collections.Generic;

namespace Lintty.Engine.Reporter.Model;

/// <summary>
/// Display-only copy describing each Canon rule, layered for two audiences:
/// <list type="bullet">
///   <item><c>WhyItMatters</c> — plain-language stakeholder framing (cost,
///   delivery friction, regression risk). No Hexagonal/DDD jargon.</item>
///   <item><c>ArchitecturalReasoning</c> — canonical Hexagonal/DDD vocabulary
///   (Dependency Rule, ports/adapters, Aggregate Root, Bounded Context,
///   Ubiquitous Language) for architects and tech leads.</item>
/// </list>
/// </summary>
internal sealed record RuleCopy(string Title, string WhyItMatters, string ArchitecturalReasoning);

/// <summary>
/// Static, hardcoded copy keyed by <c>rule_id</c>. Lives in C# (not in a
/// resource file or external JSON) to keep the determinism gate green:
/// the PDF binary depends only on inputs that travel inside the assembly.
/// Schema concern: this catalog is a presentation-layer concern only — it
/// does NOT touch <c>schema_version: "1.0"</c> nor any field on the engine
/// <c>ReportDto</c>. Adding/changing copy here cannot affect the JSON.
/// </summary>
internal static class RuleCatalog
{
    public static readonly IReadOnlyDictionary<string, RuleCopy> Rules =
        new Dictionary<string, RuleCopy>
        {
            ["LNTY-001"] = new RuleCopy(
                Title: "Domain Layer Isolation",
                WhyItMatters:
                    "A regra de negócio acaba amarrada ao banco, ao framework web ou a libs externas. " +
                    "Cada troca de tecnologia (banco, ORM, fila) reescreve lógica de produto, atrasa releases e produz regressões silenciosas em features que ninguém esperava ver afetadas.",
                ArchitecturalReasoning:
                    "Viola a Dependency Rule da Arquitetura Hexagonal: o núcleo de Domain depende, direta ou transitivamente, de assemblies de Infrastructure ou Presentation. " +
                    "O fluxo correto é o inverso — adapters externos dependem do Domain através de ports declarados em Domain ou Domain.Abstractions, nunca o contrário."),

            ["LNTY-002"] = new RuleCopy(
                Title: "Persistence Contamination",
                WhyItMatters:
                    "Detalhes de banco vivem dentro da regra de negócio. Qualquer mudança de schema, ORM ou storage vira retrabalho em código de produto, e bugs de dados passam a corromper invariantes de negócio direto na fonte.",
                ArchitecturalReasoning:
                    "SQL literal ou tipos de tecnologia de persistência (EF Core, Dapper, ADO.NET, Mongo, Npgsql) aparecem na camada de Domain. " +
                    "Isso fura a barreira interna do Hexagonal: a persistência é um adapter externo e tem que ser acessada via Repository port. " +
                    "A passagem de constant folding garante que concatenações como \"SE\"+\"LECT\" também sejam pegas — não é antipadrão de string, é antipadrão arquitetural."),

            ["LNTY-003"] = new RuleCopy(
                Title: "Forbidden Instantiation",
                WhyItMatters:
                    "Componentes que deveriam ser plugados via configuração estão sendo criados manualmente no meio do código. Isso quebra testabilidade, esconde dependências reais do módulo e dificulta substituir integrações (ex: trocar um gateway de pagamento) sem caçar new espalhado pelo repositório.",
                ArchitecturalReasoning:
                    "Application ou Presentation instancia diretamente um adapter de Infrastructure (ou um tipo cuja interface I<TypeName> existe em Domain), bypassando o container de DI. " +
                    "O contrato Hexagonal exige que adapters sejam injetados pelos composition roots; o new direto acopla as camadas internas a uma implementação concreta e dilui a inversão de dependência."),

            ["LNTY-006"] = new RuleCopy(
                Title: "Ubiquitous Language Leak",
                WhyItMatters:
                    "Nomes técnicos genéricos (Manager, Helper, Util, Data) escondem o que o código faz no contexto do negócio. Onboarding fica mais lento, conversas entre produto e engenharia divergem, e a base perde rastreabilidade entre regra escrita no contrato e código que a implementa.",
                ArchitecturalReasoning:
                    "Identifiers em Domain não refletem a Ubiquitous Language do Bounded Context. " +
                    "DDD exige que classes, métodos e namespaces no núcleo carreguem o vocabulário do domínio — termos como Manager/Helper/Util sinalizam ausência de modelagem e tendem a virar God Objects que agregam responsabilidades de múltiplos contextos."),

            ["LNTY-007"] = new RuleCopy(
                Title: "Dependency Cycles",
                WhyItMatters:
                    "Existem módulos que dependem uns dos outros em ciclo. Isso impede deploys independentes, faz uma alteração em um canto exigir rebuild e retest do outro, e torna a base impossível de quebrar em times autônomos sem reescrever o grafo de dependências primeiro.",
                ArchitecturalReasoning:
                    "Há ciclos no grafo de ProjectReference ou no grafo de Bounded Contexts (detectados via componentes fortemente conectados de Tarjan). " +
                    "Qualquer arquitetura em camadas — Hexagonal, Onion, Clean — exige um DAG de dependências; ciclos colapsam camadas e violam a separação de Bounded Contexts no nível mais fundamental."),

            ["LNTY-008"] = new RuleCopy(
                Title: "Ports at Boundaries",
                WhyItMatters:
                    "Implementações concretas (clientes de API, repositórios, gateways) são consumidas direto pelo resto do código, sem contrato no meio. Substituir uma integração, mockar em teste ou rodar dois fornecedores em paralelo vira refactor amplo em vez de troca de adapter.",
                ArchitecturalReasoning:
                    "Tipos públicos em Infrastructure consumidos fora de seu próprio assembly não implementam um port (interface I<TypeName>) declarado em Domain ou Domain.Abstractions. " +
                    "É a regra fundamental do Hexagonal: toda travessia de fronteira entre o núcleo e o mundo externo passa por uma porta explícita; sem ela, o adapter vira dependência direta e a inversão se perde."),

            ["LNTY-009"] = new RuleCopy(
                Title: "Method Exceeds Analyzability",
                WhyItMatters:
                    "Existe um método grande demais para ser entendido em um sentido só de leitura. Mudanças nele acumulam risco, code review trava, e bugs sutis se escondem porque ninguém consegue carregar o método inteiro na cabeça ao mesmo tempo.",
                ArchitecturalReasoning:
                    "O método excede o teto de 60 linhas executáveis adotado pelo Canon como proxy estrutural para violação severa de Single Responsibility Principle. " +
                    "Métodos nesse tamanho normalmente concentram múltiplas responsabilidades — validação, persistência, regra de negócio e formatação — que pertencem a Domain Services dedicados ou a métodos privados extraídos. " +
                    "A refatoração em unidades menores precede qualquer outra correção arquitetural, porque métodos longos escondem bugs de invariante e impedem code review honesto."),
        };

    /// <summary>
    /// Returns the copy for a known rule_id, or <c>null</c> if the rule isn't
    /// catalogued (graceful fallback — caller should simply skip the optional
    /// blocks). Should not happen for the 7 rules active in V0.
    /// </summary>
    public static RuleCopy? TryGet(string ruleId)
        => Rules.TryGetValue(ruleId, out var copy) ? copy : null;
}
