using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
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
/// ADR 0007 Sprint 3 / PR 4 — <c>/api/scans/*</c> + <c>GET /api/repos/{id}/scans</c>.
/// The suite covers the nine documented behaviours of the org-bound scan flow:
/// <list type="number">
///   <item><description>auth gate (cookie required everywhere);</description></item>
///   <item><description>tenant isolation on POST (cookie of org A → 404 on org B's repo per §3.5);</description></item>
///   <item><description>happy-path POST (201, public_id is UUID v4, canon snapshot, fk wiring);</description></item>
///   <item><description>polling GET returns full payload;</description></item>
///   <item><description>tenant isolation on GET (cross-org public_id → 404 per §3.5);</description></item>
///   <item><description>409 when downloading a still-queued scan (<c>scan_not_completed</c>);</description></item>
///   <item><description>404 when the scan is completed but the artifact was never written;</description></item>
///   <item><description>happy-path PDF stream (200, application/pdf, byte-faithful);</description></item>
///   <item><description>history list ordered by <c>queued_at DESC</c>.</description></item>
/// </list>
///
/// The worker is disabled by default in <see cref="WebInspectorFactory"/>
/// (<c>DisableWorker = true</c>) so jobs sit in <c>queued</c> without being
/// claimed mid-assertion. Tests that need a different status (completed)
/// drive the row directly via <see cref="LinttyDbContext"/>; tests that need
/// an artifact on disk drive <see cref="IArtifactStore"/> directly. The
/// engine subprocess is never invoked here — that's PR 5's
/// <c>DashboardScan_*</c> integration suite.
/// </summary>
public sealed class ScansEndpointsTests : WebInspectorTestBase
{
    public ScansEndpointsTests(PostgresFixture pg) : base(pg) { }

    // ── POST /api/scans ────────────────────────────────────────────────────

