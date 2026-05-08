using System;
using System.IO;
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
using Lintty.WebInspector.Artifacts;
using Lintty.WebInspector.Persistence;
using Lintty.WebInspector.Persistence.Entities;
using Lintty.WebInspector.Scans;

namespace Lintty.WebInspector.Tests;

/// <summary>
/// ADR 0007 §3.5 cross-tenant isolation gate — Sprint 3 / PR 5.
///
/// <para>
/// Each test wires <b>two distinct logged-in users</b>, one per org, and
/// proves that User-A (cookie scoped to Org-A) cannot see, mutate, or
/// exfiltrate any resource owned by Org-B. The expected response is
/// always <c>HTTP 404</c> with body <c>{ "error": "not_found", ... }</c> —
/// never <c>403</c>, never an envelope that confirms the resource exists.
/// 403 would expose enumeration: an attacker harvesting <c>public_id</c>
/// values from leaked logs could distinguish "this id exists in some other
/// org" from "this id does not exist anywhere", which is itself a leak.
/// </para>
///
/// <para>
/// <b>Scope of this file.</b> This is the focused regression suite for the
/// invariant. <see cref="ReposEndpointsTests"/> and
/// <see cref="ScansEndpointsTests"/> already cover their own happy/sad
/// paths including some cross-tenant scenarios; the value of this file is
/// having the §3.5 invariant <i>as a single grep-able target</i> with the
/// minimum five scenarios that, together, prove the boundary on every
/// surface mounted in PR 3 + PR 4.
/// </para>
///
/// <para>
/// The five scenarios (one per surface that returns a single resource by
/// id, plus the highest-impact one — the PDF download — for which an id
/// leak would let an outsider exfiltrate audit content):
/// <list type="number">
///   <item><description>GET /api/repos/{id} cross-tenant → 404.</description></item>
///   <item><description>DELETE /api/repos/{id} cross-tenant → 404, owner read still works.</description></item>
///   <item><description>GET /api/scans/{public_id} cross-tenant → 404.</description></item>
///   <item><description>GET /api/scans/{public_id}/laudo.pdf cross-tenant → 404 even when status=completed.</description></item>
///   <item><description>GET /api/repos/{id}/scans cross-tenant → 404 (NOT 200 + empty array).</description></item>
/// </list>
/// </para>
/// </summary>
public sealed class TenantIsolationTests : WebInspectorTestBase
{
    public TenantIsolationTests(PostgresFixture pg) : base(pg) { }

    [Fact]
    public async Task Get_Repo_Detail_Cross_Tenant_Returns_404_Not_403()
    {
        await ResetAsync();
        await using var factory = CreateFactory();
        var fixture = await SetupTwoOrgsWithBOwningEverythingAsync(factory);

        var resp = await fixture.ClientA.GetAsync($"/api/repos/{fixture.RepoIdB}");

        // §3.5: 404 — never 403. Body must not confirm existence.
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        AssertNoLeakage(body, fixture.RepoIdB);
    }

    [Fact]
    public async Task Delete_Repo_Cross_Tenant_Returns_404_And_Repo_Stays_Active()
    {
        await ResetAsync();
        await using var factory = CreateFactory();
        var fixture = await SetupTwoOrgsWithBOwningEverythingAsync(factory);

        // Org A attempts to delete Org B's repo.
        var attempt = await fixture.ClientA.DeleteAsync($"/api/repos/{fixture.RepoIdB}");
        Assert.Equal(HttpStatusCode.NotFound, attempt.StatusCode);
        var body = await attempt.Content.ReadAsStringAsync();
        AssertNoLeakage(body, fixture.RepoIdB);

        // Side-effect-zero sanity check: Org B (the real owner) still sees
        // the repo intact. Proves the cross-tenant DELETE was a true no-op,
        // not just a 404 envelope after a successful soft-delete.
        var ownerView = await fixture.ClientB.GetAsync($"/api/repos/{fixture.RepoIdB}");
        Assert.Equal(HttpStatusCode.OK, ownerView.StatusCode);
        using var ownerDoc = JsonDocument.Parse(await ownerView.Content.ReadAsStringAsync());
        Assert.Equal(fixture.RepoIdB, ownerDoc.RootElement.GetProperty("id").GetInt64());
    }

