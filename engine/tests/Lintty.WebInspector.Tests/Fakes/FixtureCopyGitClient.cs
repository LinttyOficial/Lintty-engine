using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Lintty.WebInspector.Jobs;
using Lintty.WebInspector.Validation;

namespace Lintty.WebInspector.Tests.Fakes;

/// <summary>
/// Test fake for <see cref="IGitClient"/>: copies a local fixture directory
/// into the destination instead of running <c>git clone</c>. Lets the worker
/// integration test exercise the entire pipeline without network access.
/// </summary>
public sealed class FixtureCopyGitClient : IGitClient
{
    private readonly string _fixtureRoot;

    public FixtureCopyGitClient(string fixtureRoot)
    {
        _fixtureRoot = fixtureRoot;
    }

    public Task<GitCloneResult> CloneAsync(
        GitHubRepoCoordinates coords,
        string? reference,
        string? token,
        string destination,
        CancellationToken ct)
    {
        if (!Directory.Exists(_fixtureRoot))
            return Task.FromResult(new GitCloneResult(false, $"fixture root missing: {_fixtureRoot}"));
        if (Directory.Exists(destination))
            Directory.Delete(destination, recursive: true);
        Directory.CreateDirectory(destination);
        CopyRecursive(_fixtureRoot, destination);
        return Task.FromResult(new GitCloneResult(true, null));
    }

    private static void CopyRecursive(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var dir in Directory.EnumerateDirectories(source))
        {
            var name = Path.GetFileName(dir);
            // Skip transient build outputs to keep the worker honest about
            // running a clean restore the same way a real clone would.
            if (name is "bin" or "obj" or ".git") continue;
            CopyRecursive(dir, Path.Combine(dest, name));
        }
        foreach (var file in Directory.EnumerateFiles(source))
        {
            var dst = Path.Combine(dest, Path.GetFileName(file));
            File.Copy(file, dst, overwrite: true);
        }
    }
}
