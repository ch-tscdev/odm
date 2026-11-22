using Odm.Agent.Repo;
using Odm.Modules.Software.Application;
using Odm.Modules.Software.Domain;

namespace Odm.Tests;

public class AptEnvironmentTests
{
    private static readonly IReadOnlySet<string> Secrets = new HashSet<string> { "PROXY_PASSWORD" };

    private static PackageSpecDto Dto(string? environment) =>
        new("postfix", null, null, null, null, null, false, null, null, environment);

    private static (bool Ok, PackageSpec Spec, Dictionary<string, string[]> Errors) Validate(PackageKind kind, string? environment)
    {
        var ok = PackageSpecValidator.TryValidate(kind, Dto(environment), Secrets, out var spec, out var errors);
        return (ok, spec, errors);
    }

    [Fact]
    public void Apt_variables_are_normalized()
    {
        var (ok, spec, errors) = Validate(PackageKind.Apt, "\r\n  HTTP_PROXY = http://proxy:3128 \r\n\r\nAPP_MODE=server\n");

        Assert.True(ok, string.Join("; ", errors.SelectMany(e => e.Value)));
        Assert.Equal("HTTP_PROXY=http://proxy:3128\nAPP_MODE=server", spec.Environment);
    }

    [Fact]
    public void Empty_environment_leaves_the_json_unchanged()
    {
        var (ok, spec, _) = Validate(PackageKind.Apt, "  \n ");

        Assert.True(ok);
        Assert.Null(spec.Environment);
        Assert.DoesNotContain("environment", spec.ToJson(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Other_kinds_ignore_the_environment()
    {
        var (ok, spec, _) = Validate(PackageKind.Rpm, "A=1");

        Assert.True(ok);
        Assert.Null(spec.Environment);
    }

    [Theory]
    [InlineData("NOEQUALS")]
    [InlineData("=value")]
    [InlineData("1ABC=x")]
    [InlineData("A-B=x")]
    [InlineData("DEBIAN_FRONTEND=dialog")]
    [InlineData("debian_frontend=dialog")]
    [InlineData("LC_ALL=de_CH.UTF-8")]
    [InlineData("PATH=/tmp")]
    [InlineData("LD_PRELOAD=/tmp/x.so")]
    [InlineData("A=1\nA=2")]
    [InlineData("A={{secret:UNKNOWN}}")]
    public void Invalid_or_reserved_variables_are_rejected(string environment)
    {
        var (ok, _, errors) = Validate(PackageKind.Apt, environment);

        Assert.False(ok);
        Assert.True(errors.ContainsKey("environment"));
    }

    [Fact]
    public void Limits_apply()
    {
        var many = string.Join('\n', Enumerable.Range(0, PackageSpecValidator.MaxEnvironmentVariables + 1).Select(i => $"V{i}=1"));
        Assert.False(Validate(PackageKind.Apt, many).Ok);

        var long_ = "A=" + new string('x', PackageSpecValidator.MaxEnvironmentLength);
        Assert.False(Validate(PackageKind.Apt, long_).Ok);
    }

    [Fact]
    public void Known_secrets_are_allowed_and_count_as_used()
    {
        var (ok, spec, _) = Validate(PackageKind.Apt, "HTTP_PROXY=http://odm:{{secret:PROXY_PASSWORD}}@proxy:3128");

        Assert.True(ok);
        Assert.Contains("PROXY_PASSWORD", SecretReferences.Extract(spec.SecretText));
    }

    [Fact]
    public void Agent_parses_lines_and_keeps_equals_signs_in_values()
    {
        var parsed = ResolvedArguments.ParseEnvironment("A=1\nB=x=y\nbroken\n=nope");

        Assert.Equal(new Dictionary<string, string> { ["A"] = "1", ["B"] = "x=y" }, parsed);
    }

    [Fact]
    public void Fixed_apt_variables_win_over_own_ones()
    {
        var merged = RepoInstaller.AptEnvironmentWith(new Dictionary<string, string>
        {
            ["DEBIAN_FRONTEND"] = "dialog",
            ["APP_MODE"] = "server"
        });

        Assert.Equal("noninteractive", merged["DEBIAN_FRONTEND"]);
        Assert.Equal("a", merged["NEEDRESTART_MODE"]);
        Assert.Equal("C", merged["LC_ALL"]);
        Assert.Equal("server", merged["APP_MODE"]);
    }
}