    [Fact]
    public async Task Get_Scan_Detail_Cross_Tenant_Returns_404()
    {
        await ResetAsync();
        await using var factory = CreateFactory();
        var fixture = await SetupTwoOrgsWithBOwningEverythingAsync(factory);

        var resp = await fixture.ClientA.GetAsync($"/api/scans/{fixture.ScanPublicIdB:D}");

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        AssertNoLeakage(body, fixture.ScanPublicIdB.ToString("D"));

        // Sanity: B's scan is still visible to B.
        var ownerView = await fixture.ClientB.GetAsync($"/api/scans/{fixture.ScanPublicIdB:D}");
        Assert.Equal(HttpStatusCode.OK, ownerView.StatusCode);
    }

    [Fact]
    public async Task Download_Scan_Pdf_Cross_Tenant_Returns_404_Even_When_Completed_With_Artifact()
    {
        await ResetAsync();
        await using var factory = CreateFactory();
        var fixture = await SetupTwoOrgsWithBOwningEverythingAsync(factory);

        // Force B's scan to completed with a real artifact on disk. This is
        // the highest-impact branch: an attacker who learned the public_id
        // (from a log leak, a shared screenshot, anything) tries to pull
        // the PDF directly. 404 must hold even when the bytes exist on the
        // store and the row is in the "downloadable" state.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var artifacts = scope.ServiceProvider.GetRequiredService<IArtifactStore>();
            var path = await artifacts.ReserveAsync(
                fixture.ScanPublicIdB, ArtifactKind.LaudoPdf, CancellationToken.None);
            // 9-byte sentinel — recognisable header so a debugger sees the
            // shape, but the test asserts on status, not contents.
            await File.WriteAllBytesAsync(path, new byte[]
            {
                0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x34, 0x0A, // "%PDF-1.4\n"
            });

            var db = scope.ServiceProvider.GetRequiredService<LinttyDbContext>();
            var row = await db.Scans.SingleAsync(s => s.PublicId == fixture.ScanPublicIdB);
            row.Status = ScanStatus.Completed;
            row.StartedAt = DateTime.UtcNow.AddSeconds(-2);
            row.CompletedAt = DateTime.UtcNow;
            row.HashContent = "sha256:0000000000000000000000000000000000000000000000000000000000000000";
            await db.SaveChangesAsync();
        }

        // Org A — wrong tenant — must get 404, NOT a stream of B's PDF.
        var crossTenant = await fixture.ClientA.GetAsync(
            $"/api/scans/{fixture.ScanPublicIdB:D}/laudo.pdf");
        Assert.Equal(HttpStatusCode.NotFound, crossTenant.StatusCode);
        // Content-Type must be JSON (the error envelope), NOT application/pdf.
        // Streaming the PDF here would be the catastrophic leak.
        Assert.Equal("application/json", crossTenant.Content.Headers.ContentType?.MediaType);
        var body = await crossTenant.Content.ReadAsStringAsync();
        AssertNoLeakage(body, fixture.ScanPublicIdB.ToString("D"));

