using System;

namespace Lintty.WebInspector.Persistence.Entities;

/// <summary>
/// Maps a <see cref="User"/> to an external identity provider account (V1.0:
/// GitHub only). One user can have many; one (provider, provider_user_id)
/// pair maps to exactly one user (unique index).
///
/// We store this **separately** from Identity's built-in
/// <c>AspNetUserLogins</c> (which we keep — see <see cref="LinttyDbContext"/>)
/// because we need product-specific fields (<c>username</c>, <c>linked_at</c>)
/// and a stable bigserial PK to reference from logs/audit. The Identity table
/// is still updated by the OAuth callback so <see cref="Microsoft.AspNetCore.Identity.SignInManager{TUser}"/>
/// can look up the user by external login on subsequent calls.
/// </summary>
public sealed class ExternalLogin
{
    public long Id { get; set; }

    public long UserId { get; set; }
    public User? User { get; set; }

    /// <summary>Provider identifier, e.g. <c>"github"</c>. Lowercase.</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>
    /// Stable, unique-within-provider identifier. For GitHub this is the
    /// numeric user id from <c>GET /user</c> (string-typed because some
    /// providers use non-numeric ids).
    /// </summary>
    public string ProviderUserId { get; set; } = string.Empty;

    /// <summary>
    /// Display username at the provider. Cosmetic — used to render
    /// "@octocat" in the UI. May go stale if the user renames at GitHub.
    /// </summary>
    public string Username { get; set; } = string.Empty;

    public DateTime LinkedAt { get; set; } = DateTime.UtcNow;
}
