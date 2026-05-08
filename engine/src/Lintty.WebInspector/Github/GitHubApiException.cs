using System;
using System.Net;

namespace Lintty.WebInspector.Github;

/// <summary>
/// Thrown by <see cref="IGitHubOrgsClient"/> implementations when GitHub
/// returns a 4xx other than 404 (which the contract maps to <c>null</c>).
/// Carries the raw <see cref="HttpStatusCode"/> so the calling endpoint can
/// fork between "token revoked / scope dropped" (401/403) and "GitHub
/// hiccup" (5xx) without having to grep the message string.
///
/// <para>
/// Distinct from <see cref="System.Net.Http.HttpRequestException"/>: that
/// type's <c>StatusCode</c> is nullable and lossy across SDK versions, and
/// callers shouldn't have to know about transport-layer details to decide
/// whether to surface "reconnect GitHub" vs "try again later". This
/// exception has a single, stable shape.
/// </para>
/// </summary>
public sealed class GitHubApiException : Exception
{
    public HttpStatusCode StatusCode { get; }

    public GitHubApiException(HttpStatusCode statusCode, string message)
        : base(message)
    {
        StatusCode = statusCode;
    }

    public GitHubApiException(HttpStatusCode statusCode, string message, Exception inner)
        : base(message, inner)
    {
        StatusCode = statusCode;
    }
}
