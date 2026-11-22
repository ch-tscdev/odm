using Odm.Agent.Os;

namespace Odm.Tests;

public class LinuxUpdaterArgumentTests
{
    [Fact]
    public void Simulation_includes_updates_that_need_new_packages()
    {
        var args = LinuxUpdater.AptSimulationArguments();

        Assert.Equal("-s", args[0]);
        Assert.Contains("upgrade", args);
        Assert.Contains("--with-new-pkgs", args);
        Assert.Contains("APT::Get::Always-Include-Phased-Updates=true", args);
    }

    [Fact]
    public void All_updates_install_new_packages_but_never_remove_any()
    {
        var args = LinuxUpdater.AptUpgradeArguments(securityOnly: false, []);

        Assert.Equal(new[] { "upgrade", "--with-new-pkgs" }, args.TakeLast(2));
        Assert.DoesNotContain("dist-upgrade", args);
        Assert.DoesNotContain("full-upgrade", args);
        Assert.Contains("-y", args);
    }

    [Fact]
    public void Security_only_names_the_upgraded_packages()
    {
        var args = LinuxUpdater.AptUpgradeArguments(securityOnly: true, ["openssl", "linux-generic"]);

        Assert.Equal(new[] { "install", "--only-upgrade", "openssl", "linux-generic" }, args.TakeLast(4));
    }

    [Theory]
    [InlineData("Inst linux-generic-hwe-26.04 [7.0.0-43.43] (7.0.0-44.44 Ubuntu:26.04/resolute-updates [amd64])", true)]
    [InlineData("Inst linux-image-7.0.0-44-generic (7.0.0-44.44 Ubuntu:26.04/resolute-updates [amd64])", false)]
    public void Upgrades_and_new_packages_are_told_apart(string line, bool upgrade) =>
        Assert.Equal(upgrade, LinuxUpdater.IsUpgrade(line));
}
