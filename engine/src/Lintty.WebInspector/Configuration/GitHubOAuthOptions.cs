namespace Lintty.WebInspector.Configuration;

/// <summary>
/// OAuth GitHub credentials. ADR 0007 §3.6 / Sprint 2. Bound to the
/// <c>Github</c> section of <c>appsettings.json</c>; in practice supplied via
/// env vars <c>LINTTY_GITHUB__CLIENTID</c> and <c>LINTTY_GITHUB__CLIENTSECRET</c>.
///
/// **Both fields empty is a valid state** — the OAuth handler is not registered
/// in that case, and <c>GET /api/auth/github/start</c> emits a 503 with a
/// human-readable message rather than crashing. Local dev can run without a
/// configured GitHub OAuth app; only password signup/login is exercised.
/// </summary>
public sealed class GitHubOAuthOptions
{
    public const string SectionName = "Github";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
}