    [Fact]
    public async Task Post_Without_Cookie_Returns_401()
    {
        await ResetAsync();
        await using var factory = CreateFactory();
        var anon = factory.CreateClient();

        var resp = await anon.PostAsJsonAsync("/api/scans", new { repoId = 1 });
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.Equal("unauthorized", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Post_With_Foreign_Repo_Returns_404()
    {
        await ResetAsync();
        await using var factory = CreateFactory();

        // Org A registers a repo. Default fake is "exists, public, 256 KB".
        var (clientA, _, _) = await SignUpAndGetAuthedClientAsync(factory, "alice@example.com", "Org A");
        var add = await clientA.PostAsJsonAsync("/api/repos", new
        {
            githubUrl = "https://github.com/orga/secret",
        });
        add.EnsureSuccessStatusCode();
        using var addDoc = JsonDocument.Parse(await add.Content.ReadAsStringAsync());
        var aRepoId = addDoc.RootElement.GetProperty("id").GetInt64();

        // Org B logs in with a fresh cookie and tries to scan A's repo.
        var (clientB, _, _) = await SignUpAndGetAuthedClientAsync(factory, "bob@example.com", "Org B");
        var resp = await clientB.PostAsJsonAsync("/api/scans", new { repoId = aRepoId });

        // §3.5: 404, not 403 — never confirm the row exists in another tenant.
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.Equal("not_found", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Post_Returns_201_And_Persists_Queued_With_PublicId_And_CanonSnapshot()
    {
        await ResetAsync();
        await using var factory = CreateFactory();
        var (client, orgId, userId) = await SignUpAndGetAuthedClientAsync(
            factory, "carol@example.com", "Carol Co");

        var add = await client.PostAsJsonAsync("/api/repos", new
        {
            githubUrl = "https://github.com/lintty-demo/the-saint",
        });
        add.EnsureSuccessStatusCode();
        using var addDoc = JsonDocument.Parse(await add.Content.ReadAsStringAsync());
        var repoId = addDoc.RootElement.GetProperty("id").GetInt64();

        var resp = await client.PostAsJsonAsync("/api/scans", new
        {
            repoId,
            @ref = "main",
        });
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var publicIdStr = doc.RootElement.GetProperty("publicId").GetString();
        Assert.NotNull(publicIdStr);
        // Parse with strict format ("D" — 8-4-4-4-12 lowercased hex with dashes)
        // and assert variant + version (UUID v4 → version nibble == 4).
        Assert.True(Guid.TryParseExact(publicIdStr, "D", out var publicId),
            $"publicId is not in canonical UUID 'D' format: '{publicIdStr}'");
        var bytes = publicId.ToByteArray();
        // System.Guid byte order: bytes[7] holds the version-nibble byte.
        Assert.Equal(0x40, bytes[7] & 0xF0);
        // RFC 4122 variant: top two bits of bytes[8] are 10.
        Assert.Equal(0x80, bytes[8] & 0xC0);

        Assert.Equal("queued", doc.RootElement.GetProperty("status").GetString());
        Assert.Equal("1.0.0", doc.RootElement.GetProperty("canonVersion").GetString());
        Assert.Equal("main", doc.RootElement.GetProperty("ref").GetString());
        Assert.Equal(userId, doc.RootElement.GetProperty("triggeredByUserId").GetInt64());
        Assert.Equal(repoId, doc.RootElement.GetProperty("repo").GetProperty("id").GetInt64());

        // Sanity-check the row landed in Postgres with the same values — the
        // wire response could in principle drift from the persisted row, so
        // we verify both sides agree.
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LinttyDbContext>();
        var row = await db.Scans
            .AsNoTracking()
            .SingleAsync(s => s.PublicId == publicId);
        Assert.Equal("queued", row.Status);
        Assert.Equal("1.0.0", row.CanonVersion);
        Assert.Equal(orgId, row.OrgId);
        Assert.Equal(repoId, row.RepoId);
        Assert.Equal(userId, row.TriggeredByUserId);
        Assert.Equal("main", row.Ref);
        Assert.Null(row.HashContent);
        Assert.Null(row.CompletedAt);
        Assert.Null(row.StartedAt);
    }

    // ── GET /api/scans/{public_id} ─────────────────────────────────────────

    [Fact]
    public async Task Get_By_PublicId_Returns_Scan_State()
    {
        await ResetAsync();
        await using var factory = CreateFactory();
        var (client, orgId, userId) = await SignUpAndGetAuthedClientAsync(
            factory, "dave@example.com", "Dave Co");

        // Repo via the endpoint (manual add path is exercised in PR 3 tests),
        // scan via the service so this test isolates the GET contract from
        // the POST one.
        var add = await client.PostAsJsonAsync("/api/repos", new
        {
            githubUrl = "https://github.com/dave/repo",
        });
        add.EnsureSuccessStatusCode();
        using var addDoc = JsonDocument.Parse(await add.Content.ReadAsStringAsync());
        var repoId = addDoc.RootElement.GetProperty("id").GetInt64();

        Guid publicId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var scanService = scope.ServiceProvider.GetRequiredService<IScanService>();
            var trigger = await scanService.TriggerAsync(orgId, userId, repoId, "feature/x", CancellationToken.None);
            Assert.Equal(TriggerScanOutcome.Created, trigger.Outcome);
            publicId = trigger.Scan!.PublicId;
        }

        var resp = await client.GetAsync($"/api/scans/{publicId:D}");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.Equal(publicId.ToString("D"), doc.RootElement.GetProperty("publicId").GetString());
        Assert.Equal("queued", doc.RootElement.GetProperty("status").GetString());
        Assert.Equal("1.0.0", doc.RootElement.GetProperty("canonVersion").GetString());
        Assert.Equal("feature/x", doc.RootElement.GetProperty("ref").GetString());
        Assert.Equal(userId, doc.RootElement.GetProperty("triggeredByUserId").GetInt64());
        Assert.Equal(repoId, doc.RootElement.GetProperty("repo").GetProperty("id").GetInt64());
        Assert.Equal("https://github.com/dave/repo",
            doc.RootElement.GetProperty("repo").GetProperty("githubUrl").GetString());
        // Optional fields: still null while queued.
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("startedAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("completedAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("hashContent").ValueKind);
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("error").ValueKind);
    }

    [Fact]
    public async Task Get_Cross_Tenant_Returns_404()
    {
        await ResetAsync();
        await using var factory = CreateFactory();

        // Org A creates a repo and triggers a scan via the service.
        var (clientA, orgIdA, userIdA) = await SignUpAndGetAuthedClientAsync(
            factory, "ea@example.com", "Org Alpha");
        var add = await clientA.PostAsJsonAsync("/api/repos", new
        {
            githubUrl = "https://github.com/alpha/secret",
        });
        add.EnsureSuccessStatusCode();
        using var addDoc = JsonDocument.Parse(await add.Content.ReadAsStringAsync());
        var aRepoId = addDoc.RootElement.GetProperty("id").GetInt64();

        Guid publicIdA;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var scanService = scope.ServiceProvider.GetRequiredService<IScanService>();
            var trigger = await scanService.TriggerAsync(orgIdA, userIdA, aRepoId, null, CancellationToken.None);
            publicIdA = trigger.Scan!.PublicId;
        }

        // Org B tries to look up A's scan by its public id.
        var (clientB, _, _) = await SignUpAndGetAuthedClientAsync(factory, "eb@example.com", "Org Beta");
        var resp = await clientB.GetAsync($"/api/scans/{publicIdA:D}");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);

        // Org A still sees its own scan — sanity that we didn't break the row.
        var ownerView = await clientA.GetAsync($"/api/scans/{publicIdA:D}");
        Assert.Equal(HttpStatusCode.OK, ownerView.StatusCode);
    }

