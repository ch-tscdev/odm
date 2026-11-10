using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Odm.Modules.Identity.Domain;

namespace Odm.Modules.Identity.Infrastructure;

/// <summary>
/// Authenticates API keys sent in the <c>X-Api-Key</c> header. The scheme is only used by policies that name it
/// (the Enrollment and DeviceDeletion policies); every other endpoint ignores API keys.
/// </summary>
public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "ApiKey";
    public const string HeaderName = "X-Api-Key";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HeaderName, out var values) || values.Count == 0)
        {
            return AuthenticateResult.NoResult();
        }

        if (!ApiKey.TryParse(values[0], out var id, out var secret))
        {
            return AuthenticateResult.Fail("Malformed API key.");
        }

        var db = Context.RequestServices.GetRequiredService<IdentityDbContext>();
        var key = await db.ApiKeys.SingleOrDefaultAsync(k => k.Id == id, Context.RequestAborted);
        var now = DateTimeOffset.UtcNow;
        if (key is null || !key.Matches(secret) || key.IsExpired(now))
        {
            return AuthenticateResult.Fail("Invalid or expired API key.");
        }

        if (key.Touch(now))
        {
            await db.SaveChangesAsync(Context.RequestAborted);
        }

        var claims = new List<Claim> { new("sub", key.Id.ToString()), new("name", $"api-key:{key.Name}") };
        claims.AddRange(key.Roles().Select(role => new Claim("role", role)));
        var identity = new ClaimsIdentity(claims, SchemeName, "name", "role");
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }
}
