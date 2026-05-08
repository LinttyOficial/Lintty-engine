using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Lintty.WebInspector.Persistence;
using Lintty.WebInspector.Persistence.Entities;

namespace Lintty.WebInspector.Auth;

/// <summary>
/// EF + <c>IDataProtector</c>-backed implementation of
/// <see cref="IGitHubUserTokenStore"/>. The protector is created once with
/// the canonical purpose <c>"github-user-token"</c>; rotating the
/// DataProtection keys (V1.1 — Apêndice E §E.13 / E1) leaves the purpose
/// unchanged so existing ciphertext stays decryptable.
///
/// **Why DI a single protector and not <c>IDataProtectionProvider</c>?**
/// <c>CreateProtector(purpose)</c> is a pure function of the provider + the
/// purpose string, so caching the result is safe and avoids re-allocating
/// per call. Tests inject a stub protector through the same DI by
/// registering <see cref="IDataProtectionProvider"/> directly.
/// </summary>
public sealed class GithubUserTokenStore : IGitHubUserTokenStore
{
    /// <summary>
    /// Canonical purpose for the protector. **Do not change** without a
    /// migration plan — every existing ciphertext was sealed with this
    /// string and changing it would invalidate every persisted token.
    /// </summary>
    public const string ProtectorPurpose = "github-user-token";

    private readonly LinttyDbContext _db;
    private readonly IDataProtector _protector;
    private readonly ILogger<GithubUserTokenStore> _log;

    public GithubUserTokenStore(
        LinttyDbContext db,
        IDataProtectionProvider provider,
        ILogger<GithubUserTokenStore> log)
    {
        _db = db;
        _protector = provider.CreateProtector(ProtectorPurpose);
        _log = log;
    }

    public async Task SaveAsync(long userId, string accessToken, string[] scopes, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(accessToken))
            throw new ArgumentException("accessToken must not be empty", nameof(accessToken));
        scopes ??= Array.Empty<string>();

        var encrypted = _protector.Protect(Encoding.UTF8.GetBytes(accessToken));

        // EF doesn't have a native UPSERT on a 1:1 PK — emulate it by Find +
        // Update or Add. The PK is user_id, so the lookup is a single index
        // probe. Two simultaneous SaveAsync calls for the same user race on
        // the SaveChanges; the loser hits a PK violation and we retry as an
        // update (last connect wins, Apêndice E §E.13 / E5).
        var existing = await _db.GithubUserTokens
            .FirstOrDefaultAsync(t => t.UserId == userId, ct)
            .ConfigureAwait(false);

        if (existing is null)
        {
            try
            {
                _db.GithubUserTokens.Add(new GithubUserToken
                {
                    UserId = userId,
                    EncryptedToken = encrypted,
                    Scopes = scopes,
                    GrantedAt = DateTime.UtcNow,
                    LastUsedAt = null,
                    RevokedAt = null,
                });
                await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            }
            catch (DbUpdateException)
            {
                // Race — another tab won. Re-read and switch to UPDATE path.
                _db.ChangeTracker.Clear();
                existing = await _db.GithubUserTokens
                    .FirstAsync(t => t.UserId == userId, ct)
                    .ConfigureAwait(false);
                ApplyUpdate(existing, encrypted, scopes);
                await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            }
        }
        else
        {
            ApplyUpdate(existing, encrypted, scopes);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        _log.LogInformation(
            "GitHub token saved: user_id={UserId} scopes=[{Scopes}]",
            userId, string.Join(",", scopes));
    }

    private static void ApplyUpdate(GithubUserToken row, byte[] encrypted, string[] scopes)
    {
        row.EncryptedToken = encrypted;
        row.Scopes = scopes;
        row.GrantedAt = DateTime.UtcNow;
        row.LastUsedAt = null;
        row.RevokedAt = null;
    }

    public async Task<GithubUserTokenSnapshot?> GetSnapshotAsync(long userId, CancellationToken ct)
    {
        var row = await _db.GithubUserTokens
            .AsNoTracking()
            .Where(t => t.UserId == userId)
            .Select(t => new GithubUserTokenSnapshot(
                t.UserId,
                t.Scopes,
                t.GrantedAt,
                t.LastUsedAt,
                t.RevokedAt))
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        return row;
    }

    public async Task<string?> GetActiveTokenAsync(long userId, CancellationToken ct)
    {
        var row = await _db.GithubUserTokens
            .FirstOrDefaultAsync(t => t.UserId == userId, ct)
            .ConfigureAwait(false);
        if (row is null || row.RevokedAt is not null) return null;

        string plaintext;
        try
        {
            plaintext = Encoding.UTF8.GetString(_protector.Unprotect(row.EncryptedToken));
        }
        catch (System.Security.Cryptography.CryptographicException ex)
        {
            // Key ring rotated and we lost the original key — pendência E1.
            // Treat as revoked so the caller surfaces a "please reconnect"
            // error rather than a 500. Don't log the row contents.
            _log.LogWarning(ex, "DataProtection unprotect failed for user_id={UserId}; treating as revoked.", userId);
            return null;
        }

        // Touch last_used_at — same transaction so we don't surface a token
        // we never recorded a read for.
        row.LastUsedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return plaintext;
    }

    public async Task<bool> RevokeAsync(long userId, CancellationToken ct)
    {
        var row = await _db.GithubUserTokens
            .FirstOrDefaultAsync(t => t.UserId == userId, ct)
            .ConfigureAwait(false);
        if (row is null || row.RevokedAt is not null) return false;

        row.RevokedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        _log.LogInformation("GitHub token revoked: user_id={UserId}", userId);
        return true;
    }
}
