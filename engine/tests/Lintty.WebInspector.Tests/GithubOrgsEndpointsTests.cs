using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Lintty.WebInspector.Auth;
using Lintty.WebInspector.Github;
using Lintty.WebInspector.Persistence;

namespace Lintty.WebInspector.Tests;

/// <summary>
/// ADR 0007 Apêndice E §E.7 / §E.8 / §E.12 — Sprint 3 PR 7. Covers the
/// three endpoints introduced for the dashboard's "connect a GitHub org →
/// pick a repo to import" flow:
/// <list type="bullet">
///   <item><description><c>GET /api/github/orgs</c> — lists user's orgs and UPSERTs into <c>github_orgs</c>;</description></item>
///   <item><description><c>GET /api/github/orgs/{login}/repos</c> — lists public + private repos under <c>{login}</c>;</description></item>
///   <item><description><c>POST /api/repos/import</c> — imports a repo with all four GitHub-import fields populated.</description></item>
/// </list>
///
/// Every test seeds an active token via <see cref="IGitHubUserTokenStore.SaveAsync"/>
/// to avoid running the real OAuth callback (which is exercised in
/// <see cref="AuthGithubConnectTests"/>); the fake
/// <see cref="Fakes.FakeGitHubOrgsClient"/> supplies the GitHub-side data.
/// No test hits <c>api.github.com</c>.
/// </summary>
public sealed class GithubOrgsEndpointsTests : WebInspectorTestBase
{
    public GithubOrgsEndpointsTests(PostgresFixture pg) : base(pg) { }

    // ── GET /api/github/orgs ───────────────────────────────────────────────

