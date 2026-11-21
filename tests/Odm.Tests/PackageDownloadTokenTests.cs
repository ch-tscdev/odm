using Microsoft.Extensions.Options;
using Odm.Modules.Software.Application;

namespace Odm.Tests;

public class PackageDownloadTokenTests
{
    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static readonly DateTimeOffset Start = new(2026, 11, 21, 10, 0, 0, TimeSpan.Zero);

    private static PackageDownloadTokens Tokens(Clock clock, byte fill = 7) =>
        new(Options.Create(new SecretsOptions { MasterKey = Convert.ToBase64String(Enumerable.Repeat(fill, 32).ToArray()) }), clock);

    [Fact]
    public void Token_is_valid_for_its_version_until_it_expires()
    {
        var clock = new Clock(Start);
        var tokens = Tokens(clock);
        var version = Guid.NewGuid();
        var (token, expiresAt) = tokens.Create(version);

        Assert.Equal(Start + PackageDownloadTokens.Lifetime, expiresAt);
        Assert.True(tokens.IsValid(token, version));

        clock.Now = expiresAt;
        Assert.True(tokens.IsValid(token, version));

        clock.Now = expiresAt.AddSeconds(1);
        Assert.False(tokens.IsValid(token, version));
    }

    [Fact]
    public void Token_does_not_open_another_version()
    {
        var tokens = Tokens(new Clock(Start));
        var (token, _) = tokens.Create(Guid.NewGuid());

        Assert.False(tokens.IsValid(token, Guid.NewGuid()));
    }

    [Fact]
    public void Tampered_foreign_or_malformed_tokens_are_rejected()
    {
        var clock = new Clock(Start);
        var version = Guid.NewGuid();
        var (token, _) = Tokens(clock).Create(version);

        // Another server key
        Assert.False(Tokens(clock, fill: 8).IsValid(token, version));

        // Changed character (e.g. a later expiry)
        var chars = token.ToCharArray();
        chars[30] = chars[30] == 'A' ? 'B' : 'A';
        Assert.False(Tokens(clock).IsValid(new string(chars), version));

        Assert.False(Tokens(clock).IsValid(null, version));
        Assert.False(Tokens(clock).IsValid("", version));
        Assert.False(Tokens(clock).IsValid("not base64 !", version));
        Assert.False(Tokens(clock).IsValid(token[..20], version));
        Assert.False(Tokens(clock).IsValid(new string('A', 500), version));
    }

    [Theory]
    [InlineData("7-Zip", "24.09", "7-Zip-24.09.zip")]
    [InlineData("Adobe Reader DC", "1.0+build 5", "Adobe_Reader_DC-1.0_build_5.zip")]
    [InlineData("..\\..\\evil/name", "1", "evil_name-1.zip")]
    [InlineData("Büro \"Paket\"", "2", "B_ro_Paket_-2.zip")]
    public void File_name_is_safe(string name, string version, string expected)
    {
        Assert.Equal(expected, PackageDownloadTokens.FileName(name, version));
    }
}
