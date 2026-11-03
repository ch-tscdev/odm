namespace Odm.Modules.Software.Application;

/// <summary>
/// When a single PSADT version may be deleted (pure, testable). A version stays as long as anything still relies on its ZIP:
/// the newest version is the target of "latest" assignments, a pinned version is a fixed target, and an installed version is
/// needed to uninstall the package from the devices that have it.
/// </summary>
public static class VersionDeletionRules
{
    public const string Newest = "The newest version cannot be deleted. Upload a newer version first.";
    public const string Pinned = "The version is pinned by an assignment or a profile.";
    public const string InUse = "An installation with this version is pending or running.";
    public const string Installed = "The version is installed on {Count} device(s) and is needed to uninstall it there.";

    /// <summary>Returns why the version must be kept, or null if it may be deleted.</summary>
    public static string? BlockReason(bool isNewest, bool isPinned, bool inUse, int installedCount) =>
        isNewest ? Newest
        : isPinned ? Pinned
        : inUse ? InUse
        : installedCount > 0 ? Installed.Replace("{Count}", installedCount.ToString(), StringComparison.Ordinal)
        : null;
}
