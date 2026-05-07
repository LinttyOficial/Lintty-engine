using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Lintty.WebInspector.Configuration;

namespace Lintty.WebInspector.Artifacts;

/// <summary>
/// Filesystem-backed <see cref="IArtifactStore"/>. ADR 0007 §3.8 — the V1.0
/// default. The cloud counterpart (<c>S3ArtifactStore</c>) lands in V1.1 and
/// will share this contract.
///
/// <para>
/// <b>Path scheme.</b>
/// <c>{ArtifactsRoot}/scans/{2-char shard}/{public_id_hex}/{filename}</c>.
/// The shard is the first 2 hex chars of the (dashed) UUID — example:
/// <c>3bf1a8b2-4d9e-...</c> shards into <c>3b/</c>. With UUID v4 entropy the
/// 256 buckets fan out evenly, so the parent directory holding all scans
/// never exceeds a few thousand entries even with millions of total scans.
/// Avoids inode pressure / slow <c>readdir</c> on ext4/NTFS. Filenames are
/// fixed (<c>laudo.pdf</c>, <c>report.json</c>) so the layout is purely a
/// function of the public id.
/// </para>
///
/// <para>
/// <b>Concurrency.</b> No in-memory cache. The OS owns the truth; the same
/// PDF being read by two HTTP requests in flight is handled by opening with
/// <see cref="FileShare.Read"/>. A worker write racing a read is impossible
/// in practice (the endpoint only resolves the path after the scan reaches
/// <c>completed</c> in Postgres, and the worker writes-then-commits) but
/// the share mode keeps the contract honest if that invariant ever slips.
/// </para>
///
/// <para>
/// <b>Root resolution.</b> <see cref="JobStorageOptions.ArtifactsRoot"/> may
/// be relative (default <c>var/lintty/artifacts</c>) or absolute. We mirror
/// the existing pattern in <c>JobWorker.ResolveArtifactsDir</c>: relative is
/// joined to <see cref="Directory.GetCurrentDirectory"/> at call time, not
/// at startup, so test fixtures setting <c>JobStorage:Root</c> via
/// <c>WebApplicationFactory</c> still win.
/// </para>
/// </summary>
public sealed class LocalArtifactStore : IArtifactStore
{
    private readonly JobStorageOptions _options;

    public LocalArtifactStore(IOptions<JobStorageOptions> options)
    {
        _options = options.Value;
    }

    /// <inheritdoc />
    public Task<string> ReserveAsync(Guid publicId, ArtifactKind kind, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var dir = ResolveScanDir(publicId);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, FilenameFor(kind));
        return Task.FromResult(path);
    }

    /// <inheritdoc />
    public Task<Stream?> OpenAsync(Guid publicId, ArtifactKind kind, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var path = Path.Combine(ResolveScanDir(publicId), FilenameFor(kind));
        if (!File.Exists(path))
        {
            return Task.FromResult<Stream?>(null);
        }

        // Open with FileShare.Read so concurrent downloads of the same
        // artifact don't lock each other out, and Async because ASP.NET will
        // stream the result through Results.Stream(...) in PR 4.
        Stream stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            useAsync: true);
        return Task.FromResult<Stream?>(stream);
    }

    /// <inheritdoc />
    public Task<bool> ExistsAsync(Guid publicId, ArtifactKind kind, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var path = Path.Combine(ResolveScanDir(publicId), FilenameFor(kind));
        // File.Exists is a single syscall; wrapping it in Task.Run would buy
        // nothing but a thread hop. The interface is async to leave room for
        // S3ArtifactStore (which will need a real round-trip).
        return Task.FromResult(File.Exists(path));
    }

    /// <inheritdoc />
    public Task DeleteAsync(Guid publicId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var dir = ResolveScanDir(publicId);
        try
        {
            Directory.Delete(dir, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
            // Idempotent contract — already gone is success.
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Composes <c>{absolute root}/scans/{shard}/{full hex id}/</c>. The
    /// resolution happens on every call (not cached) so test fixtures that
    /// mutate <c>JobStorage:Root</c> at runtime still take effect — same
    /// rationale as <c>JobWorker.ResolveArtifactsDir</c>.
    /// </summary>
    private string ResolveScanDir(Guid publicId)
    {
        var rootRaw = _options.ArtifactsRoot;
        var root = Path.IsPathRooted(rootRaw)
            ? rootRaw
            : Path.Combine(Directory.GetCurrentDirectory(), rootRaw);

        // "D" format = 32 hex digits + 4 dashes, lowercase by default. We
        // shard on the first two hex chars (skipping any dashes — but "D"
        // never starts with one, so we just take Substring(0, 2)).
        var hex = publicId.ToString("D");
        var shard = hex.Substring(0, 2);
        return Path.Combine(root, "scans", shard, hex);
    }

    private static string FilenameFor(ArtifactKind kind) => kind switch
    {
        ArtifactKind.LaudoPdf => "laudo.pdf",
        ArtifactKind.ReportJson => "report.json",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown ArtifactKind."),
    };
}
