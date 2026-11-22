namespace Odm.Modules.Software.Application;

/// <summary>
/// The agent (from 0.9.2) appends the version that the package manager reports after a successful repo installation
/// as the last line of the job output: "ODM-Installed-Version: 24.09". The line is removed from the stored message.
/// </summary>
public static class InstalledVersionMarker
{
    public const string Prefix = "ODM-Installed-Version: ";

    public static (string? Output, string? Version) Extract(string? output)
    {
        if (string.IsNullOrEmpty(output))
        {
            return (output, null);
        }

        var trimmed = output.TrimEnd();
        var start = trimmed.LastIndexOf('\n') + 1;
        var last = trimmed[start..].Trim();
        if (!last.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return (output, null);
        }

        var version = last[Prefix.Length..].Trim();
        var rest = trimmed[..start].TrimEnd();
        return (rest, version.Length == 0 ? null : version);
    }
}
