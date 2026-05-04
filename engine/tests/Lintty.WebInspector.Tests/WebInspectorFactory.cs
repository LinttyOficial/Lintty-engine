using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Lintty.WebInspector;
using Lintty.WebInspector.Jobs;
using Lintty.WebInspector.Validation;
using Lintty.WebInspector.Tests.Fakes;

namespace Lintty.WebInspector.Tests;

/// <summary>
/// <see cref="WebApplicationFactory{TEntryPoint}"/> tuned for the contract
/// tests:
///   * Each instance gets its own scratch <see cref="JobStorage:Root"/> under
///     the system temp dir, so tests don't leak SQLite state into each other.
///   * The GitHub metadata client is swapped to <see cref="FakeGitHubMetadataClient"/>.
///   * Optionally swaps the git client and disables the worker for tests
///     that only exercise the HTTP contract.
/// </summary>
public sealed class WebInspectorFactory : WebApplicationFactory<Program>
{
    public string StorageRoot { get; }
    public FakeGitHubMetadataClient FakeGitHub { get; } = new();

    /// <summary>
    /// Optional override for <see cref="IGitClient"/>. When null, the real
    /// <see cref="GitCliClient"/> is used (only relevant if a test actually
    /// lets the worker run).
    /// </summary>
    public IGitClient? GitClientOverride { get; set; }

    /// <summary>
    /// When true, the <see cref="JobWorker"/> background service is removed
    /// from the host. Contract tests do this so jobs sit in <c>queued</c>
    /// without being claimed mid-assertion.
    /// </summary>
    public bool DisableWorker { get; set; } = true;

    public WebInspectorFactory()
    {
        StorageRoot = Path.Combine(Path.GetTempPath(), "lintty-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(StorageRoot);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");

        builder.ConfigureAppConfiguration((_, cfg) =>
        {
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JobStorage:Root"] = StorageRoot,
                ["Engine:CliDllPath"] = TestPaths.EngineCliDll,
                ["Engine:JobTimeoutSeconds"] = "300",
                ["Engine:CloneTimeoutSeconds"] = "60",
                ["Queue:PollIntervalMs"] = "100",
                ["Queue:MaxLength"] = "5",
                ["RateLimit:JobsPerIpPerDay"] = "3",
            });
        });

        builder.ConfigureServices(services =>
        {
            // Swap the GitHub metadata client. We have to remove BOTH the
            // typed client and the HttpClient registration the AddHttpClient
            // extension created.
            RemoveAll<IGitHubMetadataClient>(services);
            services.AddSingleton<IGitHubMetadataClient>(FakeGitHub);

            if (GitClientOverride is not null)
            {
                RemoveAll<IGitClient>(services);
                services.AddSingleton(GitClientOverride);
            }

            if (DisableWorker)
            {
                var hosted = services.Where(d => d.ServiceType == typeof(IHostedService)
                    && d.ImplementationType == typeof(JobWorker)).ToList();
                foreach (var d in hosted) services.Remove(d);
            }
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            try { if (Directory.Exists(StorageRoot)) Directory.Delete(StorageRoot, recursive: true); }
            catch { /* best effort */ }
        }
    }

    private static void RemoveAll<T>(IServiceCollection services)
    {
        var doomed = services.Where(d => d.ServiceType == typeof(T)).ToList();
        foreach (var d in doomed) services.Remove(d);
    }
}
