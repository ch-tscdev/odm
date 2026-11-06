using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace Odm.Modules.Identity.Infrastructure;

/// <summary>
/// Checks on every request that the token of a local user is still valid: the user exists, is active and its security
/// stamp matches the one in the token. Deactivating, demoting, deleting a user or changing the password therefore ends all
/// earlier sessions at once instead of when the token expires. LDAP and OIDC users have no server-side state; their tokens
/// stay valid until they expire (the directory or provider is checked again at the next sign-in).
/// </summary>
public sealed class SessionValidator(IServiceScopeFactory scopes, IMemoryCache cache)
{
    public const string SourceClaim = "idp";
    public const string StampClaim = "stamp";
    public const string LocalSource = "local";

    /// <summary>How long a looked-up stamp is reused; changes in this process clear it immediately.</summary>
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(30);

    private sealed record Entry(bool IsActive, string Stamp);

    public async Task<bool> IsValidAsync(ClaimsPrincipal principal, CancellationToken ct)
    {
        var source = principal.FindFirstValue(SourceClaim);
        if (source is not null && source != LocalSource)
        {
            return true;
        }

        // Local user (tokens from before the stamp existed have no source claim and are treated as local).
        if (!Guid.TryParse(principal.FindFirstValue("sub"), out var id) || principal.FindFirstValue(StampClaim) is not { } stamp)
        {
            return false;
        }

        var entry = await cache.GetOrCreateAsync(Key(id), async e =>
        {
            e.AbsoluteExpirationRelativeToNow = CacheFor;
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            return await db.Users.AsNoTracking()
                .Where(u => u.Id == id)
                .Select(u => new Entry(u.IsActive, u.SecurityStamp))
                .SingleOrDefaultAsync(ct);
        });

        return entry is { IsActive: true } && string.Equals(entry.Stamp, stamp, StringComparison.Ordinal);
    }

    /// <summary>Call after a user was changed or deleted so that the next request sees the new state.</summary>
    public void Forget(Guid userId) => cache.Remove(Key(userId));

    private static string Key(Guid id) => $"odm.session.{id}";
}
