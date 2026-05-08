using System;
using System.Threading;
using System.Threading.Tasks;

namespace Lintty.WebInspector.Auth;

/// <summary>
/// Stores the per-user GitHub OAuth access token granted via the explicit
/// "Connect GitHub" flow (Apêndice E §E.5/§E.6 of ADR 0007). The plaintext
/// token is encrypted via <c>IDataProtector</c> with purpose
/// <c>"github-user-token"</c> before being persisted to <c>github_user_tokens</c>.
///
/// **One row per user**. The connect flow upserts on <c>user_id</c> — a second
/// connect from the same user (e.g. two browser tabs racing, or an explicit
/// re-grant after revoking) is "last connect wins" by design (pendência E5
/// of Apêndice E). The PK is <c>user_id</c> itself, so the database enforces
/// the 1:1 invariant; concurrent inserts collapse to a single ON CONFLICT
/// path with no orphan rows.
///
/// **Plaintext never logs and never returns over the wire.**
/// <see cref="GetActiveTokenAsync"/> is the only path that returns a
/// decrypted token, and it's intended exclusively for callers that need to
/// authenticate to GitHub (e.g. <c>GitCliClient</c> when cloning a private
/// repo). The status endpoint (<c>GET /api/auth/github/connect</c>) reads
/// metadata via <see cref="GetSnapshotAsync"/>, which never decrypts.
/// </summary>
public interface IGitHubUserTokenStore
{
    /// <summary>
    /// UPSERT for the user's GitHub OAuth token. Encrypts <paramref name="accessToken"/>
    /// via <c>IDataProtector</c> and writes the resulting <c>bytea</c> +
    /// <paramref name="scopes"/> to <c>github_user_tokens</c>.
    ///
    /// **Side effect on conflict:** when a row already exists for
    /// <paramref name="userId"/> the call rewrites <c>encrypted_token</c>,
    /// <c>scopes</c>, <c>granted_at = now()</c>, clears <c>last_used_at</c>,
    /// and clears <c>revoked_at</c>. Last connect wins (Apêndice E §E.13 / E5).
    /// A user that re-connects after revoking gets a fresh active row without
    /// needing a separate "un-revoke" call.
    /// </summary>
    Task SaveAsync(long userId, string accessToken, string[] scopes, CancellationToken ct);

    /// <summary>
    /// Reads metadata (scopes, granted_at, last_used_at, revoked_at) without
    /// decrypting the token. Used by the privacy console
    /// (<c>GET /api/auth/github/connect</c>) to render "Connected since X,
    /// last used Y" without giving the endpoint access to the secret.
    /// Returns <c>null</c> when no row exists for the user.
    /// </summary>
    Task<GithubUserTokenSnapshot?> GetSnapshotAsync(long userId, CancellationToken ct);

    /// <summary>
    /// Returns the decrypted access token for <paramref name="userId"/> if a
    /// row exists AND <c>revoked_at IS NULL</c>; otherwise returns <c>null</c>.
    /// **Side effect:** on hit, updates <c>last_used_at = now()</c> in the same
    /// transaction so a future TTL sweep (Apêndice E §E.13 / E2) can reason
    /// about idle tokens without extra plumbing.
    ///
    /// Callers (e.g. <c>GitCliClient</c>) MUST treat <c>null</c> as
    /// "user must reconnect" and surface a structured error
    /// (<c>GITHUB_TOKEN_REVOKED</c> in scan flow), not crash.
    /// </summary>
    Task<string?> GetActiveTokenAsync(long userId, CancellationToken ct);

    /// <summary>
    /// Marks the user's token as soft-revoked (<c>revoked_at = now()</c>).
    /// Returns <c>true</c> when the call changed state (row existed and was
    /// active), <c>false</c> when the row was missing or already revoked
    /// (idempotent). **Does not** call GitHub's revocation endpoint —
    /// per ADR 0007 founder decision #3 the user is told to revoke on
    /// github.com/settings/applications if they want full external revocation.
    /// </summary>
    Task<bool> RevokeAsync(long userId, CancellationToken ct);
}

/// <summary>
/// Metadata projection of <c>github_user_tokens</c> safe to expose over the
/// wire. Note the absence of any token bytes — the snapshot is the
/// deliberately-narrow surface the privacy console renders from.
/// </summary>
public sealed record GithubUserTokenSnapshot(
    long UserId,
    string[] Scopes,
    DateTime GrantedAt,
    DateTime? LastUsedAt,
    DateTime? RevokedAt);
