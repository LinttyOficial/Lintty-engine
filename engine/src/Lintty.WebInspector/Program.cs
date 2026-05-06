using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi.Models;
using Lintty.WebInspector.Configuration;
using Lintty.WebInspector.Endpoints;
using Lintty.WebInspector.Jobs;
using Lintty.WebInspector.Validation;

namespace Lintty.WebInspector;

/// <summary>
/// Entry point for the Lintty Web Inspector backend (V0). ASP.NET Core 8
/// minimal API. Exposes:
///   POST /api/jobs                  – enqueue a scan
///   GET  /api/jobs/{id}             – poll status
///   GET  /api/jobs/{id}/laudo.pdf   – download PDF artifact
///   GET  /api/jobs/{id}/report.json – download JSON artifact
///   GET  /healthz                   – liveness
///
/// The worker is a single-instance <see cref="JobWorker"/> background service
/// that pulls queued jobs from Postgres, shells out to the engine CLI as a
/// subprocess, and cleans up <c>/tmp/lintty-&lt;id&gt;</c>. Backing store moved
/// from SQLite to Postgres in ADR 0007 Sprint 1.
///
/// Non-static so tests can use <c>WebApplicationFactory&lt;Program&gt;</c>
/// (the factory's TEntryPoint generic parameter requires a non-abstract,
/// non-static class).
/// </summary>
public class Program
{
    public static void Main(string[] args)
    {
        // Determinism guard. The Web Inspector itself doesn't generate the PDF —
        // the engine subprocess does — but we still pin invariant culture here
        // so any string formatting in the API path (timestamps, sizes) matches
        // the contract regardless of host locale.
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
        Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;

        var builder = WebApplication.CreateBuilder(args);
        ConfigureServices(builder);

        var app = builder.Build();
        ConfigurePipeline(app);
        InitializeStorage(app);

        app.Run();
    }

    public static void ConfigureServices(WebApplicationBuilder builder)
    {
        RegisterOptions(builder);
        RegisterJsonSerialization(builder.Services);
        RegisterJobsInfrastructure(builder.Services);
        RegisterEngineRunner(builder.Services);
        RegisterValidationServices(builder.Services);
        RegisterBackgroundWorkers(builder.Services);
        RegisterOpenApi(builder.Services);
    }

    private static void RegisterOptions(WebApplicationBuilder builder)
    {
        builder.Services.Configure<JobStorageOptions>(builder.Configuration.GetSection(JobStorageOptions.SectionName));
        builder.Services.Configure<PostgresOptions>(builder.Configuration.GetSection(PostgresOptions.SectionName));
        builder.Services.Configure<EngineOptions>(builder.Configuration.GetSection(EngineOptions.SectionName));
        builder.Services.Configure<RateLimitOptions>(builder.Configuration.GetSection(RateLimitOptions.SectionName));
        builder.Services.Configure<QueueOptions>(builder.Configuration.GetSection(QueueOptions.SectionName));
    }

    private static void RegisterJsonSerialization(IServiceCollection services)
    {
        services.Configure<JsonOptions>(o =>
        {
            // Compact responses; no whitespace surprises in golden curls.
            o.SerializerOptions.WriteIndented = false;
        });
    }

    private static void RegisterJobsInfrastructure(IServiceCollection services)
    {
        // ADR 0007 Sprint 1: SQLite is gone; Postgres is the only backing
        // store. PostgresJobStore is safe as singleton (NpgsqlConnection is
        // constructed per call and disposed; Npgsql does its own pooling).
        services.AddSingleton<IJobStore, PostgresJobStore>();
        services.AddSingleton<IGitClient, GitCliClient>();
    }

    private static void RegisterEngineRunner(IServiceCollection services)
    {
        services.AddSingleton<IEngineRunner, EngineSubprocessRunner>();
    }

