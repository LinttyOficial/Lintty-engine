using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Lintty.WebInspector.Persistence;
using Lintty.WebInspector.Repos.Preflight;
using Xunit;

namespace Lintty.WebInspector.Tests;

/// <summary>
/// ADR 0007 Sprint 3 / PR S1 — preflight + scan-target curation. Covers:
/// <list type="bullet">
///   <item><description>Saved selection short-circuits to <c>ready</c>.</description></item>
///   <item><description>Public repo with a single root .sln auto-detects.</description></item>
///   <item><description>Multiple csprojs with no .sln → <c>needs_config</c>.</description></item>
///   <item><description>Empty repo → <c>no_dotnet_project</c>.</description></item>
///   <item><description>Cross-tenant lookup → 404 (§3.5).</description></item>
///   <item><description>PUT validates path-in-candidates and combination rules.</description></item>
/// </list>
///
/// All tests use <see cref="WebInspectorFactory.FakeRepoTargetDiscovery"/>
/// so no test ever calls api.github.com (golden rule of the contract suite).
/// </summary>
public sealed class RepoPreflightTests : WebInspectorTestBase
{
    public RepoPreflightTests(PostgresFixture pg) : base(pg) { }

    // ── GET /api/repos/{id}/preflight ─────────────────────────────────────

    [Fact]
    public async Task Preflight_With_Saved_ScanProjects_Returns_Ready_With_Selection()
    {
        await ResetAsync();
        await using var factory = CreateFactory();
        var (client, orgId, _) = await SignUpAndGetAuthedClientAsync(
            factory, "alice@example.com", "Alice Co");

        var repoId = await AddRepoAsync(client, "https://github.com/lintty-demo/the-saint");

        // Persist a saved selection directly on the row so we don't depend
        // on the PUT endpoint's validation here. The PUT path has its own
        // tests below.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LinttyDbContext>();
            var row = await db.Repos.FirstAsync(r => r.Id == repoId);
            row.ScanProjects = new[] { "Saint.sln" };
            await db.SaveChangesAsync();
        }

        // Discovery results — same .sln present plus a couple of csprojs.
        // Status must be `ready` (saved selection wins; auto-detect is
        // skipped) and `scanProjects` echoes what we stored.
        factory.FakeRepoTargetDiscovery.ResultsByFullName["lintty-demo/the-saint"] =
            new RepoTargetDiscoveryResult(
                SlnFiles: new[] { "Saint.sln" },
                CsprojFiles: new[] { "src/Saint.Domain/Saint.Domain.csproj" },
                LinttyYmlFiles: Array.Empty<string>(),
                Truncated: false);

