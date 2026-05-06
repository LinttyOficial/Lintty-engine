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

    protected override void OnModelCreating(ModelBuilder builder)
    {
        // Identity setup must run first — it configures the AspNet* tables and
        // their relationships. Our overrides come after.
        base.OnModelCreating(builder);

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
    }
}
