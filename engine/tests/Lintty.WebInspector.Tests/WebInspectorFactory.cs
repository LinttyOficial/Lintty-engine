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
using Lintty.WebInspector.Auth;
using Lintty.WebInspector.Canon;
using Lintty.WebInspector.Github;
using Lintty.WebInspector.Jobs;
using Lintty.WebInspector.Validation;
using Lintty.WebInspector.Tests.Fakes;

namespace Lintty.WebInspector.Tests;

/// <summary>
/// <see cref="WebApplicationFactory{TEntryPoint}"/> tuned for the contract
/// tests:
///   * Connection string for Postgres is supplied by the caller (the shared
///     <see cref="PostgresFixture"/>). ADR 0007 Sprint 1 swapped SQLite for
///     Postgres; the per-test scratch root that used to host the SQLite file
///     now hosts only the artifact tree (PDFs/JSONs).
///   * Each instance gets its own scratch artifact root under the system
///     temp dir, so PDF/JSON files don't leak between tests.
///   * The GitHub metadata client is swapped to <see cref="FakeGitHubMetadataClient"/>.
///   * Optionally swaps the git client and disables the worker for tests
///     that only exercise the HTTP contract.
/// </summary>
public sealed class WebInspectorFactory : WebApplicationFactory<Program>
{
    public string StorageRoot { get; }
    public string ConnectionString { get; }
    public FakeGitHubMetadataClient FakeGitHub { get; } = new();
    public FakeGitHubOAuthClient FakeGitHubOAuth { get; } = new();
    /// <summary>
    /// Fake for <see cref="IGitHubOrgsClient"/> — the read-side GitHub
    /// orgs/repos client introduced in PR 7. Tests script the orgs +
    /// repos this fake "sees" before issuing the request; defaults to
    /// empty so a test that doesn't care simply sees an empty list.
    /// </summary>
    public FakeGitHubOrgsClient FakeGitHubOrgs { get; } = new();

    /// <summary>
    /// When set, the factory wires GitHub OAuth credentials so the
    /// <c>/api/auth/github/start</c> and <c>/callback</c> endpoints are
    /// reachable. Tests that exercise OAuth flow set this to a non-empty
    /// value; tests for the unconfigured-503 path leave it empty.
    /// </summary>
    public bool EnableGitHubOAuth { get; set; }

    /// <summary>
    /// Optional override for <see cref="IGitClient"/>. When null, the real
    /// <see cref="GitCliClient"/> is used (only relevant if a test actually
    /// lets the worker run).
    /// </summary>
    public IGitClient? GitClientOverride { get; set; }

    /// <summary>
    /// Optional decorator-style override for <see cref="IEngineRunner"/>.
    /// Receives the real <see cref="EngineSubprocessRunner"/> resolved from
    /// the host's DI graph and returns a wrapper. Used by PR 5's
    /// <c>WorkerIntegrationTests.DashboardScan_PinnedCanon_Survives_NewCanon</c>
    /// to capture the arguments the worker hands to the engine — the
    /// snapshotted <c>--canon-version</c> in particular.
    /// </summary>
    public Func<IEngineRunner, IEngineRunner>? EngineRunnerDecorator { get; set; }

    /// <summary>
    /// Optional override for <see cref="ICanonVersionProvider"/>. PR 5's
    /// canon-snapshot regression swaps in
    /// <see cref="Fakes.ScriptedCanonVersionProvider"/> so it can flip the
    /// "current canon" between trigger and worker pickup and prove the
    /// scan row's <c>canon_version</c> column survives the bump (§3.7).
    /// </summary>
    public ICanonVersionProvider? CanonVersionProviderOverride { get; set; }

    /// <summary>
    /// When true, the <see cref="JobWorker"/> background service is removed
    /// from the host. Contract tests do this so jobs sit in <c>queued</c>
    /// without being claimed mid-assertion.
    /// </summary>
    public bool DisableWorker { get; set; } = true;

    /// <summary>
    /// Constructs a factory pointed at the supplied Postgres connection. In
    /// practice the connection comes from <see cref="PostgresFixture.ConnectionString"/>.
    /// </summary>
    public WebInspectorFactory(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException(
                "Postgres connection string is required. Tests must use the shared " +
                "PostgresFixture (collection 'Postgres'); see PostgresFixture.cs.",
                nameof(connectionString));
        }
        ConnectionString = connectionString;
        StorageRoot = Path.Combine(Path.GetTempPath(), "lintty-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(StorageRoot);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");

        builder.ConfigureAppConfiguration((_, cfg) =>
        {
            var settings = new Dictionary<string, string?>
            {
                ["Postgres:ConnectionString"] = ConnectionString,
                ["JobStorage:Root"] = StorageRoot,
                ["Engine:CliDllPath"] = TestPaths.EngineCliDll,
                ["Engine:JobTimeoutSeconds"] = "300",
                ["Engine:CloneTimeoutSeconds"] = "60",
                ["Queue:PollIntervalMs"] = "100",
                ["Queue:MaxLength"] = "5",
                ["RateLimit:JobsPerIpPerDay"] = "3",
            };
            if (EnableGitHubOAuth)
            {
                settings["Github:ClientId"] = "test-client-id";
                settings["Github:ClientSecret"] = "test-client-secret";
            }
            cfg.AddInMemoryCollection(settings);
        });

        builder.ConfigureServices(services =>
        {
            // Swap the GitHub metadata client. We have to remove BOTH the
            // typed client and the HttpClient registration the AddHttpClient
            // extension created.
            RemoveAll<IGitHubMetadataClient>(services);
            services.AddSingleton<IGitHubMetadataClient>(FakeGitHub);

            // Swap the OAuth client unconditionally — tests never hit github.com.
            RemoveAll<IGitHubOAuthClient>(services);
            services.AddSingleton<IGitHubOAuthClient>(FakeGitHubOAuth);

            // Same for the orgs/repos client. AddHttpClient<TI, TImpl>
            // registers BOTH the typed client + the HttpMessageHandler
            // factory entry; RemoveAll on TI only catches the first half,
            // but the swapped Singleton wins because DI always resolves
            // the LAST registration for a service type.
            RemoveAll<IGitHubOrgsClient>(services);
            services.AddSingleton<IGitHubOrgsClient>(FakeGitHubOrgs);

            if (GitClientOverride is not null)
            {
                RemoveAll<IGitClient>(services);
                services.AddSingleton(GitClientOverride);
            }

            if (CanonVersionProviderOverride is not null)
            {
                RemoveAll<ICanonVersionProvider>(services);
                services.AddSingleton(CanonVersionProviderOverride);
            }

            if (EngineRunnerDecorator is not null)
            {
                // Replace the singleton IEngineRunner with a factory that
                // builds the real EngineSubprocessRunner and wraps it via
                // the test-supplied decorator. We rebuild the inner via
                // ActivatorUtilities so it picks up IOptions/ILogger from
                // the host DI graph — same wiring Program.cs uses.
                RemoveAll<IEngineRunner>(services);
                services.AddSingleton<IEngineRunner>(sp =>
                {
                    var inner = ActivatorUtilities.CreateInstance<EngineSubprocessRunner>(sp);
                    return EngineRunnerDecorator(inner);
                });
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
