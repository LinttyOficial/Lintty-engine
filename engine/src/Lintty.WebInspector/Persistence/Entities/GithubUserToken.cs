using System;

namespace Lintty.WebInspector.Persistence.Entities;

/// <summary>
/// Per-user GitHub OAuth token granted via the explicit "Connect GitHub" flow
/// (Apêndice E §E.5/§E.6). Distinct from the login OAuth flow which uses
/// minimal scopes and discards its token (§3.6 original).
///
/// **1:1 with <see cref="User"/>** — the table's PK is <see cref="UserId"/>
/// itself. The connect flow is per-user, and a second connect from the same
/// user upserts (last connect wins; see pendência E5). Migrating to multi-row
/// (e.g. one row per scope set) is not anticipated for V1.0.
///
/// PR 1 lands the schema only. <c>IGitHubUserTokenStore</c>,
/// <c>IDataProtector</c> wiring, and the connect endpoints land in PR 6.
///
/// **<see cref="EncryptedToken"/>** is the raw output of
/// <c>IDataProtector.Protect(Encoding.UTF8.GetBytes(plainToken))</c> with
/// purpose <c>"github-user-token"</c>. The plaintext token never touches the
/// DB; the column is <c>bytea</c> (binary) — not a base64 <c>text</c> column —
/// because <c>Protect</c> already returns binary and base64 round-tripping is
/// pure overhead.
///
/// <see cref="LastUsedAt"/> exists today (no field added later) so adding a
/// TTL sweep in V1.1 doesn't need a migration. <see cref="RevokedAt"/> is the
/// soft-revoke marker — a non-null value means
/// <c>IGitHubUserTokenStore.GetForUserAsync</c> returns <c>null</c> regardless
/// of <see cref="EncryptedToken"/>.
/// </summary>
public sealed class GithubUserToken
{
    /// <summary>FK + PK. 1:1 relation with <see cref="User"/>; cascade delete
    /// when the user is removed.</summary>
    public long UserId { get; set; }
    public User? User { get; set; }

    /// <summary>Output of <c>IDataProtector.Protect</c>. Never logged, never
    /// returned over the wire.</summary>
    public byte[] EncryptedToken { get; set; } = Array.Empty<byte>();

    /// <summary>Scopes the token was granted (e.g. <c>["repo","read:org"]</c>).
    /// Stored as a Postgres native <c>text[]</c> (mapped by
    /// <c>Npgsql.EntityFrameworkCore.PostgreSQL</c> from <see cref="string"/>[]).
    /// Used by V1.1 audits; V1.0 only reads it for the privacy console
    /// (<c>GET /api/auth/github/connect</c>).</summary>
    public string[] Scopes { get; set; } = Array.Empty<string>();

    public DateTime GrantedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Best-effort touch updated by the token store on every read.
    /// Powers a future TTL sweep without requiring another migration.</summary>
    public DateTime? LastUsedAt { get; set; }

    /// <summary>Soft-revoke. When set, the token is treated as revoked
    /// locally even if GitHub still considers it valid. The store overwrites
    /// this on a fresh connect (UPSERT path).</summary>
    public DateTime? RevokedAt { get; set; }
}
