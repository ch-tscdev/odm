using Odm.Modules.Identity.Infrastructure.Oidc;
using Odm.SharedKernel.Text;
using Xunit;

namespace Odm.Tests;

public class ConfigListTests
{
    [Fact]
    public void Comma_and_semicolon_separated_values_become_a_list()
    {
        Assert.Equal(new[] { "a@x.ch", "b@x.ch", "c@x.ch" }, ConfigList.Split(["a@x.ch, b@x.ch;c@x.ch"]));
        Assert.Equal(new[] { "a", "b" }, ConfigList.Split(["a", " b ", "a"]));
    }

    [Fact]
    public void Empty_values_from_unset_variables_are_dropped()
    {
        Assert.Empty(ConfigList.Split([""]));
        Assert.Empty(ConfigList.Split([" , ;"]));
        Assert.Empty(ConfigList.Split(null));
    }

    [Fact]
    public void Empty_oidc_provider_from_env_is_ignored()
    {
        var options = new OidcOptions { Providers = [new OidcProviderOptions { Scopes = [""], AdminValues = [""] }] };
        options.Normalize();

        Assert.Empty(options.Providers);
        Assert.Null(options.Problem());
    }

    [Fact]
    public void Oidc_role_values_accept_a_comma_separated_list()
    {
        var options = new OidcOptions
        {
            Providers = [new OidcProviderOptions { Id = "entra", AdminValues = ["odm-admin, odm-root"], Scopes = [""] }]
        };
        options.Normalize();

        Assert.Equal(new[] { "odm-admin", "odm-root" }, options.Providers[0].AdminValues);
        Assert.Empty(options.Providers[0].Scopes);
    }
}
