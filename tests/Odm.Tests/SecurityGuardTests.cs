using System.Text.RegularExpressions;

namespace Odm.Tests;

/// <summary>
/// Guards that a change cannot quietly add anonymous endpoints or new code paths that start programs or open network
/// connections from the server modules. A legitimate new entry must be added here deliberately, which shows up in the review.
/// </summary>
public class SecurityGuardTests
{
    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "OpenDeviceManager.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private static IEnumerable<string> SourceFiles(string relative) =>
        Directory.EnumerateFiles(Path.Combine(Root(), relative), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                        !f.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}"));

    private static Dictionary<string, int> Count(string relative, string pattern) =>
        SourceFiles(relative)
            .Select(f => (File: Path.GetRelativePath(Root(), f).Replace('\\', '/'), Count: Regex.Matches(File.ReadAllText(f), pattern).Count))
            .Where(x => x.Count > 0)
            .ToDictionary(x => x.File, x => x.Count);

    [Fact]
    public void Anonymous_endpoints_are_the_known_ones()
    {
        var actual = Count("src", @"\.AllowAnonymous\(\)");

        var expected = new Dictionary<string, int>
        {
            ["src/Modules/Identity/Odm.Modules.Identity/IdentityModule.cs"] = 4, // sign-in, provider list, OIDC start and callback
            ["src/Modules/Agents/Odm.Modules.Agents/Endpoints/InstallEndpoints.cs"] = 1, // install scripts and agent downloads
            ["src/Modules/Agents/Odm.Modules.Agents/Endpoints/AgentEndpoints.cs"] = 1 // registration with a one-time token
        };

        Assert.Equal(expected.OrderBy(x => x.Key), actual.OrderBy(x => x.Key));
    }

    [Fact]
    public void Api_keys_are_accepted_by_their_two_policies_only()
    {
        // The API key scheme is named in exactly two policies (Enrollment, DeviceDeletion) ...
        Assert.Equal(
            new Dictionary<string, int> { ["src/Modules/Identity/Odm.Modules.Identity/IdentityModule.cs"] = 2 }.OrderBy(x => x.Key),
            Count("src", @"AddAuthenticationSchemes\([^)]*ApiKeyAuthenticationHandler\.SchemeName").OrderBy(x => x.Key));

        // ... Enrollment is only used for creating registration tokens ...
        Assert.Equal(
            new Dictionary<string, int>
            {
                ["src/Modules/Identity/Odm.Modules.Identity/IdentityModule.cs"] = 1, // definition
                ["src/Modules/Agents/Odm.Modules.Agents/Endpoints/AdminEndpoints.cs"] = 1 // POST /agents/tokens
            }.OrderBy(x => x.Key),
            Count("src", @"Policies\.Enrollment\b").OrderBy(x => x.Key));

        // ... and DeviceDeletion only for deleting devices (by ID and by name).
        Assert.Equal(
            new Dictionary<string, int>
            {
                ["src/Modules/Identity/Odm.Modules.Identity/IdentityModule.cs"] = 1, // definition
                ["src/Modules/Devices/Odm.Modules.Devices/Endpoints/DeviceEndpoints.cs"] = 2 // DELETE /devices/{id}, /devices/by-name/{name}
            }.OrderBy(x => x.Key),
            Count("src", @"Policies\.DeviceDeletion\b").OrderBy(x => x.Key));
    }

    [Fact]
    public void Agent_does_not_watch_files_for_configuration_changes()
    {
        // Reload on change watches a whole directory tree; on Linux every directory costs an inotify watch.
        Assert.Equal(
            new Dictionary<string, int> { ["src/Agent/Odm.Agent/Program.cs"] = 1 },
            Count("src/Agent", @"hostBuilder:reloadConfigOnChange\W+=\s*\W+false"));
        Assert.Empty(Count("src/Agent", @"reloadOnChange:\s*true"));
        Assert.Empty(Count("src/Agent", @"\bFileSystemWatcher\b"));
    }

    [Fact]
    public void Server_modules_do_not_start_processes()
    {
        Assert.Empty(Count("src/Modules", @"Process\.Start|new\s+ProcessStartInfo|ProcessRunner"));
        Assert.Empty(Count("src/Odm.Host", @"Process\.Start|new\s+ProcessStartInfo|ProcessRunner"));
    }
}
