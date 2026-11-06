using System.Security.Claims;
using Microsoft.Extensions.Caching.Memory;
using Odm.Modules.Audit.Infrastructure;
using Odm.Modules.Identity.Domain;
using Odm.Modules.Identity.Infrastructure;
using Xunit;

namespace Odm.Tests;

public class SessionSecurityTests
{
    private sealed class ManualClock(DateTimeOffset start) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = start;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static User NewUser() => User.Create("anna", "Anna", "hash", UserRole.Operator);

    [Fact]
    public void Stamp_changes_with_role_active_state_and_password_but_not_with_the_display_name()
    {
        var user = NewUser();
        var stamp = user.SecurityStamp;
        Assert.Equal(32, stamp.Length);

        user.Update("Anna Muster", UserRole.Operator, true);
        Assert.Equal(stamp, user.SecurityStamp);

        user.Update("Anna Muster", UserRole.ReadOnly, true);
        Assert.NotEqual(stamp, user.SecurityStamp);

        stamp = user.SecurityStamp;
        user.Update("Anna Muster", UserRole.ReadOnly, false);
        Assert.NotEqual(stamp, user.SecurityStamp);

        stamp = user.SecurityStamp;
        user.SetPasswordHash("other-hash");
        Assert.NotEqual(stamp, user.SecurityStamp);
    }

    private static ClaimsPrincipal Principal(params Claim[] claims) => new(new ClaimsIdentity(claims, "test"));

    [Fact]
    public async Task Directory_users_are_not_checked_against_the_database()
    {
        var validator = new SessionValidator(null!, new MemoryCache(new MemoryCacheOptions()));
        Assert.True(await validator.IsValidAsync(
            Principal(new Claim("sub", Guid.NewGuid().ToString()), new Claim("idp", "ldap")), CancellationToken.None));
        Assert.True(await validator.IsValidAsync(
            Principal(new Claim("sub", Guid.NewGuid().ToString()), new Claim("idp", "oidc:entra")), CancellationToken.None));
    }

    [Fact]
    public async Task Local_tokens_without_stamp_are_rejected()
    {
        var validator = new SessionValidator(null!, new MemoryCache(new MemoryCacheOptions()));
        Assert.False(await validator.IsValidAsync(
            Principal(new Claim("sub", Guid.NewGuid().ToString())), CancellationToken.None));
        Assert.False(await validator.IsValidAsync(
            Principal(new Claim("sub", Guid.NewGuid().ToString()), new Claim("idp", "local")), CancellationToken.None));
    }

    [Fact]
    public void Account_is_locked_after_too_many_failures_and_released_after_the_window()
    {
        var clock = new ManualClock(DateTimeOffset.UtcNow);
        var throttle = new LoginThrottle(new MemoryCache(new MemoryCacheOptions()), clock);

        for (var i = 0; i < LoginThrottle.MaxFailures - 1; i++)
        {
            throttle.RecordFailure("Anna");
        }

        Assert.False(throttle.IsLocked("anna"));
        throttle.RecordFailure(" anna ");
        Assert.True(throttle.IsLocked("ANNA"));
        Assert.False(throttle.IsLocked("bert"));

        clock.Now += LoginThrottle.Window;
        Assert.False(throttle.IsLocked("anna"));
    }

    [Fact]
    public void Successful_sign_in_resets_the_counter()
    {
        var throttle = new LoginThrottle(new MemoryCache(new MemoryCacheOptions()), TimeProvider.System);
        for (var i = 0; i < LoginThrottle.MaxFailures; i++)
        {
            throttle.RecordFailure("anna");
        }

        throttle.Reset("anna");
        Assert.False(throttle.IsLocked("anna"));
    }

    [Fact]
    public void Audit_actor_shows_the_sign_in_method_of_directory_users()
    {
        Assert.Equal("admin", AuditLog.ActorOf(Principal(new Claim("name", "admin"), new Claim("idp", "local"))));
        Assert.Equal("ldap:admin", AuditLog.ActorOf(Principal(new Claim("name", "admin"), new Claim("idp", "ldap"))));
        Assert.Equal("oidc:entra:jdoe", AuditLog.ActorOf(Principal(new Claim("name", "jdoe"), new Claim("idp", "oidc:entra"))));
    }
}
