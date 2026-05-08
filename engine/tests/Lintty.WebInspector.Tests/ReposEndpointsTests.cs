using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Lintty.WebInspector.Tests;

/// <summary>
/// ADR 0007 Sprint 3 / PR 3 — <c>/api/repos/*</c>. Manual add only; the
/// Org-import path lives behind a different route and lands in PR 7. The
/// suite covers the four documented behaviours:
/// <list type="number">
///   <item><description>auth gate (cookie required everywhere);</description></item>
///   <item><description>URL validation + canonicalisation + GitHub pre-flight;</description></item>
///   <item><description>idempotent re-add of the same active URL;</description></item>
///   <item><description>tenant isolation (cookie of org A → 404 on org B per §3.5).</description></item>
/// </list>
///
/// The fake <c>FakeGitHubMetadataClient</c> defaults to "exists, public, 256 KB"
/// so the happy path needs zero setup; the private/404 cases mutate the fake
/// before issuing the request.
/// </summary>
public sealed class ReposEndpointsTests : WebInspectorTestBase
{
    public ReposEndpointsTests(PostgresFixture pg) : base(pg) { }

    // ── POST /api/repos ────────────────────────────────────────────────────

    [Fact]
    public async Task Post_Without_Cookie_Returns_401()
    {
        await ResetAsync();
        await using var factory = CreateFactory();
        var anon = factory.CreateClient();

        var resp = await anon.PostAsJsonAsync("/api/repos", new
        {
            githubUrl = "https://github.com/owner/repo",
        });
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.Equal("unauthorized", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Post_With_Invalid_Url_Returns_400_invalid_github_url()
    {
        await ResetAsync();
        await using var factory = CreateFactory();
        var (client, _, _) = await SignUpAndGetAuthedClientAsync(factory, "alice@example.com", "Alice Co");

        var resp = await client.PostAsJsonAsync("/api/repos", new
        {
            githubUrl = "https://gitlab.com/owner/repo",
        });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.Equal("invalid_github_url", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Post_With_Public_Repo_Returns_201_And_Persists()
    {
        await ResetAsync();
        await using var factory = CreateFactory();
        // FakeGitHub defaults: Exists=true, Forbidden=false, 256 KB, branch=main.
        var (client, orgId, userId) = await SignUpAndGetAuthedClientAsync(
            factory, "bob@example.com", "Bob Inc", displayName: "Bob");

        var resp = await client.PostAsJsonAsync("/api/repos", new
        {
            githubUrl = "https://github.com/lintty-demo/the-saint",
        });
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.True(doc.RootElement.GetProperty("id").GetInt64() > 0);
        Assert.Equal("https://github.com/lintty-demo/the-saint",
            doc.RootElement.GetProperty("githubUrl").GetString());
        Assert.False(doc.RootElement.GetProperty("isPrivate").GetBoolean());
        Assert.Equal(userId, doc.RootElement.GetProperty("addedBy").GetProperty("id").GetInt64());
        Assert.Equal("Bob", doc.RootElement.GetProperty("addedBy").GetProperty("displayName").GetString());

        // GET /api/repos returns the row.
        var list = await client.GetAsync("/api/repos");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        using var listDoc = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        Assert.Equal(1, listDoc.RootElement.GetArrayLength());
        Assert.Equal(orgId, orgId); // sanity — silences the "unused" warning if assertion above ever drops.
    }

    [Fact]
    public async Task Post_Same_Url_Twice_Returns_200_Idempotent_Same_Id()
    {
        await ResetAsync();
        await using var factory = CreateFactory();
        var (client, _, _) = await SignUpAndGetAuthedClientAsync(factory, "carol@example.com", "Carol Co");

        var first = await client.PostAsJsonAsync("/api/repos", new
        {
            githubUrl = "https://github.com/lintty-demo/the-saint",
        });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        using var firstDoc = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        var firstId = firstDoc.RootElement.GetProperty("id").GetInt64();

        var second = await client.PostAsJsonAsync("/api/repos", new
        {
            githubUrl = "https://github.com/lintty-demo/the-saint",
        });
        // Idempotent: 200 OK with the same row.
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        using var secondDoc = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
        Assert.Equal(firstId, secondDoc.RootElement.GetProperty("id").GetInt64());

        // List has exactly one row.
        var list = await client.GetAsync("/api/repos");
        using var listDoc = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        Assert.Equal(1, listDoc.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task Post_With_Private_Repo_Returns_400_repo_is_private()
    {
        await ResetAsync();
        await using var factory = CreateFactory();
        // GitHub responded 401/403 to our anonymous metadata call → repo is
        // private (or org policy blocks unauth — same UX impact).
        factory.FakeGitHub.Forbidden = true;
        var (client, _, _) = await SignUpAndGetAuthedClientAsync(factory, "dave@example.com", "Dave Co");

        var resp = await client.PostAsJsonAsync("/api/repos", new
        {
            githubUrl = "https://github.com/private-org/private-repo",
        });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.Equal("repo_is_private", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Post_With_Nonexistent_Repo_Returns_400_repo_not_found_or_private()
    {
        await ResetAsync();
        await using var factory = CreateFactory();
        factory.FakeGitHub.Exists = false;
        var (client, _, _) = await SignUpAndGetAuthedClientAsync(factory, "erin@example.com", "Erin Co");

        var resp = await client.PostAsJsonAsync("/api/repos", new
        {
            githubUrl = "https://github.com/nobody/nothing",
        });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.Equal("repo_not_found_or_private", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Post_Url_Casing_And_Trailing_Slash_Normalized()
    {
        await ResetAsync();
        await using var factory = CreateFactory();
        var (client, _, _) = await SignUpAndGetAuthedClientAsync(factory, "frank@example.com", "Frank Co");

        // Mixed-case host, mixed-case owner/repo, .git suffix, trailing slash.
        // UrlValidator preserves owner/repo casing (GitHub URLs are case-sensitive
        // for path segments per their docs) but lowercases the host and strips
        // .git/trailing-slash. The persisted row reflects that canonical form.
        var resp = await client.PostAsJsonAsync("/api/repos", new
        {
            githubUrl = "Https://Github.com/Owner/Repo.git/",
        });
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.Equal("https://github.com/Owner/Repo",
            doc.RootElement.GetProperty("githubUrl").GetString());

        // Re-post with a different casing variant of the same logical URL —
        // partial unique catches it via canonicalisation, idempotent 200.
        var second = await client.PostAsJsonAsync("/api/repos", new
        {
            githubUrl = "https://GITHUB.COM/Owner/Repo",
        });
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
    }

    // ── GET /api/repos ─────────────────────────────────────────────────────

    [Fact]
    public async Task Get_List_Returns_Only_Active_Repos_Of_Tenant_Org()
    {
        await ResetAsync();
        await using var factory = CreateFactory();

        // Org A: two repos.
        var (clientA, _, _) = await SignUpAndGetAuthedClientAsync(factory, "ga@example.com", "Org A");
        var a1 = await clientA.PostAsJsonAsync("/api/repos", new { githubUrl = "https://github.com/a/one" });
        a1.EnsureSuccessStatusCode();
        var a2 = await clientA.PostAsJsonAsync("/api/repos", new { githubUrl = "https://github.com/a/two" });
        a2.EnsureSuccessStatusCode();

        // Org B: one repo.
        var (clientB, _, _) = await SignUpAndGetAuthedClientAsync(factory, "gb@example.com", "Org B");
        var b1 = await clientB.PostAsJsonAsync("/api/repos", new { githubUrl = "https://github.com/b/one" });
        b1.EnsureSuccessStatusCode();

        // Org A list sees only A's repos.
        var listA = await clientA.GetAsync("/api/repos");
        Assert.Equal(HttpStatusCode.OK, listA.StatusCode);
        using var docA = JsonDocument.Parse(await listA.Content.ReadAsStringAsync());
        Assert.Equal(2, docA.RootElement.GetArrayLength());
        foreach (var elem in docA.RootElement.EnumerateArray())
        {
            var url = elem.GetProperty("githubUrl").GetString();
            Assert.StartsWith("https://github.com/a/", url);
        }

        // Org B list sees only B's repo.
        var listB = await clientB.GetAsync("/api/repos");
        using var docB = JsonDocument.Parse(await listB.Content.ReadAsStringAsync());
        Assert.Equal(1, docB.RootElement.GetArrayLength());
        Assert.Equal("https://github.com/b/one",
            docB.RootElement[0].GetProperty("githubUrl").GetString());
    }

    // ── GET /api/repos/{id} ────────────────────────────────────────────────

    [Fact]
    public async Task Get_Detail_Cross_Tenant_Returns_404()
    {
        await ResetAsync();
        await using var factory = CreateFactory();

        var (clientA, _, _) = await SignUpAndGetAuthedClientAsync(factory, "ha@example.com", "Org A");
        var a1 = await clientA.PostAsJsonAsync("/api/repos", new { githubUrl = "https://github.com/a/secret" });
        using var aDoc = JsonDocument.Parse(await a1.Content.ReadAsStringAsync());
        var aRepoId = aDoc.RootElement.GetProperty("id").GetInt64();

        var (clientB, _, _) = await SignUpAndGetAuthedClientAsync(factory, "hb@example.com", "Org B");
        var resp = await clientB.GetAsync($"/api/repos/{aRepoId}");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);

        // Org A still sees its own repo.
        var ownerView = await clientA.GetAsync($"/api/repos/{aRepoId}");
        Assert.Equal(HttpStatusCode.OK, ownerView.StatusCode);
    }

    // ── DELETE /api/repos/{id} ─────────────────────────────────────────────

    [Fact]
    public async Task Delete_Soft_Deletes_And_Subsequent_Get_Returns_404()
    {
        await ResetAsync();
        await using var factory = CreateFactory();
        var (client, _, _) = await SignUpAndGetAuthedClientAsync(factory, "ia@example.com", "Org I");
        var add = await client.PostAsJsonAsync("/api/repos", new { githubUrl = "https://github.com/i/repo" });
        using var addDoc = JsonDocument.Parse(await add.Content.ReadAsStringAsync());
        var repoId = addDoc.RootElement.GetProperty("id").GetInt64();

        var del = await client.DeleteAsync($"/api/repos/{repoId}");
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);

        // GET → 404
        var getAfter = await client.GetAsync($"/api/repos/{repoId}");
        Assert.Equal(HttpStatusCode.NotFound, getAfter.StatusCode);

        // List → empty
        var list = await client.GetAsync("/api/repos");
        using var listDoc = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        Assert.Equal(0, listDoc.RootElement.GetArrayLength());

        // Re-DELETE → 404 (already deleted)
        var delTwice = await client.DeleteAsync($"/api/repos/{repoId}");
        Assert.Equal(HttpStatusCode.NotFound, delTwice.StatusCode);
    }

    [Fact]
    public async Task Delete_Cross_Tenant_Returns_404()
    {
        await ResetAsync();
        await using var factory = CreateFactory();

        var (clientA, _, _) = await SignUpAndGetAuthedClientAsync(factory, "ja@example.com", "Org J");
        var add = await clientA.PostAsJsonAsync("/api/repos", new { githubUrl = "https://github.com/j/keep" });
        using var addDoc = JsonDocument.Parse(await add.Content.ReadAsStringAsync());
        var repoId = addDoc.RootElement.GetProperty("id").GetInt64();

        var (clientB, _, _) = await SignUpAndGetAuthedClientAsync(factory, "jb@example.com", "Org K");
        var attempt = await clientB.DeleteAsync($"/api/repos/{repoId}");
        Assert.Equal(HttpStatusCode.NotFound, attempt.StatusCode);

        // Org A's repo is intact and still queryable.
        var ownerView = await clientA.GetAsync($"/api/repos/{repoId}");
        Assert.Equal(HttpStatusCode.OK, ownerView.StatusCode);
    }
}
