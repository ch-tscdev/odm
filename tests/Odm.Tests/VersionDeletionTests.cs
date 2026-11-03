using Odm.Modules.Software.Application;
using Xunit;

namespace Odm.Tests;

public class VersionDeletionTests
{
    [Fact]
    public void Unused_old_version_may_be_deleted() =>
        Assert.Null(VersionDeletionRules.BlockReason(isNewest: false, isPinned: false, inUse: false, installedCount: 0));

    [Fact]
    public void Newest_version_is_kept() =>
        Assert.Equal(VersionDeletionRules.Newest, VersionDeletionRules.BlockReason(true, false, false, 0));

    [Fact]
    public void Pinned_version_is_kept() =>
        Assert.Equal(VersionDeletionRules.Pinned, VersionDeletionRules.BlockReason(false, true, false, 0));

    [Fact]
    public void Version_of_a_running_installation_is_kept() =>
        Assert.Equal(VersionDeletionRules.InUse, VersionDeletionRules.BlockReason(false, false, true, 0));

    [Fact]
    public void Installed_version_is_kept_for_uninstall()
    {
        var reason = VersionDeletionRules.BlockReason(false, false, false, 3);
        Assert.Equal("The version is installed on 3 device(s) and is needed to uninstall it there.", reason);
    }
}
