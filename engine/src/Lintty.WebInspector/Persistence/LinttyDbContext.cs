using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Lintty.WebInspector.Jobs;
using Lintty.WebInspector.Persistence.Entities;

namespace Lintty.WebInspector.Persistence;

/// <summary>
/// EF Core context for the Web Inspector. ADR 0007 Sprint 2: Identity entities
/// (<see cref="User"/>/<see cref="Role"/> with <c>long</c>/<c>bigserial</c>
/// PKs), tenant entities (<see cref="Org"/>, <see cref="OrgMember"/>,
/// <see cref="ExternalLogin"/>), plus the V0 tables (<see cref="Job"/>,
/// <see cref="RateLimit"/>) that Sprint 1 bootstrapped via raw SQL. The
/// migration <c>InitialIdentityAndTenant</c> absorbs all of them so EF is the
/// single source of truth for the schema from V1.0 onwards.
///
/// **Coexistence with Dapper:** PostgresJobStore continues to read/write
/// <c>jobs</c> and <c>rate_limits</c> via Dapper (hand-tuned
/// UPDATE...RETURNING for <c>ClaimNextQueuedAsync</c>; ON CONFLICT DO UPDATE
/// for the rate counter). EF owns DDL; Dapper owns the hot-path DML on those
/// two tables. This is the canonical pattern when one half of the app needs
/// LINQ ergonomics (Identity stores, tenant queries) and the other half needs
/// SQL precision (worker contention).
///
/// **snake_case naming:** applied via <c>EFCore.NamingConventions</c> in the
/// DI registration (<c>UseSnakeCaseNamingConvention</c>). Identity tables
/// therefore become <c>users</c>, <c>roles</c>, <c>user_claims</c>,
/// <c>user_logins</c>, <c>user_tokens</c>, <c>role_claims</c>,
/// <c>user_roles</c> — see <see cref="OnModelCreating"/> for the explicit
/// <c>ToTable</c> calls (Identity defaults to <c>AspNetUsers</c> etc., which
/// the convention can't rename — only column names are auto-mapped).
/// </summary>
public sealed class LinttyDbContext : IdentityDbContext<User, Role, long>
{
    public LinttyDbContext(DbContextOptions<LinttyDbContext> options) : base(options)
    {
    }

    public DbSet<Org> Orgs => Set<Org>();
    public DbSet<OrgMember> OrgMembers => Set<OrgMember>();
    public DbSet<ExternalLogin> ExternalLogins => Set<ExternalLogin>();
    public DbSet<Job> Jobs => Set<Job>();
    public DbSet<RateLimit> RateLimits => Set<RateLimit>();

    // ADR 0007 Sprint 3 — multi-tenant scan ownership + Apêndice E (OAuth
    // user-token elevation for org listing and private clone).
    public DbSet<Repo> Repos => Set<Repo>();
    public DbSet<Scan> Scans => Set<Scan>();
    public DbSet<GithubUserToken> GithubUserTokens => Set<GithubUserToken>();
    public DbSet<GithubOrg> GithubOrgs => Set<GithubOrg>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        // Identity setup must run first — it configures the AspNet* tables and
        // their relationships. Our overrides come after.
        base.OnModelCreating(builder);

        // ── pgcrypto extension ──────────────────────────────────────────────
        // Enables gen_random_uuid() used by scans.public_id default. Postgres
        // 13+ ships gen_random_uuid() in pgcrypto; Postgres 16 (our compose
        // image) has it but the extension still needs to be enabled in the
        // target database. EF emits CREATE EXTENSION IF NOT EXISTS pgcrypto
        // in the migration — idempotent, safe to re-apply.
        builder.HasPostgresExtension("pgcrypto");

        // ── Identity tables: rename from AspNet* to snake_case. ─────────────
        // EFCore.NamingConventions handles columns automatically, but the
        // table names IdentityDbContext sets are hard-coded ("AspNetUsers"
        // etc.) — we rename them here.
        builder.Entity<User>(b =>
        {
            b.ToTable("users");
            b.Property(u => u.DisplayName).HasMaxLength(128).IsRequired();
            b.Property(u => u.CreatedAt).HasDefaultValueSql("now() at time zone 'utc'");
        });
        builder.Entity<Role>(b => b.ToTable("roles"));
        builder.Entity<Microsoft.AspNetCore.Identity.IdentityUserClaim<long>>(b => b.ToTable("user_claims"));
        builder.Entity<Microsoft.AspNetCore.Identity.IdentityUserRole<long>>(b => b.ToTable("user_roles"));
        builder.Entity<Microsoft.AspNetCore.Identity.IdentityUserLogin<long>>(b => b.ToTable("user_logins"));
        builder.Entity<Microsoft.AspNetCore.Identity.IdentityRoleClaim<long>>(b => b.ToTable("role_claims"));
        builder.Entity<Microsoft.AspNetCore.Identity.IdentityUserToken<long>>(b => b.ToTable("user_tokens"));

