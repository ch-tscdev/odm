using System.Xml.Linq;
using Odm.Agent.Os;

namespace Odm.Agent.Repo;

/// <summary>
/// After a successful installation: which version does the package manager report as installed? Best effort: any
/// failure yields null and never fails the job. The server shows the result instead of its internal revision label.
/// </summary>
public static class InstalledVersion
{
    /// <summary>Last line of the job output; the server removes it from the message (counterpart there).</summary>
    public const string MarkerPrefix = "ODM-Installed-Version: ";

    private static readonly TimeSpan QueryTimeout = TimeSpan.FromMinutes(2);

    private static readonly IReadOnlyDictionary<string, string> CLocale = new Dictionary<string, string> { ["LC_ALL"] = "C" };

    public static string AppendMarker(string? output, string? version) =>
        string.IsNullOrWhiteSpace(version) ? output ?? "" : $"{output?.TrimEnd()}\n{MarkerPrefix}{version.Trim()}";

    public static async Task<string?> QueryAsync(InstallRepoPayload p, Func<string, IEnumerable<string>, TimeSpan, Task<ProcessRunner.Result>> exec, CancellationToken ct)
    {
        try
        {
            switch (p.Manager)
            {
                case "Apt" when ToolLocator.Linux("dpkg-query") is { } dpkg:
                {
                    var r = await ProcessRunner.RunAsync(dpkg, ["-W", "-f=${Version}", p.Identifier], QueryTimeout, ct, CLocale);
                    return r.ExitCode == 0 ? Clean(r.Output) : null;
                }
                case "Rpm" when ToolLocator.Linux("rpm") is { } rpm:
                {
                    var r = await ProcessRunner.RunAsync(rpm, ["-q", "--qf", "%{VERSION}-%{RELEASE}\n", p.Identifier], QueryTimeout, ct, CLocale);
                    return r.ExitCode == 0 ? LastLine(r.Output) : null;
                }
                case "Snap" when ToolLocator.Linux("snap") is { } snap:
                {
                    var r = await ProcessRunner.RunAsync(snap, ["list", p.Identifier], QueryTimeout, ct, CLocale);
                    return r.ExitCode == 0 ? ParseSnapList(r.Output, p.Identifier) : null;
                }
                case "Flatpak" when ToolLocator.Linux("flatpak") is { } flatpak:
                {
                    var scope = p.Scope == "user" ? "--user" : "--system";
                    var r = await exec(flatpak, ["info", scope, p.Identifier], QueryTimeout);
                    return r.ExitCode == 0 ? ParseFlatpakInfo(r.Output) : null;
                }
                case "Chocolatey" when ToolLocator.Chocolatey() is { } choco:
                    return ChocolateyNuspecVersion(choco, p.Identifier);
                case "Winget":
                {
                    var exe = RepoInstaller.RunsAsUser(p) ? "winget" : ToolLocator.Winget();
                    if (exe is null)
                    {
                        return null;
                    }

                    var r = await exec(exe, ["list", "--id", p.Identifier, "--exact", "--disable-interactivity", "--accept-source-agreements"], QueryTimeout);
                    return r.ExitCode == 0 ? ParseWingetList(r.Output, p.Identifier) : null;
                }
                default:
                    return null;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>"Name  Version  Rev  Tracking  Publisher  Notes" header, then one line per snap.</summary>
    public static string? ParseSnapList(string output, string name)
    {
        foreach (var line in Lines(output).Skip(1))
        {
            var columns = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (columns.Length >= 2 && string.Equals(columns[0], name, StringComparison.Ordinal))
            {
                return Clean(columns[1]);
            }
        }

        return null;
    }

    /// <summary>"Version: 46.1" line of "flatpak info" (not every app declares a version).</summary>
    public static string? ParseFlatpakInfo(string output) =>
        Lines(output)
            .Select(l => l.Trim())
            .Where(l => l.StartsWith("Version:", StringComparison.Ordinal))
            .Select(l => Clean(l["Version:".Length..]))
            .FirstOrDefault(v => v is not null);

    /// <summary>
    /// "winget list" prints a table: header ("Name  Id  Version  Available  Source"), a line of dashes, then rows.
    /// The version column starts where the third header word starts; progress output before the table is skipped.
    /// </summary>
    public static string? ParseWingetList(string output, string id)
    {
        var lines = Lines(output).ToList();
        var separator = lines.FindIndex(l => l.TrimStart().StartsWith("---", StringComparison.Ordinal));
        if (separator < 1)
        {
            return null;
        }

        var header = lines[separator - 1];
        var idStart = ColumnStarts(header).ElementAtOrDefault(1);
        var versionStart = ColumnStarts(header).ElementAtOrDefault(2);
        if (idStart <= 0 || versionStart <= idStart)
        {
            return null;
        }

        foreach (var row in lines.Skip(separator + 1))
        {
            if (row.Length <= versionStart || !row[idStart..].StartsWith(id, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var rest = row[versionStart..].TrimStart();
            var end = rest.IndexOf(' ');
            return Clean(end < 0 ? rest : rest[..end]);
        }

        return null;
    }

    /// <summary>Chocolatey keeps every installed package's manifest in lib\&lt;id&gt;\&lt;id&gt;.nuspec.</summary>
    public static string? ChocolateyNuspecVersion(string chocoExe, string id)
    {
        var root = Path.GetDirectoryName(Path.GetDirectoryName(chocoExe));
        if (root is null)
        {
            return null;
        }

        var lib = Path.Combine(root, "lib");
        var folder = Directory.Exists(lib)
            ? Directory.EnumerateDirectories(lib).FirstOrDefault(d => string.Equals(Path.GetFileName(d), id, StringComparison.OrdinalIgnoreCase))
            : null;
        var nuspec = folder is null ? null : Directory.EnumerateFiles(folder, "*.nuspec").FirstOrDefault();
        return nuspec is null ? null : NuspecVersion(File.ReadAllText(nuspec));
    }

    public static string? NuspecVersion(string xml) =>
        Clean(XDocument.Parse(xml).Descendants().FirstOrDefault(e => e.Name.LocalName == "version")?.Value);

    private static IEnumerable<int> ColumnStarts(string header)
    {
        for (var i = 0; i < header.Length; i++)
        {
            if (header[i] != ' ' && (i == 0 || header[i - 1] == ' '))
            {
                yield return i;
            }
        }
    }

    // Progress output of winget uses carriage returns; only the text after the last one is visible.
    private static IEnumerable<string> Lines(string output) =>
        output.Split('\n').Select(l => l.TrimEnd('\r')).Select(l => l[(l.LastIndexOf('\r') + 1)..]);

    private static string? LastLine(string output) =>
        Clean(output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault());

    private static string? Clean(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) || trimmed.Length > 128 || trimmed.Any(char.IsControl) ? null : trimmed;
    }
}