    private static void RegisterValidationServices(IServiceCollection services)
    {
        services.AddHttpClient<IGitHubMetadataClient, GitHubMetadataClient>(GitHubMetadataClient.HttpClientName);
    }

    private static void RegisterBackgroundWorkers(IServiceCollection services)
    {
        services.AddHostedService<JobWorker>();
    }

    private static void RegisterOpenApi(IServiceCollection services)
    {
        // ── OpenAPI / Swagger ───────────────────────────────────────────────
        // The Web Inspector API is public (no auth in V0) and documented to
        // clients. Swagger UI is therefore enabled in *all* environments —
        // there's nothing here that should be hidden in prod.
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v0", new OpenApiInfo
            {
                Title = "Lintty Web Inspector API",
                Version = "v0",
                Description =
                    "Backend HTTP API for the Lintty Web Inspector. Accepts a public GitHub URL, " +
                    "shallow-clones the repository, invokes the deterministic engine CLI as a " +
                    "subprocess, returns the same byte-identical PDF the local CLI would produce, " +
                    "and discards the clone. Same engine, same canon, same PDF as the CLI " +
                    "Self-Service path.\n\n" +
                    "Full spec: `docs/13-web-inspector.md` in the Lintty repository.",
                Contact = new OpenApiContact
                {
                    Name = "Lintty Support",
                    Email = "support@lintty.com",
                },
            });

            // Pull in XML doc comments from this assembly so <summary> on
            // endpoint methods and DTO properties shows up in Swagger UI.
            var xmlPath = Path.Combine(AppContext.BaseDirectory,
                Assembly.GetExecutingAssembly().GetName().Name + ".xml");
            if (File.Exists(xmlPath))
            {
                c.IncludeXmlComments(xmlPath, includeControllerXmlComments: true);
            }

            c.SupportNonNullableReferenceTypes();
        });
    }

    public static void ConfigurePipeline(WebApplication app)
    {
        // Swagger is on in dev *and* prod — see ConfigureServices comment.
        app.UseSwagger();
        app.UseSwaggerUI(o =>
        {
            o.SwaggerEndpoint("/swagger/v0/swagger.json", "Lintty Web Inspector v0");
            o.RoutePrefix = "swagger";
            o.DocumentTitle = "Lintty Web Inspector API";
        });

        // ── Static landing assets (dev convenience) ─────────────────────────
        // When the monorepo `landing/` folder is reachable, serve it at the
        // root so `http://localhost:5180/inspect.html` works same-origin with
        // the API and the front-end can `fetch('/api/jobs')` without CORS.
        // In production the landing is served by Cloudflare Pages, so this
        // path is intentionally absent from the deployment artifact and the
        // middleware silently disables itself.
        var landingRoot = ResolveLandingRoot(app);
        if (landingRoot is not null)
        {
            var fileProvider = new PhysicalFileProvider(landingRoot);
            app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = fileProvider });
            app.UseStaticFiles(new StaticFileOptions { FileProvider = fileProvider });
        }

        app.MapHealth();
        app.MapJobs();
    }

    private static string? ResolveLandingRoot(WebApplication app)
    {
        // Explicit override wins (production custom path or test fixtures).
        var configured = app.Configuration["Landing:Root"];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Directory.Exists(configured) ? Path.GetFullPath(configured) : null;
        }

        // Default monorepo layout: engine/src/Lintty.WebInspector/ → ../../../landing.
        var candidate = Path.GetFullPath(
            Path.Combine(app.Environment.ContentRootPath, "..", "..", "..", "landing"));
        return Directory.Exists(candidate) ? candidate : null;
    }

    public static void InitializeStorage(WebApplication app)
    {
        // Eagerly apply the Postgres schema (idempotent — IF NOT EXISTS) so
        // the first request is fast and any DB connectivity failure surfaces
        // at startup instead of mid-request.
        using var scope = app.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IJobStore>();
        store.InitializeAsync(default).GetAwaiter().GetResult();
    }
}