        // ── orgs ────────────────────────────────────────────────────────────
        builder.Entity<Org>(b =>
        {
            b.ToTable("orgs");
            b.HasKey(o => o.Id);
            b.Property(o => o.Slug).HasMaxLength(64).IsRequired();
            b.HasIndex(o => o.Slug).IsUnique();
            b.Property(o => o.Name).HasMaxLength(128).IsRequired();
            b.Property(o => o.CreatedAt).HasDefaultValueSql("now() at time zone 'utc'");
            b.HasOne(o => o.Owner)
                .WithMany()
                .HasForeignKey(o => o.OwnerId)
                .OnDelete(DeleteBehavior.Restrict); // can't drop a user that owns an org
        });

        // ── org_members ─────────────────────────────────────────────────────
        builder.Entity<OrgMember>(b =>
        {
            b.ToTable("org_members");
            b.HasKey(m => m.Id);
            b.Property(m => m.Role).HasMaxLength(16).IsRequired();
            b.Property(m => m.CreatedAt).HasDefaultValueSql("now() at time zone 'utc'");
            b.HasIndex(m => new { m.OrgId, m.UserId }).IsUnique();
            b.HasIndex(m => m.UserId);
            b.HasOne(m => m.Org)
                .WithMany(o => o.Members)
                .HasForeignKey(m => m.OrgId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasOne(m => m.User)
                .WithMany()
                .HasForeignKey(m => m.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ── external_logins ─────────────────────────────────────────────────
        builder.Entity<ExternalLogin>(b =>
        {
            b.ToTable("external_logins");
            b.HasKey(e => e.Id);
            b.Property(e => e.Provider).HasMaxLength(32).IsRequired();
            b.Property(e => e.ProviderUserId).HasMaxLength(128).IsRequired();
            b.Property(e => e.Username).HasMaxLength(128).IsRequired();
            b.Property(e => e.LinkedAt).HasDefaultValueSql("now() at time zone 'utc'");
            b.HasIndex(e => new { e.Provider, e.ProviderUserId }).IsUnique();
            b.HasIndex(e => e.UserId);
            b.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ── jobs (V0 anonymous flow; absorbed from Sprint 1 SQL bootstrap). ─
        // Schema mirrors Persistence/Schema/001_initial.sql byte-for-byte so
        // PostgresJobStore's hand-written SQL keeps working unmodified. Only
        // change: managed by EF migration now (ADR 0007 §3.4 §C-pendência 1).
        builder.Entity<Job>(b =>
        {
            b.ToTable("jobs");
            b.HasKey(j => j.Id);
            b.Property(j => j.Id).HasMaxLength(64);            // ULID — 26 chars but we leave headroom
            b.Property(j => j.GithubUrl).IsRequired();
            b.Property(j => j.Status).IsRequired();
            b.Property(j => j.CreatedAt).IsRequired();
            b.HasIndex(j => new { j.Status, j.CreatedAt });
        });

        // ── rate_limits ─────────────────────────────────────────────────────
        builder.Entity<RateLimit>(b =>
        {
            b.ToTable("rate_limits");
            b.HasKey(r => new { r.Ip, r.Day });
            b.Property(r => r.Count).IsRequired();
        });

        // ── repos (ADR 0007 §3.2 + Apêndice E §E.8) ─────────────────────────
        // GitHub repo registered to a Lintty org. Soft delete via deleted_at
        // because scans FK to repos and we don't want to lose history when a
        // user removes a repo from the dashboard.
        builder.Entity<Repo>(b =>
        {
            b.ToTable("repos");
            b.HasKey(r => r.Id);
            b.Property(r => r.GithubUrl).IsRequired();
            b.Property(r => r.IsPrivate).HasDefaultValue(false);
            b.Property(r => r.CreatedAt).HasDefaultValueSql("now() at time zone 'utc'");

            // Sprint 3 PR S1 — user-curated scan target. Nullable text[] so a
            // repo without a saved selection (the default) keeps its current
            // auto-detect behaviour. Npgsql maps string[] to text[] natively.
            b.Property(r => r.ScanProjects).HasColumnType("text[]");

            b.HasOne(r => r.Org)
                .WithMany()
                .HasForeignKey(r => r.OrgId)
                .OnDelete(DeleteBehavior.Cascade);

            // RESTRICT: a user who added repos cannot be hard-deleted while
            // those rows exist. Apêndice E §E.9 contract — added_by_user_id
            // is the GitHub-token owner for private clone; orphaning it would
            // break the separation-of-identity invariant.
            b.HasOne(r => r.AddedByUser)
                .WithMany()
                .HasForeignKey(r => r.AddedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            // Partial unique: same org cannot register the same URL twice
            // *while it is active*. Re-adding a previously soft-deleted repo
            // is allowed — it gets a new row.
            b.HasIndex(r => new { r.OrgId, r.GithubUrl })
                .IsUnique()
                .HasDatabaseName("uq_repos_org_url_active")
                .HasFilter("deleted_at IS NULL");

            // Lookup by GitHub repo id during Org-import dedup. Sparse —
            // manual adds leave it null.
            b.HasIndex(r => r.GithubRepoId)
                .HasDatabaseName("ix_repos_github_repo_id")
                .HasFilter("github_repo_id IS NOT NULL");
        });

        // ── scans (ADR 0007 §3.7) ───────────────────────────────────────────
        // Multi-tenant scan record. Coexists with the V0 anonymous `jobs`
        // table — see ADR §3.4 + Apêndice E redesign. Worker drains both
        // queues in PR 4; this PR only lands the schema.
        builder.Entity<Scan>(b =>
        {
            b.ToTable("scans", t =>
            {
                // CHECK constraint matches ScanStatus.* constants verbatim.
                // Mirrors the OrgRole pattern (string column with app-side
                // validation) and adds DB-level enforcement because scans is
                // the determinism gate for Apêndice §3.7 — wrong status
                // would let the worker pick up a malformed row.
                t.HasCheckConstraint(
                    "ck_scans_status",
                    "status IN ('queued','running','completed','failed','cancelled')");
            });
            b.HasKey(s => s.Id);

            b.Property(s => s.PublicId)
                .IsRequired()
                .HasDefaultValueSql("gen_random_uuid()");

            b.Property(s => s.CanonVersion).IsRequired().HasMaxLength(16);
            b.Property(s => s.Status).IsRequired().HasMaxLength(16);
            b.Property(s => s.QueuedAt).HasDefaultValueSql("now() at time zone 'utc'");

            b.HasOne(s => s.Org)
                .WithMany()
                .HasForeignKey(s => s.OrgId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasOne(s => s.Repo)
                .WithMany()
                .HasForeignKey(s => s.RepoId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasOne(s => s.TriggeredByUser)
                .WithMany()
                .HasForeignKey(s => s.TriggeredByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            // public_id is the URL-facing identifier — must be unique on the
            // entire scans table.
            b.HasIndex(s => s.PublicId)
                .IsUnique()
                .HasDatabaseName("uq_scans_public_id");

            // Tenant history list: paginated by descending queue time within
            // a (org, repo). Composite index avoids a sort on the hot path.
            b.HasIndex(s => new { s.OrgId, s.RepoId, s.QueuedAt })
                .HasDatabaseName("ix_scans_org_repo_queued_at");

            // Worker queue scan: only queued/running rows are interesting.
            // Partial index keeps the index tiny (most rows are completed).
            b.HasIndex(s => s.Status)
                .HasDatabaseName("ix_scans_status_active")
                .HasFilter("status IN ('queued','running')");
        });

        // ── github_user_tokens (Apêndice E §E.6) ────────────────────────────
        // Per-user OAuth token from the explicit Connect GitHub flow. PK is
        // user_id — 1:1 with users. Encrypted at rest via IDataProtector
        // (wired in PR 6); this PR only lands the bytea column.
        builder.Entity<GithubUserToken>(b =>
        {
            b.ToTable("github_user_tokens");
            b.HasKey(t => t.UserId);
            b.Property(t => t.UserId).ValueGeneratedNever();   // FK is the PK; no identity gen

            b.Property(t => t.EncryptedToken).IsRequired();
            // text[] — Npgsql maps string[] to Postgres text[] natively.
            b.Property(t => t.Scopes).IsRequired().HasColumnType("text[]");
            b.Property(t => t.GrantedAt).HasDefaultValueSql("now() at time zone 'utc'");

            b.HasOne(t => t.User)
                .WithOne()
                .HasForeignKey<GithubUserToken>(t => t.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // Active-token lookup. Most rows will be active; the partial
            // filter is a small optimization for the revoked path.
            b.HasIndex(t => t.UserId)
                .HasDatabaseName("ix_github_user_tokens_active")
                .HasFilter("revoked_at IS NULL");
        });

        // ── github_orgs (Apêndice E §E.7) ───────────────────────────────────
        // Cache of GitHub orgs the user has authorized. user-level binding,
        // not Lintty-org-level — see entity doc. Refreshed by IGitHubOrgsClient
        // in PR 6; PR 1 is schema only.
        builder.Entity<GithubOrg>(b =>
        {
            b.ToTable("github_orgs");
            b.HasKey(o => o.Id);
            b.Property(o => o.GithubOrgLogin).IsRequired();
            b.Property(o => o.ConnectedAt).HasDefaultValueSql("now() at time zone 'utc'");

            b.HasOne(o => o.User)
                .WithMany()
                .HasForeignKey(o => o.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // A user cannot have two rows for the same GitHub org. Re-connect
            // upserts on this key (last wins).
            b.HasIndex(o => new { o.UserId, o.GithubOrgId })
                .IsUnique()
                .HasDatabaseName("uq_github_orgs_user_github_org");
        });
    }
}
