using System.Security.Claims;
using Odm.Modules.Agents.Application;
using Odm.Modules.Identity.Domain;
using Xunit;

namespace Odm.Tests;

public class ApiKeyTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Issued_key_round_trips_and_only_the_hash_is_stored()
    {
        var (key, plain) = ApiKey.Issue("ansible", "admin", Now, null);

        Assert.StartsWith(ApiKey.Prefix, plain);
        Assert.True(ApiKey.TryParse(plain, out var id, out var secret));
        Assert.Equal(key.Id, id);
        Assert.True(key.Matches(secret));
        Assert.DoesNotContain(secret, key.SecretHash);
        Assert.Null(key.ExpiresAt);
    }

    [Fact]
    public void Roles_follow_the_permissions()
    {
        Assert.Equal(new[] { ApiKey.EnrollmentRole }, ApiKey.Issue("a", "admin", Now, null).Key.Roles());
        Assert.Equal(new[] { ApiKey.DeviceDeletionRole },
            ApiKey.Issue("b", "admin", Now, null, ApiKeyPermissions.DeviceDeletion).Key.Roles());
        Assert.Equal(new[] { ApiKey.EnrollmentRole, ApiKey.DeviceDeletionRole },
            ApiKey.Issue("c", "admin", Now, null, ApiKeyPermissions.All).Key.Roles());
    }

    [Fact]
    public void Key_without_known_permission_cannot_be_issued()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ApiKey.Issue("a", "admin", Now, null, ApiKeyPermissions.None));
        Assert.Throws<ArgumentOutOfRangeException>(() => ApiKey.Issue("a", "admin", Now, null, (ApiKeyPermissions)8));
    }

    [Fact]
    public void Wrong_secret_does_not_match()
    {
        var (key, plain) = ApiKey.Issue("ansible", "admin", Now, null);
        var (_, other) = ApiKey.Issue("other", "admin", Now, null);
        Assert.True(ApiKey.TryParse(other, out _, out var otherSecret));
        Assert.False(key.Matches(otherSecret));
        Assert.True(ApiKey.TryParse(plain, out _, out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("odmk_")]
    [InlineData("Bearer abc")]
    [InlineData("odmk_00000000000000000000000000000000")]
    [InlineData("odmk_not-a-guid-not-a-guid-not-a-gu.0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef")]
    [InlineData("odmk_0123456789abcdef0123456789abcdef.tooshort")]
    public void Malformed_keys_are_rejected(string? presented) =>
        Assert.False(ApiKey.TryParse(presented, out _, out _));

    [Fact]
    public void Key_expires_after_its_validity()
    {
        var (key, _) = ApiKey.Issue("ansible", "admin", Now, TimeSpan.FromDays(30));
        Assert.False(key.IsExpired(Now.AddDays(29)));
        Assert.True(key.IsExpired(Now.AddDays(30)));
    }

    [Fact]
    public void Last_use_is_written_at_most_every_five_minutes()
    {
        var (key, _) = ApiKey.Issue("ansible", "admin", Now, null);
        Assert.True(key.Touch(Now));
        Assert.False(key.Touch(Now.AddMinutes(4)));
        Assert.True(key.Touch(Now.AddMinutes(6)));
        Assert.Equal(Now.AddMinutes(6), key.LastUsedAt);
    }
}

public class EnrollmentLimitsTests
{
    private static ClaimsPrincipal WithRole(string role) =>
        new(new ClaimsIdentity([new Claim("role", role)], "test", "name", "role"));

    [Fact]
    public void Limits_apply_to_api_keys_but_not_to_operators()
    {
        Assert.True(EnrollmentLimits.Applies(WithRole(ApiKey.EnrollmentRole)));
        Assert.True(EnrollmentLimits.Applies(WithRole(ApiKey.DeviceDeletionRole)));
        Assert.False(EnrollmentLimits.Applies(WithRole("Operator")));
        Assert.False(EnrollmentLimits.Applies(WithRole("Admin")));
    }

    [Theory]
    [InlineData(null, null, true)]
    [InlineData(1, 1, true)]
    [InlineData(24, 1, true)]
    [InlineData(25, 1, false)]
    [InlineData(1, 5, false)]
    public void Api_keys_only_get_short_one_time_tokens(int? hours, int? uses, bool allowed) =>
        Assert.Equal(allowed, EnrollmentLimits.Problem(new CreateTokenRequest("x", hours, uses)) is null);
}
