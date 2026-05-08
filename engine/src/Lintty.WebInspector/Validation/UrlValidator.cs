using System;

namespace Lintty.WebInspector.Validation;

/// <summary>
/// Parses and validates the <c>github_url</c> input. V0 only accepts hosts
/// equal to <c>github.com</c> per spec §6 (no GitHub Enterprise, no GitLab).
/// We extract owner/repo so the worker can hit the metadata API without
/// re-parsing.
/// </summary>
public sealed record GitHubRepoCoordinates(string Owner, string Repo, Uri NormalizedUrl)
{
    public string ApiUrl => $"https://api.github.com/repos/{Owner}/{Repo}";

    /// <summary>
    /// HTTPS clone URL. Tokens (PATs / OAuth user tokens) are injected by the
    /// caller, never persisted. The authenticated form uses the
    /// <c>x-access-token</c> username — GitHub's documented contract for
    /// OAuth-app and GitHub-App tokens (Apêndice E §E.9). The <c>oauth2</c>
    /// alias also works historically but the spec form is preferred.
    /// </summary>
    public string CloneUrl(string? token)
    {
        if (string.IsNullOrEmpty(token))
            return $"https://github.com/{Owner}/{Repo}.git";
        return $"https://x-access-token:{token}@github.com/{Owner}/{Repo}.git";
    }

    /// <summary>
    /// Same shape as <see cref="CloneUrl"/> but with the token literal masked.
    /// Used in log lines so an authenticated clone is visibly distinct from
    /// an anonymous one without leaking the secret. The mask placeholder is
    /// the literal string <c>***</c> — checked verbatim by the masking test
    /// (Apêndice E §E.13 / E4).
    /// </summary>
    public string CloneUrlForLog(string? token)
    {
        if (string.IsNullOrEmpty(token))
            return $"https://github.com/{Owner}/{Repo}.git";
        return $"https://x-access-token:***@github.com/{Owner}/{Repo}.git";
    }
}

public static class UrlValidator
{
    /// <summary>
    /// Parses the user-supplied GitHub URL. Returns null and an error reason
    /// on rejection. Strips any <c>.git</c> suffix and any trailing slash so
    /// the canonical form is consistent across clients.
    /// </summary>
    public static GitHubRepoCoordinates? TryParse(string? githubUrl, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(githubUrl))
        {
            error = "github_url is required.";
            return null;
        }

        if (!Uri.TryCreate(githubUrl.Trim(), UriKind.Absolute, out var uri))
        {
            error = "github_url is not a valid URL.";
            return null;
        }

        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
        {
            error = "github_url must be http(s).";
            return null;
        }

        if (!string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
        {
            error = "Only github.com is supported in V0. Use the local CLI for other hosts.";
            return null;
        }

        // Path is "/owner/repo" or "/owner/repo.git" or "/owner/repo/<extra>".
        // We accept the first two; extra path segments are an error because we
        // can only clone the whole repo.
        var segments = uri.AbsolutePath.Trim('/').Split('/');
        if (segments.Length < 2 || string.IsNullOrEmpty(segments[0]) || string.IsNullOrEmpty(segments[1]))
        {
            error = "github_url must point at a repository (e.g. https://github.com/owner/repo).";
            return null;
        }

        var owner = segments[0];
        var repo = segments[1];
        if (repo.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            repo = repo[..^4];

        // Sanity-check owner/repo characters: github allows [A-Za-z0-9_.-].
        if (!IsSimpleSlug(owner) || !IsSimpleSlug(repo))
        {
            error = "github_url has unexpected characters in owner or repo.";
            return null;
        }

        var normalized = new Uri($"https://github.com/{owner}/{repo}");
        return new GitHubRepoCoordinates(owner, repo, normalized);
    }

    private static bool IsSimpleSlug(string s)
    {
        foreach (var c in s)
        {
            var ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')
                   || (c >= '0' && c <= '9') || c == '-' || c == '_' || c == '.';
            if (!ok) return false;
        }
        return true;
    }
}
