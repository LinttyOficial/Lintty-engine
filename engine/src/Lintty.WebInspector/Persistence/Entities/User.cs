using System;
using Microsoft.AspNetCore.Identity;

namespace Lintty.WebInspector.Persistence.Entities;

/// <summary>
/// Application user. Inherits the AspNetCore Identity contract
/// (<see cref="IdentityUser{TKey}"/>) — Identity owns
/// <c>email</c>, <c>email_normalized</c>, <c>password_hash</c>,
/// <c>security_stamp</c>, lockout fields, etc. We add product fields here.
///
/// PK type is <c>long</c> per ADR 0007 §3.2: <c>bigserial</c> for domain
/// entities, <c>uuid</c> only where a public obscure identifier is required
/// (scans, invitations).
/// </summary>
public sealed class User : IdentityUser<long>
{
    /// <summary>
    /// Display name shown in the UI (header, mention strings). Optional —
    /// signup form may default it to the local-part of the email if empty.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Row creation timestamp. Identity does not track this by default;
    /// product needs it for the user-list audit story (V1.1).
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
