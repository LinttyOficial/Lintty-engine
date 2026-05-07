using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Xunit;
using Lintty.WebInspector.Artifacts;
using Lintty.WebInspector.Configuration;

namespace Lintty.WebInspector.Tests;

/// <summary>
/// Unit tests for <see cref="LocalArtifactStore"/> — ADR 0007 Sprint 3 PR 2.
/// Standalone (no Postgres, no HTTP host) — the store talks to the filesystem
/// only, so we get away with a per-class temp directory and skip the
/// <c>PostgresCollection</c> queue. Each test class instance owns its own
/// scratch root and cleans it up on dispose.
/// </summary>
public sealed class LocalArtifactStoreTests : IDisposable
{
    private readonly string _root;
    private readonly LocalArtifactStore _store;

    public LocalArtifactStoreTests()
    {
        _root = Path.Combine(
            Path.GetTempPath(),
            "lintty-artifact-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        var opts = Options.Create(new JobStorageOptions
        {
            // Absolute path so resolution doesn't depend on cwd at test time.
            ArtifactsRoot = _root,
        });
        _store = new LocalArtifactStore(opts);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // best effort; tmp dir, OS will reap eventually
        }
    }

    [Fact]
    public async Task Reserve_Returns_Path_Containing_PublicId_And_Kind()
    {
        var id = Guid.NewGuid();

        var pdfPath = await _store.ReserveAsync(id, ArtifactKind.LaudoPdf, CancellationToken.None);

        Assert.True(Path.IsPathRooted(pdfPath), "ReserveAsync must return an absolute path.");
        Assert.Contains(id.ToString("D"), pdfPath, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("laudo.pdf", pdfPath, StringComparison.Ordinal);

        // Parent directory must exist after Reserve so the engine subprocess
        // can write to the path without further ceremony.
        var parent = Path.GetDirectoryName(pdfPath);
        Assert.NotNull(parent);
        Assert.True(Directory.Exists(parent), $"Parent dir not created: {parent}");

        // The file itself must NOT exist yet — Reserve only reserves.
        Assert.False(File.Exists(pdfPath), "Reserve must not create the file itself.");
    }

    [Fact]
    public async Task Reserve_Same_Pid_Different_Kind_Returns_Different_Path()
    {
        var id = Guid.NewGuid();

        var pdfPath = await _store.ReserveAsync(id, ArtifactKind.LaudoPdf, CancellationToken.None);
        var jsonPath = await _store.ReserveAsync(id, ArtifactKind.ReportJson, CancellationToken.None);

        Assert.NotEqual(pdfPath, jsonPath);
        Assert.EndsWith("laudo.pdf", pdfPath, StringComparison.Ordinal);
        Assert.EndsWith("report.json", jsonPath, StringComparison.Ordinal);

        // Both kinds share the same scan directory.
        Assert.Equal(Path.GetDirectoryName(pdfPath), Path.GetDirectoryName(jsonPath));
    }

    [Fact]
    public async Task Reserve_Then_Write_Then_Open_Roundtrip()
    {
        var id = Guid.NewGuid();
        var payload = new byte[4096];
        new Random(42).NextBytes(payload);
        var expectedHash = Sha256(payload);

        var path = await _store.ReserveAsync(id, ArtifactKind.LaudoPdf, CancellationToken.None);
        await File.WriteAllBytesAsync(path, payload);

        await using var stream = await _store.OpenAsync(id, ArtifactKind.LaudoPdf, CancellationToken.None);
        Assert.NotNull(stream);

        using var ms = new MemoryStream();
        await stream!.CopyToAsync(ms);
        var actual = ms.ToArray();

        Assert.Equal(payload.Length, actual.Length);
        Assert.Equal(expectedHash, Sha256(actual));
    }

    [Fact]
    public async Task Open_Nonexistent_Returns_Null()
    {
        var id = Guid.NewGuid();

        var stream = await _store.OpenAsync(id, ArtifactKind.LaudoPdf, CancellationToken.None);

        Assert.Null(stream);
    }

    [Fact]
    public async Task Exists_True_After_Write()
    {
        var id = Guid.NewGuid();
        var path = await _store.ReserveAsync(id, ArtifactKind.ReportJson, CancellationToken.None);
        await File.WriteAllTextAsync(path, "{}");

        var exists = await _store.ExistsAsync(id, ArtifactKind.ReportJson, CancellationToken.None);

        Assert.True(exists);
    }

    [Fact]
    public async Task Exists_False_Before_Write()
    {
        var id = Guid.NewGuid();

        // Even if we reserve (which creates the parent dir), the file itself
        // is not present until the engine writes it.
        await _store.ReserveAsync(id, ArtifactKind.ReportJson, CancellationToken.None);

        var exists = await _store.ExistsAsync(id, ArtifactKind.ReportJson, CancellationToken.None);

        Assert.False(exists);
    }

    [Fact]
    public async Task Delete_Is_Idempotent()
    {
        var id = Guid.NewGuid();
        var path = await _store.ReserveAsync(id, ArtifactKind.LaudoPdf, CancellationToken.None);
        await File.WriteAllBytesAsync(path, new byte[] { 1, 2, 3 });

        await _store.DeleteAsync(id, CancellationToken.None);
        // Second call must not throw — idempotency is part of the contract
        // so retention cleanup can chain deletes without coordinating state.
        await _store.DeleteAsync(id, CancellationToken.None);

        var exists = await _store.ExistsAsync(id, ArtifactKind.LaudoPdf, CancellationToken.None);
        Assert.False(exists);
    }

    [Fact]
    public async Task Delete_Removes_Both_Artifacts()
    {
        var id = Guid.NewGuid();
        var pdfPath = await _store.ReserveAsync(id, ArtifactKind.LaudoPdf, CancellationToken.None);
        var jsonPath = await _store.ReserveAsync(id, ArtifactKind.ReportJson, CancellationToken.None);
        await File.WriteAllBytesAsync(pdfPath, new byte[] { 1 });
        await File.WriteAllTextAsync(jsonPath, "{}");

        Assert.True(await _store.ExistsAsync(id, ArtifactKind.LaudoPdf, CancellationToken.None));
        Assert.True(await _store.ExistsAsync(id, ArtifactKind.ReportJson, CancellationToken.None));

        await _store.DeleteAsync(id, CancellationToken.None);

        Assert.False(await _store.ExistsAsync(id, ArtifactKind.LaudoPdf, CancellationToken.None));
        Assert.False(await _store.ExistsAsync(id, ArtifactKind.ReportJson, CancellationToken.None));
    }

    [Fact]
    public async Task Sharding_Fans_Out_By_First_2_Chars()
    {
        // Hand-pick UUIDs whose hex form starts with 3 distinct prefixes to
        // confirm the shard appears in the path. We don't rely on specific
        // implementation details (e.g. that the shard is the literal parent
        // dir name) — only that paths with different prefixes don't share
        // a parent directory.
        var a = new Guid("aabbccdd-1111-4111-8111-111111111111");
        var b = new Guid("bbbbccdd-2222-4222-8222-222222222222");
        var c = new Guid("11223344-3333-4333-8333-333333333333");

        var pa = await _store.ReserveAsync(a, ArtifactKind.LaudoPdf, CancellationToken.None);
        var pb = await _store.ReserveAsync(b, ArtifactKind.LaudoPdf, CancellationToken.None);
        var pc = await _store.ReserveAsync(c, ArtifactKind.LaudoPdf, CancellationToken.None);

        var parents = new[] { pa, pb, pc }
            .Select(p => Path.GetDirectoryName(Path.GetDirectoryName(p))!)
            .ToArray();

        // Each scan's *grandparent* (the shard dir) must differ between
        // distinct prefixes — this is the whole point of sharding.
        Assert.Equal(3, parents.Distinct(StringComparer.OrdinalIgnoreCase).Count());

        // And the grandparent's name must literally be the 2-char prefix.
        Assert.Equal("aa", new DirectoryInfo(parents[0]).Name, ignoreCase: true);
        Assert.Equal("bb", new DirectoryInfo(parents[1]).Name, ignoreCase: true);
        Assert.Equal("11", new DirectoryInfo(parents[2]).Name, ignoreCase: true);
    }

    private static string Sha256(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