        var resp = await client.GetAsync($"/api/repos/{repoId}/preflight");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());

        Assert.Equal(PreflightStatus.Ready, doc.RootElement.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("autoDetected").ValueKind);
        var saved = doc.RootElement.GetProperty("scanProjects");
        Assert.Equal(JsonValueKind.Array, saved.ValueKind);
        Assert.Equal(1, saved.GetArrayLength());
        Assert.Equal("Saint.sln", saved[0].GetString());
        Assert.Equal(2, doc.RootElement.GetProperty("candidates").GetArrayLength());
        Assert.False(doc.RootElement.GetProperty("truncated").GetBoolean());

        // Sanity: orgId is referenced by the cross-tenant test below.
        Assert.True(orgId > 0);
    }

    [Fact]
    public async Task Preflight_Public_Repo_With_Single_Sln_Auto_Detects()
    {
        await ResetAsync();
        await using var factory = CreateFactory();
        var (client, _, _) = await SignUpAndGetAuthedClientAsync(
            factory, "bob@example.com", "Bob Inc");

        var repoId = await AddRepoAsync(client, "https://github.com/lintty-demo/the-saint");

        factory.FakeRepoTargetDiscovery.ResultsByFullName["lintty-demo/the-saint"] =
            new RepoTargetDiscoveryResult(
                SlnFiles: new[] { "Saint.sln" },
                CsprojFiles: new[]
                {
                    "src/Saint.Application/Saint.Application.csproj",
                    "src/Saint.Domain/Saint.Domain.csproj",
                },
                LinttyYmlFiles: Array.Empty<string>(),
                Truncated: false);

        var resp = await client.GetAsync($"/api/repos/{repoId}/preflight");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());

        Assert.Equal(PreflightStatus.Ready, doc.RootElement.GetProperty("status").GetString());
        var auto = doc.RootElement.GetProperty("autoDetected");
        Assert.Equal(JsonValueKind.Object, auto.ValueKind);
        Assert.Equal(CandidateKind.Sln, auto.GetProperty("kind").GetString());
        Assert.Equal("Saint.sln", auto.GetProperty("path").GetString());
        // No saved selection.
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("scanProjects").ValueKind);
        // Sln first, then csprojs alpha — matches BuildOrderedCandidates.
        var candidates = doc.RootElement.GetProperty("candidates");
        Assert.Equal(3, candidates.GetArrayLength());
        Assert.Equal("Saint.sln", candidates[0].GetProperty("path").GetString());
    }

    [Fact]
    public async Task Preflight_Public_Repo_With_LinttyYml_Auto_Detects_Yaml_Over_Sln()
    {
        await ResetAsync();
        await using var factory = CreateFactory();
        var (client, _, _) = await SignUpAndGetAuthedClientAsync(
            factory, "yaml@example.com", "Yaml Co");

        var repoId = await AddRepoAsync(client, "https://github.com/lintty-demo/the-saint-no-sln");

        // Both root lintty.yml AND a root .sln present — yaml wins.
        factory.FakeRepoTargetDiscovery.ResultsByFullName["lintty-demo/the-saint-no-sln"] =
            new RepoTargetDiscoveryResult(
                SlnFiles: new[] { "Saint.sln" },
                CsprojFiles: Array.Empty<string>(),
                LinttyYmlFiles: new[] { "lintty.yml" },
                Truncated: false);

        var resp = await client.GetAsync($"/api/repos/{repoId}/preflight");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());

        Assert.Equal(PreflightStatus.Ready, doc.RootElement.GetProperty("status").GetString());
        var auto = doc.RootElement.GetProperty("autoDetected");
        Assert.Equal(CandidateKind.Yaml, auto.GetProperty("kind").GetString());
        Assert.Equal("lintty.yml", auto.GetProperty("path").GetString());
    }

    [Fact]
    public async Task Preflight_Public_Repo_With_Multiple_Csprojs_Returns_NeedsConfig()
    {
        await ResetAsync();
        await using var factory = CreateFactory();
        var (client, _, _) = await SignUpAndGetAuthedClientAsync(
            factory, "carol@example.com", "Carol Co");

        var repoId = await AddRepoAsync(client, "https://github.com/lintty-demo/multi-csproj");

        factory.FakeRepoTargetDiscovery.ResultsByFullName["lintty-demo/multi-csproj"] =
            new RepoTargetDiscoveryResult(
                SlnFiles: Array.Empty<string>(),
                CsprojFiles: new[]
                {
                    "src/ProjA/ProjA.Domain.csproj",
                    "src/ProjB/ProjB.Domain.csproj",
                },
                LinttyYmlFiles: Array.Empty<string>(),
                Truncated: false);

        var resp = await client.GetAsync($"/api/repos/{repoId}/preflight");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());

        Assert.Equal(PreflightStatus.NeedsConfig, doc.RootElement.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("autoDetected").ValueKind);
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("scanProjects").ValueKind);
        Assert.Equal(2, doc.RootElement.GetProperty("candidates").GetArrayLength());
        Assert.False(string.IsNullOrEmpty(doc.RootElement.GetProperty("reason").GetString()));
    }

    [Fact]
    public async Task Preflight_Empty_Repo_Returns_NoDotnetProject()
    {
        await ResetAsync();
        await using var factory = CreateFactory();
        var (client, _, _) = await SignUpAndGetAuthedClientAsync(
            factory, "dave@example.com", "Dave Co");

        var repoId = await AddRepoAsync(client, "https://github.com/lintty-demo/empty");

        // Default fake behaviour: no entry → empty discovery.
        var resp = await client.GetAsync($"/api/repos/{repoId}/preflight");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());

        Assert.Equal(PreflightStatus.NoDotnetProject, doc.RootElement.GetProperty("status").GetString());
        Assert.Equal(0, doc.RootElement.GetProperty("candidates").GetArrayLength());
    }

    [Fact]
    public async Task Preflight_Cross_Tenant_Returns_404()
    {
        await ResetAsync();
        await using var factory = CreateFactory();
        var (clientA, _, _) = await SignUpAndGetAuthedClientAsync(
            factory, "tenantA@example.com", "Tenant A");
        var (clientB, _, _) = await SignUpAndGetAuthedClientAsync(
            factory, "tenantB@example.com", "Tenant B");

        // Org A registers the repo; Org B asks for its preflight.
        var repoId = await AddRepoAsync(clientA, "https://github.com/lintty-demo/the-saint");

        var resp = await clientB.GetAsync($"/api/repos/{repoId}/preflight");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.Equal("not_found", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Preflight_Without_Cookie_Returns_401()
    {
        await ResetAsync();
        await using var factory = CreateFactory();
        // Need a real repo id so we know we're not just hitting the
        // routing 404; the auth gate fires before the EF lookup.
        var (authed, _, _) = await SignUpAndGetAuthedClientAsync(
            factory, "auth@example.com", "Auth Co");
        var repoId = await AddRepoAsync(authed, "https://github.com/lintty-demo/the-saint");

        var anon = factory.CreateClient();
        var resp = await anon.GetAsync($"/api/repos/{repoId}/preflight");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    // ── PUT /api/repos/{id}/scan-target ───────────────────────────────────

    [Fact]
    public async Task SetScanTarget_With_Valid_Sln_Updates_Row()
    {
        await ResetAsync();
        await using var factory = CreateFactory();
        var (client, _, _) = await SignUpAndGetAuthedClientAsync(
            factory, "set@example.com", "Set Co");

        var repoId = await AddRepoAsync(client, "https://github.com/lintty-demo/the-saint");

        factory.FakeRepoTargetDiscovery.ResultsByFullName["lintty-demo/the-saint"] =
            new RepoTargetDiscoveryResult(
                SlnFiles: new[] { "Saint.sln" },
                CsprojFiles: new[] { "src/Saint.Domain/Saint.Domain.csproj" },
                LinttyYmlFiles: Array.Empty<string>(),
                Truncated: false);

        var resp = await client.PutAsJsonAsync($"/api/repos/{repoId}/scan-target", new
        {
            projects = new[] { "Saint.sln" },
        });
        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);

        // Confirm the row was updated.
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LinttyDbContext>();
        var row = await db.Repos.AsNoTracking().FirstAsync(r => r.Id == repoId);
        Assert.NotNull(row.ScanProjects);
        Assert.Single(row.ScanProjects!);
        Assert.Equal("Saint.sln", row.ScanProjects![0]);

        // Re-running preflight now reports `ready` with the saved selection.
        var refreshed = await client.GetAsync($"/api/repos/{repoId}/preflight");
        using var doc = JsonDocument.Parse(await refreshed.Content.ReadAsStringAsync());
        Assert.Equal(PreflightStatus.Ready, doc.RootElement.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("autoDetected").ValueKind);
        Assert.Equal("Saint.sln",
            doc.RootElement.GetProperty("scanProjects")[0].GetString());
    }

    [Fact]
    public async Task SetScanTarget_With_Empty_List_Clears_Saved_Selection()
    {
        await ResetAsync();
        await using var factory = CreateFactory();
        var (client, _, _) = await SignUpAndGetAuthedClientAsync(
            factory, "clear@example.com", "Clear Co");

        var repoId = await AddRepoAsync(client, "https://github.com/lintty-demo/the-saint");
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LinttyDbContext>();
            var row = await db.Repos.FirstAsync(r => r.Id == repoId);
            row.ScanProjects = new[] { "Saint.sln" };
            await db.SaveChangesAsync();
        }

        var resp = await client.PutAsJsonAsync($"/api/repos/{repoId}/scan-target", new
        {
            projects = Array.Empty<string>(),
        });
        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);

        await using var scope2 = factory.Services.CreateAsyncScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<LinttyDbContext>();
        var row2 = await db2.Repos.AsNoTracking().FirstAsync(r => r.Id == repoId);
        Assert.Null(row2.ScanProjects);
    }

    [Fact]
    public async Task SetScanTarget_With_Multiple_Csprojs_Persists_All()
    {
        await ResetAsync();
        await using var factory = CreateFactory();
        var (client, _, _) = await SignUpAndGetAuthedClientAsync(
            factory, "multi@example.com", "Multi Co");

        var repoId = await AddRepoAsync(client, "https://github.com/lintty-demo/multi-csproj");

        factory.FakeRepoTargetDiscovery.ResultsByFullName["lintty-demo/multi-csproj"] =
            new RepoTargetDiscoveryResult(
                SlnFiles: Array.Empty<string>(),
                CsprojFiles: new[]
                {
                    "src/ProjA/ProjA.Domain.csproj",
                    "src/ProjB/ProjB.Domain.csproj",
                },
                LinttyYmlFiles: Array.Empty<string>(),
                Truncated: false);

        var resp = await client.PutAsJsonAsync($"/api/repos/{repoId}/scan-target", new
        {
            projects = new[]
            {
                "src/ProjA/ProjA.Domain.csproj",
                "src/ProjB/ProjB.Domain.csproj",
            },
        });
        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LinttyDbContext>();
        var row = await db.Repos.AsNoTracking().FirstAsync(r => r.Id == repoId);
        Assert.NotNull(row.ScanProjects);
        Assert.Equal(2, row.ScanProjects!.Length);
    }

    [Fact]
    public async Task SetScanTarget_With_Path_Not_In_Candidates_Returns_400()
    {
        await ResetAsync();
        await using var factory = CreateFactory();
        var (client, _, _) = await SignUpAndGetAuthedClientAsync(
            factory, "tampering@example.com", "Tampering Co");

        var repoId = await AddRepoAsync(client, "https://github.com/lintty-demo/the-saint");

        factory.FakeRepoTargetDiscovery.ResultsByFullName["lintty-demo/the-saint"] =
            new RepoTargetDiscoveryResult(
                SlnFiles: new[] { "Saint.sln" },
                CsprojFiles: Array.Empty<string>(),
                LinttyYmlFiles: Array.Empty<string>(),
                Truncated: false);

        // Forge a path that isn't in the candidate set — even though the
        // file extension is valid, the service rejects it because the
        // dashboard never offered that option.
        var resp = await client.PutAsJsonAsync($"/api/repos/{repoId}/scan-target", new
        {
            projects = new[] { "evil/Other.sln" },
        });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.Equal("invalid_scan_projects", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task SetScanTarget_With_Mixed_Sln_And_Csproj_Returns_400()
    {
        await ResetAsync();
        await using var factory = CreateFactory();
        var (client, _, _) = await SignUpAndGetAuthedClientAsync(
            factory, "mixed@example.com", "Mixed Co");

        var repoId = await AddRepoAsync(client, "https://github.com/lintty-demo/the-saint");

        factory.FakeRepoTargetDiscovery.ResultsByFullName["lintty-demo/the-saint"] =
            new RepoTargetDiscoveryResult(
                SlnFiles: new[] { "Saint.sln" },
                CsprojFiles: new[] { "src/Saint.Domain/Saint.Domain.csproj" },
                LinttyYmlFiles: Array.Empty<string>(),
                Truncated: false);

        // Combination invalid: 1 sln + 1 csproj. Combination check fires
        // before the candidate-set check, so even though both paths exist
        // in the discovery, we reject with 400.
        var resp = await client.PutAsJsonAsync($"/api/repos/{repoId}/scan-target", new
        {
            projects = new[] { "Saint.sln", "src/Saint.Domain/Saint.Domain.csproj" },
        });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.Equal("invalid_scan_projects", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task SetScanTarget_With_Multiple_Slns_Returns_400()
    {
        await ResetAsync();
        await using var factory = CreateFactory();
        var (client, _, _) = await SignUpAndGetAuthedClientAsync(
            factory, "twosln@example.com", "TwoSln Co");

        var repoId = await AddRepoAsync(client, "https://github.com/lintty-demo/the-saint");

        factory.FakeRepoTargetDiscovery.ResultsByFullName["lintty-demo/the-saint"] =
            new RepoTargetDiscoveryResult(
                SlnFiles: new[] { "A.sln", "B.sln" },
                CsprojFiles: Array.Empty<string>(),
                LinttyYmlFiles: Array.Empty<string>(),
                Truncated: false);

        var resp = await client.PutAsJsonAsync($"/api/repos/{repoId}/scan-target", new
        {
            projects = new[] { "A.sln", "B.sln" },
        });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task SetScanTarget_Cross_Tenant_Returns_404()
    {
        await ResetAsync();
        await using var factory = CreateFactory();
        var (clientA, _, _) = await SignUpAndGetAuthedClientAsync(
            factory, "tenantA-put@example.com", "Tenant A Put");
        var (clientB, _, _) = await SignUpAndGetAuthedClientAsync(
            factory, "tenantB-put@example.com", "Tenant B Put");

        var repoId = await AddRepoAsync(clientA, "https://github.com/lintty-demo/the-saint");

        // Discovery seeds matter for the org A path; org B should never
        // reach discovery because the cross-tenant check fires first.
        factory.FakeRepoTargetDiscovery.ResultsByFullName["lintty-demo/the-saint"] =
            new RepoTargetDiscoveryResult(
                SlnFiles: new[] { "Saint.sln" },
                CsprojFiles: Array.Empty<string>(),
                LinttyYmlFiles: Array.Empty<string>(),
                Truncated: false);

        var resp = await clientB.PutAsJsonAsync($"/api/repos/{repoId}/scan-target", new
        {
            projects = new[] { "Saint.sln" },
        });
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    /// <summary>
    /// Adds a public repo via <c>POST /api/repos</c> using
    /// <see cref="WebInspectorFactory.FakeGitHub"/>'s defaults (Exists=true,
    /// public). Returns the new repo's id.
    /// </summary>
    private static async Task<long> AddRepoAsync(HttpClient client, string url)
    {
        var resp = await client.PostAsJsonAsync("/api/repos", new { githubUrl = url });
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("id").GetInt64();
    }
}