    // ── GET /api/scans/{public_id}/laudo.pdf ───────────────────────────────

    [Fact]
    public async Task Download_Pdf_Without_Completed_Status_Returns_409()
    {
        await ResetAsync();
        await using var factory = CreateFactory();
        var (client, orgId, userId) = await SignUpAndGetAuthedClientAsync(
            factory, "fa@example.com", "Org F");

        var add = await client.PostAsJsonAsync("/api/repos", new
        {
            githubUrl = "https://github.com/f/repo",
        });
        add.EnsureSuccessStatusCode();
        using var addDoc = JsonDocument.Parse(await add.Content.ReadAsStringAsync());
        var repoId = addDoc.RootElement.GetProperty("id").GetInt64();

        Guid publicId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var scanService = scope.ServiceProvider.GetRequiredService<IScanService>();
            var trigger = await scanService.TriggerAsync(orgId, userId, repoId, null, CancellationToken.None);
            publicId = trigger.Scan!.PublicId;
        }

        var resp = await client.GetAsync($"/api/scans/{publicId:D}/laudo.pdf");
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.Equal("scan_not_completed", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Download_Pdf_When_Artifact_Missing_Returns_404()
    {
        await ResetAsync();
        await using var factory = CreateFactory();
        var (client, orgId, userId) = await SignUpAndGetAuthedClientAsync(
            factory, "ga@example.com", "Org G");

        var add = await client.PostAsJsonAsync("/api/repos", new
        {
            githubUrl = "https://github.com/g/repo",
        });
        add.EnsureSuccessStatusCode();
        using var addDoc = JsonDocument.Parse(await add.Content.ReadAsStringAsync());
        var repoId = addDoc.RootElement.GetProperty("id").GetInt64();

        Guid publicId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var scanService = scope.ServiceProvider.GetRequiredService<IScanService>();
            var trigger = await scanService.TriggerAsync(orgId, userId, repoId, null, CancellationToken.None);
            publicId = trigger.Scan!.PublicId;

            // Force the row to completed with no artifact on disk — the
            // defensive 404 branch for "store empty for a completed scan".
            var db = scope.ServiceProvider.GetRequiredService<LinttyDbContext>();
            var row = await db.Scans.SingleAsync(s => s.PublicId == publicId);
            row.Status = ScanStatus.Completed;
            row.StartedAt = DateTime.UtcNow.AddSeconds(-2);
            row.CompletedAt = DateTime.UtcNow;
            row.HashContent = "sha256:0000000000000000000000000000000000000000000000000000000000000000";
            await db.SaveChangesAsync();
        }

        var resp = await client.GetAsync($"/api/scans/{publicId:D}/laudo.pdf");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        // The endpoint surfaces both "row missing" and "artifact missing" as
        // the same 404 envelope (ScansEndpoints.NotFound), so the message —
        // not the code — distinguishes them.
        Assert.Equal("not_found", doc.RootElement.GetProperty("error").GetString());
        Assert.Equal("Artifact missing.", doc.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Download_Pdf_Happy_Path_Streams_File()
    {
        await ResetAsync();
        await using var factory = CreateFactory();
        var (client, orgId, userId) = await SignUpAndGetAuthedClientAsync(
            factory, "ha@example.com", "Org H");

        var add = await client.PostAsJsonAsync("/api/repos", new
        {
            githubUrl = "https://github.com/h/repo",
        });
        add.EnsureSuccessStatusCode();
        using var addDoc = JsonDocument.Parse(await add.Content.ReadAsStringAsync());
        var repoId = addDoc.RootElement.GetProperty("id").GetInt64();

        // Mock PDF payload: 4 KB of deterministic bytes plus a %PDF prefix so
        // anyone debugging the test sees a recognisable header. Hash both
        // ends to assert byte-faithfulness through the streaming response.
        var prefix = System.Text.Encoding.ASCII.GetBytes("%PDF-1.4\n");
        var rest = new byte[4096];
        new Random(20260507).NextBytes(rest);
        var pdfBytes = prefix.Concat(rest).ToArray();
        var expectedHash = Sha256(pdfBytes);

        Guid publicId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var scanService = scope.ServiceProvider.GetRequiredService<IScanService>();
            var trigger = await scanService.TriggerAsync(orgId, userId, repoId, null, CancellationToken.None);
            publicId = trigger.Scan!.PublicId;

            // Reserve + write the PDF on the same store the endpoint reads
            // back through. Resolving from the same DI scope ensures the
            // JobStorage:Root override from the test factory is honoured.
            var artifacts = scope.ServiceProvider.GetRequiredService<IArtifactStore>();
            var path = await artifacts.ReserveAsync(publicId, ArtifactKind.LaudoPdf, CancellationToken.None);
            await File.WriteAllBytesAsync(path, pdfBytes);

            var db = scope.ServiceProvider.GetRequiredService<LinttyDbContext>();
            var row = await db.Scans.SingleAsync(s => s.PublicId == publicId);
            row.Status = ScanStatus.Completed;
            row.StartedAt = DateTime.UtcNow.AddSeconds(-2);
            row.CompletedAt = DateTime.UtcNow;
            row.HashContent = "sha256:" + expectedHash; // not asserted but realistic
            await db.SaveChangesAsync();
        }

        var resp = await client.GetAsync($"/api/scans/{publicId:D}/laudo.pdf");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("application/pdf", resp.Content.Headers.ContentType?.MediaType);

        var actual = await resp.Content.ReadAsByteArrayAsync();
        Assert.Equal(pdfBytes.Length, actual.Length);
        Assert.Equal(expectedHash, Sha256(actual));
    }

    // ── GET /api/repos/{id}/scans ──────────────────────────────────────────

    [Fact]
    public async Task History_Lists_Scans_Of_Repo_Ordered_By_Queued_Desc()
    {
        await ResetAsync();
        await using var factory = CreateFactory();
        var (client, orgId, userId) = await SignUpAndGetAuthedClientAsync(
            factory, "ia@example.com", "Org I");

        var add = await client.PostAsJsonAsync("/api/repos", new
        {
            githubUrl = "https://github.com/i/repo",
        });
        add.EnsureSuccessStatusCode();
        using var addDoc = JsonDocument.Parse(await add.Content.ReadAsStringAsync());
        var repoId = addDoc.RootElement.GetProperty("id").GetInt64();

        // Seed three scans with hand-set queued_at values — the EF default
        // (DateTime.UtcNow on Add) would race within the same millisecond on
        // a fast box and leave the order at the mercy of the id tie-breaker
        // alone. Explicit times remove the ambiguity.
        var t0 = DateTime.UtcNow.AddMinutes(-10);
        Guid pidOldest, pidMiddle, pidNewest;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LinttyDbContext>();
            var oldest = NewQueued(orgId, repoId, userId, t0, "v1");
            var middle = NewQueued(orgId, repoId, userId, t0.AddMinutes(2), "v2");
            var newest = NewQueued(orgId, repoId, userId, t0.AddMinutes(5), "v3");
            db.Scans.AddRange(oldest, middle, newest);
            await db.SaveChangesAsync();
            pidOldest = oldest.PublicId;
            pidMiddle = middle.PublicId;
            pidNewest = newest.PublicId;
        }

        var resp = await client.GetAsync($"/api/repos/{repoId}/scans");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.Equal(3, doc.RootElement.GetArrayLength());

        var ids = doc.RootElement.EnumerateArray()
            .Select(e => Guid.ParseExact(e.GetProperty("publicId").GetString()!, "D"))
            .ToArray();
        Assert.Equal(new[] { pidNewest, pidMiddle, pidOldest }, ids);

        // Same payload shape as GET-by-id — repo block denormalised in.
        Assert.Equal(repoId, doc.RootElement[0].GetProperty("repo").GetProperty("id").GetInt64());
        Assert.Equal("https://github.com/i/repo",
            doc.RootElement[0].GetProperty("repo").GetProperty("githubUrl").GetString());
    }

    // ── helpers ────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a fully-formed <see cref="Scan"/> entity ready for direct DB
    /// insert. Pre-fills <see cref="Scan.PublicId"/> on the client side so
    /// the test can correlate inserted rows with response payloads without
    /// a re-read; the migration's <c>gen_random_uuid()</c> default would
    /// otherwise fight the explicit value silently.
    /// </summary>
    private static Scan NewQueued(long orgId, long repoId, long userId, DateTime queuedAt, string? gitRef)
        => new()
        {
            OrgId = orgId,
            RepoId = repoId,
            TriggeredByUserId = userId,
            Ref = gitRef,
            CanonVersion = "1.0.0",
            Status = ScanStatus.Queued,
            QueuedAt = queuedAt,
            PublicId = Guid.NewGuid(),
        };

    private static string Sha256(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