    [Fact]
    public async Task ListOrgs_Without_Cookie_Returns_401()
    {
        await ResetAsync();
        await using var factory = CreateFactory();
        var anon = factory.CreateClient();

        var resp = await anon.GetAsync("/api/github/orgs");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.Equal("unauthorized", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task ListOrgs_Without_Connected_Github_Returns_403_github_not_connected()
    {
        await ResetAsync();
        await using var factory = CreateFactory();
        var (client, _, _) = await SignUpAndGetAuthedClientAsync(factory, "alice@example.com", "Alice Co");

        // No SaveAsync on the token store — user signed up via email/password
        // and never ran the connect flow.
        var resp = await client.GetAsync("/api/github/orgs");
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.Equal("github_not_connected", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task ListOrgs_Returns_Orgs_From_Github_And_Upserts_GithubOrgs_Table()
    {
        await ResetAsync();
        await using var factory = CreateFactory();
        factory.FakeGitHubOrgs.Orgs = new List<GitHubOrgSummary>
        {
            new(1001, "acme-corp", "https://avatars.example/acme.png"),
            new(1002, "personal-projects", null),
            new(1003, "lintty-demo", "https://avatars.example/lintty.png"),
        };

        var (client, _, userId) = await SignUpAndGetAuthedClientAsync(factory, "bob@example.com", "Bob Inc");
        await SeedActiveTokenAsync(factory, userId);

        var resp = await client.GetAsync("/api/github/orgs");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.Equal(3, doc.RootElement.GetArrayLength());
        var logins = doc.RootElement.EnumerateArray()
            .Select(e => e.GetProperty("login").GetString())
            .ToArray();
        Assert.Contains("acme-corp", logins);
        Assert.Contains("personal-projects", logins);
        Assert.Contains("lintty-demo", logins);

        // The fake records the token it was handed; assert the same
        // plaintext that GetActiveTokenAsync should have returned.
        Assert.Equal("ghs_TEST_TOKEN_123", factory.FakeGitHubOrgs.LastTokenSeen);

        // 3 rows in github_orgs for this user.
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LinttyDbContext>();
        var rows = await db.GithubOrgs.AsNoTracking()
            .Where(o => o.UserId == userId).ToListAsync();
        Assert.Equal(3, rows.Count);
        Assert.Equal(new[] { 1001L, 1002L, 1003L }, rows.Select(o => o.GithubOrgId).OrderBy(x => x).ToArray());
        Assert.Contains(rows, o => o.GithubOrgLogin == "acme-corp" && o.GithubOrgAvatarUrl == "https://avatars.example/acme.png");
    }

    [Fact]
    public async Task ListOrgs_Idempotent_Updates_Login_And_AvatarUrl_On_Re_call()
    {
        await ResetAsync();
        await using var factory = CreateFactory();
        factory.FakeGitHubOrgs.Orgs = new List<GitHubOrgSummary>
        {
            new(2001, "acme-corp", "https://avatars.example/old.png"),
        };

        var (client, _, userId) = await SignUpAndGetAuthedClientAsync(factory, "carol@example.com", "Carol Co");
        await SeedActiveTokenAsync(factory, userId);

        var first = await client.GetAsync("/api/github/orgs");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        // GitHub now returns a renamed org + new avatar.
        factory.FakeGitHubOrgs.Orgs = new List<GitHubOrgSummary>
        {
            new(2001, "acme-corp-renamed", "https://avatars.example/new.png"),
        };
        var second = await client.GetAsync("/api/github/orgs");
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        // Still exactly one row (no insert), with refreshed cosmetic fields.
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LinttyDbContext>();
        var rows = await db.GithubOrgs.AsNoTracking()
            .Where(o => o.UserId == userId).ToListAsync();
        Assert.Single(rows);
        Assert.Equal("acme-corp-renamed", rows[0].GithubOrgLogin);
        Assert.Equal("https://avatars.example/new.png", rows[0].GithubOrgAvatarUrl);
    }

    // ── GET /api/github/orgs/{login}/repos ─────────────────────────────────

    [Fact]
    public async Task ListRepos_Without_Cookie_Returns_401()
    {
        await ResetAsync();
        await using var factory = CreateFactory();
        var anon = factory.CreateClient();

        var resp = await anon.GetAsync("/api/github/orgs/acme/repos");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task ListRepos_Without_Org_In_User_Orgs_Returns_404_org_not_in_user_orgs()
    {
        await ResetAsync();
        await using var factory = CreateFactory();
        var (client, _, userId) = await SignUpAndGetAuthedClientAsync(factory, "dave@example.com", "Dave Co");
        await SeedActiveTokenAsync(factory, userId);

        // User has token but never called GET /api/github/orgs to populate
        // the github_orgs cache. Asking for /api/github/orgs/microsoft/repos
        // must 404 — defends against arbitrary org pings.
        var resp = await client.GetAsync("/api/github/orgs/microsoft/repos");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.Equal("org_not_in_user_orgs", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task ListRepos_Returns_Public_And_Private_Repos()
    {
        await ResetAsync();
        await using var factory = CreateFactory();
        factory.FakeGitHubOrgs.Orgs = new List<GitHubOrgSummary>
        {
            new(3001, "acme", null),
        };
        factory.FakeGitHubOrgs.ReposByOrgLogin = new Dictionary<string, List<GitHubRepoSummary>>
        {
            ["acme"] = new()
            {
                new(11, "engine", "acme/engine", IsPrivate: true, "main", "https://github.com/acme/engine"),
                new(12, "open-source", "acme/open-source", IsPrivate: false, "main", "https://github.com/acme/open-source"),
                new(13, "secrets", "acme/secrets", IsPrivate: true, "develop", "https://github.com/acme/secrets"),
            },
        };

        var (client, _, userId) = await SignUpAndGetAuthedClientAsync(factory, "erin@example.com", "Erin Co");
        await SeedActiveTokenAsync(factory, userId);

        // Populate github_orgs first via the orgs endpoint (same flow the
        // dashboard would drive).
        (await client.GetAsync("/api/github/orgs")).EnsureSuccessStatusCode();

        var resp = await client.GetAsync("/api/github/orgs/acme/repos");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.Equal(3, doc.RootElement.GetArrayLength());

        var entries = doc.RootElement.EnumerateArray()
            .Select(e => new
            {
                FullName = e.GetProperty("fullName").GetString(),
                Private = e.GetProperty("private").GetBoolean(),
                Branch = e.GetProperty("defaultBranch").GetString(),
            })
            .ToArray();
        Assert.Contains(entries, x => x.FullName == "acme/engine" && x.Private == true && x.Branch == "main");
        Assert.Contains(entries, x => x.FullName == "acme/open-source" && x.Private == false);
        Assert.Contains(entries, x => x.FullName == "acme/secrets" && x.Private == true && x.Branch == "develop");
        Assert.Contains(entries, x => x.Private == true);   // sanity: private repos are visible
    }

    // ── POST /api/repos/import ────────────────────────────────────────────

    [Fact]
    public async Task Import_Without_Cookie_Returns_401()
    {
        await ResetAsync();
        await using var factory = CreateFactory();
        var anon = factory.CreateClient();

        var resp = await anon.PostAsJsonAsync("/api/repos/import", new
        {
            githubOrgLogin = "acme",
            repoFullName = "acme/engine",
        });
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Import_Without_Connected_Github_Returns_403_github_not_connected()
    {
        await ResetAsync();
        await using var factory = CreateFactory();
        var (client, _, _) = await SignUpAndGetAuthedClientAsync(factory, "frank@example.com", "Frank Co");

        var resp = await client.PostAsJsonAsync("/api/repos/import", new
        {
            githubOrgLogin = "acme",
            repoFullName = "acme/engine",
        });
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.Equal("github_not_connected", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Import_Repo_When_Org_Not_In_User_Orgs_Returns_404()
    {
        await ResetAsync();
        await using var factory = CreateFactory();
        var (client, _, userId) = await SignUpAndGetAuthedClientAsync(factory, "gerald@example.com", "G Co");
        await SeedActiveTokenAsync(factory, userId);

        // Token is active but github_orgs is empty for this user.
        var resp = await client.PostAsJsonAsync("/api/repos/import", new
        {
            githubOrgLogin = "acme",
            repoFullName = "acme/engine",
        });
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.Equal("org_not_in_user_orgs", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Import_Repo_Not_Found_On_Github_Returns_404_repo_not_found()
    {
        await ResetAsync();
        await using var factory = CreateFactory();
        factory.FakeGitHubOrgs.Orgs = new List<GitHubOrgSummary>
        {
            new(4001, "acme", null),
        };
        // MetadataByFullName has no entry for acme/missing → fake returns null.

        var (client, _, userId) = await SignUpAndGetAuthedClientAsync(factory, "harry@example.com", "H Co");
        await SeedActiveTokenAsync(factory, userId);
        (await client.GetAsync("/api/github/orgs")).EnsureSuccessStatusCode();

        var resp = await client.PostAsJsonAsync("/api/repos/import", new
        {
            githubOrgLogin = "acme",
            repoFullName = "acme/missing",
        });
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.Equal("repo_not_found", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Import_Happy_Path_Persists_Repo_With_Github_Fields_Populated()
    {
        await ResetAsync();
        await using var factory = CreateFactory();
        factory.FakeGitHubOrgs.Orgs = new List<GitHubOrgSummary>
        {
            new(5001, "acme", null),
        };
        factory.FakeGitHubOrgs.MetadataByFullName = new Dictionary<string, GitHubRepoDetails>
        {
            ["acme/engine"] = new(
                Id: 99001,
                Name: "engine",
                FullName: "acme/engine",
                IsPrivate: true,
                DefaultBranch: "develop",
                CloneUrl: "https://github.com/acme/engine.git"),
        };

        var (client, orgId, userId) = await SignUpAndGetAuthedClientAsync(factory, "import-happy@example.com", "Import Co");
        await SeedActiveTokenAsync(factory, userId);
        (await client.GetAsync("/api/github/orgs")).EnsureSuccessStatusCode();

        var resp = await client.PostAsJsonAsync("/api/repos/import", new
        {
            githubOrgLogin = "acme",
            repoFullName = "acme/engine",
        });
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var newRepoId = doc.RootElement.GetProperty("id").GetInt64();
        Assert.True(newRepoId > 0);
        Assert.Equal("https://github.com/acme/engine",
            doc.RootElement.GetProperty("githubUrl").GetString());
        Assert.True(doc.RootElement.GetProperty("isPrivate").GetBoolean());
        Assert.Equal(userId, doc.RootElement.GetProperty("addedBy").GetProperty("id").GetInt64());

        // Direct DB inspection confirms the four GitHub-import fields landed.
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LinttyDbContext>();
        var row = await db.Repos.AsNoTracking().SingleAsync(r => r.Id == newRepoId);
        Assert.Equal(orgId, row.OrgId);
        Assert.Equal(99001L, row.GithubRepoId);
        Assert.Equal("acme", row.GithubOrgLogin);
        Assert.Equal("develop", row.DefaultBranch);
        Assert.True(row.IsPrivate);
    }

    [Fact]
    public async Task Import_Idempotent_By_GithubRepoId_Returns_200_Same_Row()
    {
        await ResetAsync();
        await using var factory = CreateFactory();
        factory.FakeGitHubOrgs.Orgs = new List<GitHubOrgSummary>
        {
            new(6001, "acme", null),
        };
        factory.FakeGitHubOrgs.MetadataByFullName = new Dictionary<string, GitHubRepoDetails>
        {
            ["acme/engine"] = new(
                Id: 88001,
                Name: "engine",
                FullName: "acme/engine",
                IsPrivate: false,
                DefaultBranch: "main",
                CloneUrl: "https://github.com/acme/engine.git"),
        };

        var (client, _, userId) = await SignUpAndGetAuthedClientAsync(factory, "import-dedupe@example.com", "Dedupe Co");
        await SeedActiveTokenAsync(factory, userId);
        (await client.GetAsync("/api/github/orgs")).EnsureSuccessStatusCode();

        var first = await client.PostAsJsonAsync("/api/repos/import", new
        {
            githubOrgLogin = "acme",
            repoFullName = "acme/engine",
        });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        using var firstDoc = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        var firstId = firstDoc.RootElement.GetProperty("id").GetInt64();

        var second = await client.PostAsJsonAsync("/api/repos/import", new
        {
            githubOrgLogin = "acme",
            repoFullName = "acme/engine",
        });
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        using var secondDoc = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
        Assert.Equal(firstId, secondDoc.RootElement.GetProperty("id").GetInt64());
    }

    [Fact]
    public async Task Import_With_Insufficient_Github_Scopes_Returns_403()
    {
        await ResetAsync();
        await using var factory = CreateFactory();
        factory.FakeGitHubOrgs.Orgs = new List<GitHubOrgSummary>
        {
            new(7001, "acme", null),
        };
        // GitHub responds 403 to GetRepoMetadataAsync — token revoked at
        // GitHub between connect and import, or scope dropped.
        factory.FakeGitHubOrgs.ThrowOnGetMetadataForRepo = new Dictionary<string, HttpStatusCode>
        {
            ["acme/private"] = HttpStatusCode.Forbidden,
        };

        var (client, _, userId) = await SignUpAndGetAuthedClientAsync(factory, "import-403@example.com", "Three Oh Three");
        await SeedActiveTokenAsync(factory, userId);
        (await client.GetAsync("/api/github/orgs")).EnsureSuccessStatusCode();

        var resp = await client.PostAsJsonAsync("/api/repos/import", new
        {
            githubOrgLogin = "acme",
            repoFullName = "acme/private",
        });
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.Equal("insufficient_github_scopes", doc.RootElement.GetProperty("error").GetString());
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    /// <summary>
    /// Bypasses the OAuth callback by writing an "active" row directly via
    /// the token store. Keeps the suite focused on the orgs/repos surface;
    /// the callback's own contract is exercised by
    /// <see cref="AuthGithubConnectTests"/>.
    /// </summary>
    private static async Task SeedActiveTokenAsync(
        WebInspectorFactory factory, long userId, string token = "ghs_TEST_TOKEN_123")
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IGitHubUserTokenStore>();
        await store.SaveAsync(userId, token, new[] { "repo", "read:org" }, CancellationToken.None);
    }
}
