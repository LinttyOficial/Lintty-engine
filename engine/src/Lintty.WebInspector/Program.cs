using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using AspNet.Security.OAuth.GitHub;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Rewrite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using Lintty.WebInspector.Artifacts;
using Lintty.WebInspector.Auth;
using Lintty.WebInspector.Canon;
using Lintty.WebInspector.Configuration;
using Lintty.WebInspector.Endpoints;
using Lintty.WebInspector.Github;
using Lintty.WebInspector.Jobs;
using Lintty.WebInspector.Persistence;
using Lintty.WebInspector.Persistence.Entities;
using Lintty.WebInspector.Repos;
using Lintty.WebInspector.Scans;
using Lintty.WebInspector.Validation;

namespace Lintty.WebInspector;

/// <summary>
/// Entry point for the Lintty Web Inspector backend. ASP.NET Core 8 minimal
/// API. Routes:
///   POST /api/jobs                  – enqueue a scan (V0 anonymous; logged-in passthrough)
///   GET  /api/jobs/{id}             – poll status
///   GET  /api/jobs/{id}/laudo.pdf   – download PDF artifact
///   GET  /api/jobs/{id}/report.json – download JSON artifact
///   POST /api/auth/signup           – ADR 0007 Sprint 2
///   POST /api/auth/login            – Sprint 2
///   POST /api/auth/logout           – Sprint 2
///   GET  /api/auth/me               – Sprint 2
///   GET  /api/auth/github/start     – Sprint 2 (OAuth GitHub login, scopes mínimos)
///   GET  /api/auth/github/callback  – Sprint 2
///   GET  /api/auth/github/connect/start    – Sprint 3 PR 6 (OAuth elevated, repo + read:org)
///   GET  /api/auth/github/connect/callback – Sprint 3 PR 6
///   GET  /api/auth/github/connect          – Sprint 3 PR 6 (status — privacy console)
///   DELETE /api/auth/github/connect        – Sprint 3 PR 6 (soft-revoke local)
///   POST /api/repos                 – ADR 0007 Sprint 3 PR 3 (manual add)
///   POST /api/repos/import          – Sprint 3 PR 7 (import from connected GitHub org)
///   GET  /api/repos                 – Sprint 3 PR 3 (list)
///   GET  /api/repos/{id}            – Sprint 3 PR 3 (detail)
///   DELETE /api/repos/{id}          – Sprint 3 PR 3 (soft-delete)
///   GET  /api/repos/{id}/scans      – Sprint 3 PR 4 (scan history)
///   POST /api/scans                 – Sprint 3 PR 4 (trigger)
///   GET  /api/scans/{public_id}     – Sprint 3 PR 4 (poll)
///   GET  /api/scans/{public_id}/laudo.pdf  – Sprint 3 PR 4 (PDF)
///   GET  /api/scans/{public_id}/report.json – Sprint 3 PR 4 (JSON)
///   GET  /api/github/orgs           – Sprint 3 PR 7 (list connected user's orgs + UPSERT cache)
///   GET  /api/github/orgs/{login}/repos – Sprint 3 PR 7 (list repos under {login})
///   GET  /healthz                   – liveness
///
/// Sprint 2 introduces auth (Identity + cookie + GitHub OAuth) and the tenant
/// context middleware. **V0 contract is preserved bit-for-bit** —
/// <c>/api/jobs</c> stays anonymous-friendly; logged-in callers are tagged
/// onto their org via the middleware but the endpoint behavior is identical.
/// Cross-determinism gate (<c>WorkerIntegrationTests</c>) must remain green.
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
        RegisterPersistence(builder);
        RegisterIdentityAndAuth(builder);
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
        builder.Services.Configure<GitHubOAuthOptions>(builder.Configuration.GetSection(GitHubOAuthOptions.SectionName));
    }

    private static void RegisterJsonSerialization(IServiceCollection services)
    {
        services.Configure<JsonOptions>(o =>
        {
            // Compact responses; no whitespace surprises in golden curls.
            o.SerializerOptions.WriteIndented = false;
        });
    }

    /// <summary>
    /// EF Core + Postgres. ADR 0007 Sprint 2: <see cref="LinttyDbContext"/>
    /// owns the schema for Identity + tenant tables + the V0 jobs/rate_limits
    /// tables (absorbed from Sprint 1's embedded SQL). PostgresJobStore
    /// continues to use Dapper on the same connection string for the hot
    /// path — both stacks coexist on the same Postgres pool.
    /// </summary>
    private static void RegisterPersistence(WebApplicationBuilder builder)
    {
        // Resolve the connection string lazily through DI so any
        // ConfigureAppConfiguration layered on top by WebApplicationFactory
        // (test fixtures) wins over appsettings.json. Reading
        // builder.Configuration directly here would freeze the dev creds
        // before the test override applies.
        builder.Services.AddDbContext<LinttyDbContext>((sp, options) =>
        {
            var cfg = sp.GetRequiredService<IConfiguration>();
            var conn = cfg.GetSection(PostgresOptions.SectionName)["ConnectionString"];
            options.UseNpgsql(conn);
            // EFCore.NamingConventions converts PascalCase property names to
            // snake_case columns. Identity table names themselves are
            // overridden in LinttyDbContext.OnModelCreating.
            options.UseSnakeCaseNamingConvention();
        });
    }

    /// <summary>
    /// Identity + cookie + OAuth GitHub. ADR 0007 Sprint 2 §3.1: cookie
    /// server-side, PBKDF2 default (Argon2 deferred V1.1). GitHub OAuth is
    /// registered as an additional authentication scheme so the SignInManager
    /// can complete external login via <c>HttpContext.AuthenticateAsync</c>.
    /// </summary>
    private static void RegisterIdentityAndAuth(WebApplicationBuilder builder)
    {
        builder.Services
            .AddIdentityCore<User>(opts =>
            {
                // Defaults follow Identity 8 — minimum length 6, requires digit, lowercase, uppercase, non-alphanumeric.
                opts.User.RequireUniqueEmail = true;
                opts.SignIn.RequireConfirmedEmail = false;     // V1.0: email verification deferred
                opts.SignIn.RequireConfirmedAccount = false;
                // PBKDF2 defaults are baked into PasswordHasher<T>; no extra config.
            })
            .AddRoles<Role>()
            .AddEntityFrameworkStores<LinttyDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders();

        var auth = builder.Services
            .AddAuthentication(IdentityConstants.ApplicationScheme)
            .AddCookie(IdentityConstants.ApplicationScheme, opts =>
            {
                opts.Cookie.Name = "lintty_auth";
                opts.Cookie.HttpOnly = true;
                opts.Cookie.SameSite = SameSiteMode.Lax;
                opts.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                opts.ExpireTimeSpan = System.TimeSpan.FromDays(14);
                opts.SlidingExpiration = true;
                // API-style behavior: don't redirect to a /Login page; emit 401.
                opts.Events.OnRedirectToLogin = ctx =>
                {
                    ctx.Response.StatusCode = 401;
                    return System.Threading.Tasks.Task.CompletedTask;
                };
                opts.Events.OnRedirectToAccessDenied = ctx =>
                {
                    ctx.Response.StatusCode = 403;
                    return System.Threading.Tasks.Task.CompletedTask;
                };
            });

        // ADR 0007 §3.6: AspNet.Security.OAuth.GitHub is registered
        // unconditionally with placeholder credentials. The actual flow goes
        // through our hand-rolled endpoints in AuthEndpoints (which call
        // IGitHubOAuthClient directly), so the framework handler's
        // ChallengeAsync path is never invoked unless an operator explicitly
        // wires it later. PostConfigure binds real creds from IConfiguration
        // at request time so test fixtures and env vars both win over
        // appsettings.json without a special-case at registration.
        auth.AddGitHub(opts =>
        {
            opts.ClientId = "placeholder";
            opts.ClientSecret = "placeholder";
            opts.Scope.Clear();
            opts.Scope.Add("read:user");
            opts.Scope.Add("user:email");
            opts.SaveTokens = false;
        });
        builder.Services.AddSingleton<IPostConfigureOptions<GitHubAuthenticationOptions>, GitHubOAuthOptionsBinder>();

        builder.Services.AddAuthorization();

        // Auth abstractions used by the endpoints + middleware.
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<ITenantContext, TenantContext>();
        builder.Services.AddHttpClient<IGitHubOAuthClient, GitHubOAuthClient>(GitHubOAuthClient.HttpClientName);

        // ADR 0007 Apêndice E §E.6 — DataProtection for the connect-flow
        // OAuth user-token. Keys persist on disk under
        // {JobStorage:Root}/data-protection so a single-instance deploy
        // (V1.0 default) survives restarts; multi-instance deploys upgrade
        // to Azure Key Vault / GCP KMS in V1.1 (pendência E1).
        // SetApplicationName is mandatory — without it, two Lintty processes
        // with different assembly names would generate distinct keyrings and
        // existing ciphertext would become unreadable.
        builder.Services.AddDataProtection()
            .SetApplicationName("Lintty.WebInspector")
            .PersistKeysToFileSystem(new DirectoryInfo(ResolveDataProtectionDir(builder)));

        // Per-user GitHub OAuth token store (Apêndice E §E.6). Scoped
        // because it holds an EF DbContext (also scoped). The protector it
        // uses is resolved through IDataProtectionProvider, which is a
        // singleton — fine to share across scopes.
        builder.Services.AddScoped<IGitHubUserTokenStore, GithubUserTokenStore>();
    }

    /// <summary>
    /// Computes the on-disk path for DataProtection keys. Honors an
    /// explicit override (<c>JobStorage:DataProtectionDir</c>) and otherwise
    /// nests under <c>{JobStorage:Root}/data-protection</c>. The directory
    /// is created if missing — DataProtection asserts the parent exists.
    /// </summary>
    private static string ResolveDataProtectionDir(WebApplicationBuilder builder)
    {
        var cfg = builder.Configuration.GetSection(JobStorageOptions.SectionName);
        var root = cfg["Root"];
        if (string.IsNullOrWhiteSpace(root))
        {
            // Mirror JobStorageOptions default exactly.
            root = "var/lintty";
        }
        var resolved = Path.IsPathRooted(root)
            ? root
            : Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, root));
        var dir = Path.Combine(resolved, "data-protection");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void RegisterJobsInfrastructure(IServiceCollection services)
    {
        // ADR 0007 Sprint 1: Postgres backing store. Sprint 2: schema is owned
        // by the EF migration but PostgresJobStore continues to drive the hot
        // path via Dapper.
        services.AddSingleton<IJobStore, PostgresJobStore>();
        services.AddSingleton<IGitClient, GitCliClient>();

        // ADR 0007 Sprint 3 §3.8: artifact storage abstraction. V1.0 ships
        // only the local impl; S3ArtifactStore lands in V1.1. Singleton
        // because the store is stateless — the filesystem is the truth.
        services.AddSingleton<IArtifactStore, LocalArtifactStore>();

        // ADR 0007 Sprint 3 §3.7 PR 4: org-bound scan queue (sibling of
        // IJobStore for the V0 anonymous flow). Singleton because the queue
        // is stateless — Postgres owns the truth, the class only holds a
        // connection string + logger.
        services.AddSingleton<IScanQueue, PostgresScanQueue>();
    }

    private static void RegisterEngineRunner(IServiceCollection services)
    {
        services.AddSingleton<IEngineRunner, EngineSubprocessRunner>();
    }

    private static void RegisterValidationServices(IServiceCollection services)
    {
        services.AddHttpClient<IGitHubMetadataClient, GitHubMetadataClient>(GitHubMetadataClient.HttpClientName);

        // ADR 0007 Apêndice E §E.7 / §E.8 / Sprint 3 PR 7 — read-side GitHub
        // orgs/repos client used by /api/github/* and the org-import path
        // in RepoService.ImportFromGithubOrgAsync. Distinct HttpClient pool
        // from the metadata client (different UA + accept headers) so the
        // two surfaces don't share connection state.
        services.AddHttpClient<IGitHubOrgsClient, GitHubOrgsClient>(GitHubOrgsClient.HttpClientName);

        // ADR 0007 Sprint 3 / PR 3 — manual repo CRUD. Scoped because the
        // service holds an EF DbContext (also scoped). PR 7 widened the
        // surface with ImportFromGithubOrgAsync (Apêndice E §E.8).
        services.AddScoped<IRepoService, RepoService>();

        // ADR 0007 Sprint 3 / PR 4 — org-bound scan lifecycle. Scoped (EF
        // DbContext). Canon version provider is a Singleton (pure function
        // of the host's configuration); will be swapped per-org/per-repo
        // in V1.1 without disturbing this wiring.
        services.AddSingleton<ICanonVersionProvider, DefaultCanonVersionProvider>();
        services.AddScoped<IScanService, ScanService>();
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
        var landingRoot = ResolveLandingRoot(app);
        if (landingRoot is not null)
        {
            // Next.js with trailingSlash:true emits routes as folders containing
            // index.html (out/inspect/index.html, out/login/index.html, ...).
            // Visiting /inspect (no trailing slash) would 404 because the static
            // files middleware doesn't redirect for directories. Rewrite any
            // extension-less path to its trailing-slash form so the browser
            // request lands on the directory, then UseDefaultFiles serves index.html.
            var rewriteOptions = new RewriteOptions()
                .AddRedirect(@"^(?!api/|swagger)(?!.*\.).+(?<!/)$", "$0/", statusCode: 301);
            app.UseRewriter(rewriteOptions);

            var fileProvider = new PhysicalFileProvider(landingRoot);
            app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = fileProvider });
            app.UseStaticFiles(new StaticFileOptions { FileProvider = fileProvider });

            // ── SPA dynamic-segment fallback (Sprint 3 PR F5/F6) ────────────
            // Next.js with output:"export" emits placeholder index.html for
            // dynamic segments at out/dashboard/repos/_/index.html and
            // out/dashboard/scans/_/index.html. The real ids only exist
            // client-side (window.location), so requests like
            // /dashboard/repos/42 or /dashboard/scans/<uuid> have no exact
            // file on disk and UseStaticFiles 404s. Cloudflare Pages has an
            // implicit SPA fallback in prod; this dotnet host doesn't.
            //
            // Scoped narrowly: regex matches a single segment after
            // /dashboard/repos or /dashboard/scans (with optional trailing
            // slash) and nothing deeper, so /api/* and any unrelated path
            // remain untouched. Defense in depth: if a real file ever lands
            // at the requested path, UseStaticFiles above already served it
            // and this middleware never runs.
            var dashboardReposRe = new Regex(@"^/dashboard/repos/[^/]+/?$",
                RegexOptions.Compiled | RegexOptions.CultureInvariant);
            var dashboardScansRe = new Regex(@"^/dashboard/scans/[^/]+/?$",
                RegexOptions.Compiled | RegexOptions.CultureInvariant);

            app.MapWhen(
                ctx =>
                {
                    var path = ctx.Request.Path.Value;
                    if (string.IsNullOrEmpty(path)) return false;
                    return dashboardReposRe.IsMatch(path) || dashboardScansRe.IsMatch(path);
                },
                branch => branch.Run(async ctx =>
                {
                    var path = ctx.Request.Path.Value!;
                    var placeholder = path.StartsWith("/dashboard/repos/", StringComparison.Ordinal)
                        ? "dashboard/repos/_/index.html"
                        : "dashboard/scans/_/index.html";
                    var filePath = Path.Combine(
                        landingRoot,
                        placeholder.Replace('/', Path.DirectorySeparatorChar));
                    if (!File.Exists(filePath))
                    {
                        ctx.Response.StatusCode = 404;
                        return;
                    }
                    ctx.Response.ContentType = "text/html; charset=utf-8";
                    // The placeholder is identical for every id; caching it
                    // under a specific URL would pin a stale build manifest
                    // when Next bumps hashes. The SPA itself is tiny.
                    ctx.Response.Headers["Cache-Control"] = "no-store";
                    await ctx.Response.SendFileAsync(filePath);
                }));
        }

        // ── Auth pipeline ───────────────────────────────────────────────────
        // Authentication / Authorization come BEFORE the tenant middleware so
        // HttpContext.User is populated by the time we resolve the org.
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseMiddleware<TenantContextMiddleware>();

        app.MapHealth();
        app.MapJobs();
        app.MapAuth();
        app.MapAuthGithubConnect();
        app.MapGithub();
        app.MapRepos();
        app.MapScans();
    }

    private static string? ResolveLandingRoot(WebApplication app)
    {
        // Explicit override wins (production custom path or test fixtures).
        var configured = app.Configuration["Landing:Root"];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Directory.Exists(configured) ? Path.GetFullPath(configured) : null;
        }

        // Default monorepo layout: engine/src/Lintty.WebInspector/ → ../../../frontend/out.
        // The frontend is now a Next.js static export under frontend/out (see frontend/README.md).
        // Run `npm run build` in /frontend before booting the host if you want the SPA served
        // same-origin at http://localhost:5180/. In production, the frontend is deployed
        // separately to Cloudflare Pages and this folder may not exist — the middleware
        // simply no-ops when the path is missing.
        var candidate = Path.GetFullPath(
            Path.Combine(app.Environment.ContentRootPath, "..", "..", "..", "frontend", "out"));
        return Directory.Exists(candidate) ? candidate : null;
    }

    /// <summary>
    /// In dev (or under a test factory), apply pending EF migrations at
    /// startup so first-request latency is not affected and any DB connectivity
    /// failure surfaces here. In prod, operators run
    /// <c>dotnet ef database update</c> manually before booting the app —
    /// see <c>engine/README.md</c>.
    /// </summary>
    public static void InitializeStorage(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var sp = scope.ServiceProvider;

        if (app.Environment.IsDevelopment() || app.Environment.EnvironmentName == "Test")
        {
            var db = sp.GetRequiredService<LinttyDbContext>();
            db.Database.Migrate();
        }

        // Keep IJobStore.InitializeAsync invocation for any non-EF impls that
        // future work might add (in-memory tests, e.g.); current PostgresJobStore
        // implementation is a no-op since Sprint 2.
        var store = sp.GetRequiredService<IJobStore>();
        store.InitializeAsync(default).GetAwaiter().GetResult();
    }
}