        // Org B (owner) DOES get the PDF — proves the artifact was reachable
        // and the 404 came from tenancy, not from a write failure earlier.
        var ownerView = await fixture.ClientB.GetAsync(
            $"/api/scans/{fixture.ScanPublicIdB:D}/laudo.pdf");
        Assert.Equal(HttpStatusCode.OK, ownerView.StatusCode);
        Assert.Equal("application/pdf", ownerView.Content.Headers.ContentType?.MediaType);
        var bytes = await ownerView.Content.ReadAsByteArrayAsync();
        Assert.Equal(9, bytes.Length);
    }

    [Fact]
    public async Task List_Scans_Of_Repo_Cross_Tenant_Returns_404_Not_Empty_Array()
    {
        await ResetAsync();
        await using var factory = CreateFactory();
        var fixture = await SetupTwoOrgsWithBOwningEverythingAsync(factory);

        // The tempting-but-wrong response would be 200 + empty array
        // ("here's all the scans of that repo that YOU can see"). That
        // distinguishes "repo doesn't exist" from "repo exists but is
        // empty for me", which is exactly the enumeration vector §3.5
        // closes. The endpoint must return 404 here — same shape as if
        // the repo never existed.
        var resp = await fixture.ClientA.GetAsync($"/api/repos/{fixture.RepoIdB}/scans");

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        AssertNoLeakage(body, fixture.RepoIdB);

        // Sanity: B sees their own scan in the list.
        var ownerView = await fixture.ClientB.GetAsync($"/api/repos/{fixture.RepoIdB}/scans");
        Assert.Equal(HttpStatusCode.OK, ownerView.StatusCode);
        using var ownerDoc = JsonDocument.Parse(await ownerView.Content.ReadAsStringAsync());
        Assert.Equal(1, ownerDoc.RootElement.GetArrayLength());
        Assert.Equal(fixture.ScanPublicIdB.ToString("D"),
            ownerDoc.RootElement[0].GetProperty("publicId").GetString());
    }

    // ── shared setup ─────────────────────────────────────────────────────────

    /// <summary>
    /// Provisions <b>two distinct logged-in clients</b> bound to two
    /// different orgs. Org B owns one repo with one queued scan; Org A
    /// owns nothing. Returns the handles every cross-tenant test needs:
    /// the cookie-bearing clients and the ids/public_ids of B's resources.
    /// Centralised here so the five tests share one provisioning shape and
    /// a regression in one of them is easy to triage.
    /// </summary>
    private static async Task<TenantFixture> SetupTwoOrgsWithBOwningEverythingAsync(
        WebInspectorFactory factory)
    {
        var (clientA, _, _) = await SignUpAndGetAuthedClientAsync(
            factory, "alice@isolation.test", "Org Alpha");
        var (clientB, orgIdB, userIdB) = await SignUpAndGetAuthedClientAsync(
            factory, "bob@isolation.test", "Org Beta");

        // B adds a repo via the public endpoint (uses FakeGitHubMetadataClient
        // defaults: exists, public, 256 KB).
        var addB = await clientB.PostAsJsonAsync("/api/repos", new
        {
            githubUrl = "https://github.com/orgbeta/secret-thing",
        });
        addB.EnsureSuccessStatusCode();
        using var addDoc = JsonDocument.Parse(await addB.Content.ReadAsStringAsync());
        var repoIdB = addDoc.RootElement.GetProperty("id").GetInt64();

        // B triggers a scan via the service. We use the service (not the
        // endpoint) so the suite isolates the §3.5 surface from the POST
        // /api/scans contract — that one has its own dedicated test in
        // ScansEndpointsTests.Post_With_Foreign_Repo_Returns_404.
        Guid scanPublicIdB;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var scanService = scope.ServiceProvider.GetRequiredService<IScanService>();
            var trigger = await scanService.TriggerAsync(
                orgIdB, userIdB, repoIdB, gitRef: null, targets: null, CancellationToken.None);
            Assert.Equal(TriggerScanOutcome.Created, trigger.Outcome);
            scanPublicIdB = trigger.Scan!.PublicId;
        }

        return new TenantFixture(clientA, clientB, repoIdB, scanPublicIdB);
    }

    /// <summary>
    /// Asserts the error body looks like the canonical
    /// <c>{ "error": "not_found", "message": "..." }</c> envelope and that
    /// it does not leak the <paramref name="forbiddenSubstring"/> identifier
    /// — the message could in principle echo the id ("Repo 42 not found"),
    /// which would itself be a leak ("42 exists in some org"). We tighten
    /// the contract here so a future endpoint refactor can't accidentally
    /// regress.
    /// </summary>
    private static void AssertNoLeakage(string responseBody, object forbiddenSubstring)
    {
        using var doc = JsonDocument.Parse(responseBody);
        var error = doc.RootElement.GetProperty("error").GetString();
        Assert.Equal("not_found", error);

        // The message field should be a generic "not found" string —
        // never "forbidden", "unauthorized", "wrong tenant", or anything
        // that hints at "exists elsewhere".
        if (doc.RootElement.TryGetProperty("message", out var msg)
            && msg.ValueKind == JsonValueKind.String)
        {
            var lower = msg.GetString()!.ToLowerInvariant();
            Assert.DoesNotContain("forbid", lower);
            Assert.DoesNotContain("permission", lower);
            Assert.DoesNotContain("tenant", lower);
            Assert.DoesNotContain("other org", lower);
            Assert.DoesNotContain("denied", lower);
        }

        // The id itself must not appear anywhere in the body — neither in
        // the message ("Repo 42 not found.") nor in any ancillary field.
        var needle = forbiddenSubstring.ToString();
        if (!string.IsNullOrEmpty(needle))
        {
            Assert.DoesNotContain(needle, responseBody, StringComparison.OrdinalIgnoreCase);
        }
    }

    private sealed record TenantFixture(
        HttpClient ClientA,
        HttpClient ClientB,
        long RepoIdB,
        Guid ScanPublicIdB);
}
