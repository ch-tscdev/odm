using Odm.Agent.Repo;
using Odm.Modules.Software.Application;
using Odm.Modules.Software.Domain;

namespace Odm.Tests;

public class InstalledVersionTests
{
    [Fact]
    public void Marker_round_trip_between_agent_and_server()
    {
        var output = InstalledVersion.AppendMarker("Setting up htop (3.4.1-1) ...\n", "3.4.1-1");
        var (message, version) = InstalledVersionMarker.Extract(output);

        Assert.Equal("Setting up htop (3.4.1-1) ...", message);
        Assert.Equal("3.4.1-1", version);
        Assert.Equal(InstalledVersion.MarkerPrefix, InstalledVersionMarker.Prefix);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("done")]
    [InlineData("ODM-Installed-Version: 1.0\nsomething after it")]
    public void Output_without_a_final_marker_is_kept(string? output)
    {
        var (message, version) = InstalledVersionMarker.Extract(output);

        Assert.Equal(output, message);
        Assert.Null(version);
    }

    [Fact]
    public void No_version_no_marker() =>
        Assert.Equal("ok", InstalledVersion.AppendMarker("ok", null));

    [Fact]
    public void Installation_keeps_the_reported_version_until_removal()
    {
        var now = DateTimeOffset.UtcNow;
        var installation = Installation.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now);
        installation.MarkQueued(Guid.NewGuid(), now);
        installation.Complete(true, "ok", now, "24.09");

        Assert.Equal("24.09", installation.ReportedVersion);
    }

    [Theory]
    [InlineData("1.1", null, "1.1")]
    [InlineData("latest (r5)", "{\"identifier\":\"htop\",\"classic\":false}", "latest")]
    [InlineData("24.09 (r2)", "{\"identifier\":\"7zip.7zip\",\"version\":\"24.09\",\"classic\":false}", "24.09")]
    public void Version_labels_hide_revisions(string stored, string? snapshot, string expected) =>
        Assert.Equal(expected, VersionLabels.Display(stored, snapshot));

    [Fact]
    public void Snap_list()
    {
        const string output = "Name         Version  Rev    Tracking       Publisher   Notes\nhello-world  6.4      29     latest/stable  canonical✓  -\n";
        Assert.Equal("6.4", InstalledVersion.ParseSnapList(output, "hello-world"));
        Assert.Null(InstalledVersion.ParseSnapList(output, "other"));
    }

    [Fact]
    public void Flatpak_info()
    {
        const string output = "\nCalculator - Perform arithmetic\n\n          ID: org.gnome.Calculator\n         Ref: app/org.gnome.Calculator/x86_64/stable\n     Version: 47.1\n      Branch: stable\n";
        Assert.Equal("47.1", InstalledVersion.ParseFlatpakInfo(output));
        Assert.Null(InstalledVersion.ParseFlatpakInfo("ID: x\nBranch: stable\n"));
    }

    [Fact]
    public void Winget_list_with_progress_output()
    {
        const string output =
            "   - \r   \\ \r\r" +
            "Name               Id        Version Available Source\n" +
            "-----------------------------------------------------\n" +
            "7-Zip 24.09 (x64)  7zip.7zip 24.09   25.01     winget\n";

        Assert.Equal("24.09", InstalledVersion.ParseWingetList(output, "7zip.7zip"));
        Assert.Null(InstalledVersion.ParseWingetList(output, "Other.App"));
        Assert.Null(InstalledVersion.ParseWingetList("No installed package found matching input criteria.", "7zip.7zip"));
    }

    [Fact]
    public void Chocolatey_nuspec()
    {
        const string xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://schemas.microsoft.com/packaging/2015/06/nuspec.xsd">
              <metadata><id>notepadplusplus</id><version>8.7.1</version></metadata>
            </package>
            """;
        Assert.Equal("8.7.1", InstalledVersion.NuspecVersion(xml));
    }
}
