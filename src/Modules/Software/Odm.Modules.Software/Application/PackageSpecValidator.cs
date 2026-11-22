using System.Text.RegularExpressions;
using Odm.Modules.Software.Domain;

namespace Odm.Modules.Software.Application;

/// <summary>
/// Validates and normalizes package settings. Identifiers and versions always start with a letter or
/// a digit so that they cannot be interpreted as an option of the package manager.
/// </summary>
public static partial class PackageSpecValidator
{
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._+:@/\-]{0,199}$")]
    private static partial Regex IdentifierPattern();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._+:~\-]{0,63}$")]
    private static partial Regex VersionPattern();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._\-]{0,63}$")]
    private static partial Regex NamePattern();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._:/\-]{0,199}$")]
    private static partial Regex SourceUrlPattern();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._/\-]{0,63}$")]
    private static partial Regex ChannelPattern();

    public const int MaxArgumentsLength = 1000;
    public const int MaxEnvironmentVariables = 20;
    public const int MaxEnvironmentLength = 2000;

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]{0,127}$")]
    private static partial Regex EnvironmentNamePattern();

    /// <summary>
    /// Set by the agent for unattended APT runs (DEBIAN_FRONTEND, NEEDRESTART_MODE, LC_ALL) or controlling which
    /// program and libraries run at all (PATH, LD_*): not overridable.
    /// </summary>
    private static readonly HashSet<string> ReservedEnvironmentNames =
        new(["DEBIAN_FRONTEND", "NEEDRESTART_MODE", "LC_ALL", "PATH"], StringComparer.OrdinalIgnoreCase);

    public static bool TryValidate(
        PackageKind kind,
        PackageSpecDto? dto,
        IReadOnlySet<string> knownSecrets,
        out PackageSpec spec,
        out Dictionary<string, string[]> errors)
    {
        errors = [];
        dto ??= new PackageSpecDto(null, null, null, null, null, null, false, null);

        var identifier = Clean(dto.Identifier);
        var version = Clean(dto.Version);
        var scope = Clean(dto.Scope)?.ToLowerInvariant();
        var source = Clean(dto.Source);
        var remote = Clean(dto.Remote);
        var channel = Clean(dto.Channel);
        var arguments = Clean(dto.Arguments);
        var environment = kind == PackageKind.Apt ? NormalizeEnvironment(dto.Environment, errors) : null;
        string? platform = null;
        if (kind == PackageKind.Psadt)
        {
            var requested = Clean(dto.Platform);
            if (requested is null || string.Equals(requested, "Windows", StringComparison.OrdinalIgnoreCase))
            {
                platform = null;
            }
            else if (string.Equals(requested, PackageSpec.LinuxPlatform, StringComparison.OrdinalIgnoreCase))
            {
                platform = PackageSpec.LinuxPlatform;
            }
            else
            {
                errors["platform"] = ["Platform: Windows or Linux."];
            }
        }

        if (kind.IsRepo())
        {
            if (identifier is null || !IdentifierPattern().IsMatch(identifier))
                errors["identifier"] = ["Package name/ID is required (letters, digits and . _ + : @ / -)."];
            if (version is not null && !VersionPattern().IsMatch(version))
                errors["version"] = ["Invalid version."];
        }
        else
        {
            identifier = null;
            version = null;
        }

        switch (kind)
        {
            case PackageKind.Winget:
                scope ??= "machine";
                if (scope is not ("machine" or "user")) errors["scope"] = ["Scope: machine or user."];
                if (source is not null && !NamePattern().IsMatch(source)) errors["source"] = ["Invalid source."];
                remote = channel = null;
                break;
            case PackageKind.Chocolatey:
                scope = remote = channel = null;
                if (source is not null && !SourceUrlPattern().IsMatch(source)) errors["source"] = ["Invalid source."];
                break;
            case PackageKind.Flatpak:
                scope ??= "system";
                if (scope is not ("system" or "user")) errors["scope"] = ["Scope: system or user."];
                remote ??= "flathub";
                if (!NamePattern().IsMatch(remote)) errors["remote"] = ["Invalid remote."];
                source = channel = null;
                break;
            case PackageKind.Snap:
                channel ??= "stable";
                if (!ChannelPattern().IsMatch(channel)) errors["channel"] = ["Invalid channel."];
                scope = source = remote = null;
                break;
            default: // Psadt, Apt, Rpm
                scope = source = remote = channel = null;
                break;
        }

        var classic = kind == PackageKind.Snap && dto.Classic;

        if (arguments is not null)
        {
            if (arguments.Length > MaxArgumentsLength)
            {
                errors["arguments"] = [$"At most {MaxArgumentsLength} characters."];
            }
            else
            {
                var unknown = SecretReferences.Extract(arguments).Where(n => !knownSecrets.Contains(n)).ToList();
                if (unknown.Count > 0)
                {
                    errors["arguments"] = [$"Unknown secret: {string.Join(", ", unknown)}."];
                }
            }
        }

        if (environment is not null && !errors.ContainsKey("environment"))
        {
            var unknown = SecretReferences.Extract(environment).Where(n => !knownSecrets.Contains(n)).ToList();
            if (unknown.Count > 0)
            {
                errors["environment"] = [$"Unknown secret: {string.Join(", ", unknown)}."];
            }
        }

        spec = new PackageSpec(identifier, version, scope, source, remote, channel, classic, arguments, platform, environment);
        return errors.Count == 0;
    }

    /// <summary>
    /// Environment variables, one NAME=value per line. Empty lines are dropped, names checked; the result is the
    /// normalized text (lines joined with \n) so that equal settings always give the same JSON.
    /// </summary>
    private static string? NormalizeEnvironment(string? text, Dictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var lines = text.Replace("\r\n", "\n").Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            var separator = line.IndexOf('=');
            var name = separator < 0 ? line : line[..separator].Trim();
            if (separator < 0 || !EnvironmentNamePattern().IsMatch(name))
            {
                errors["environment"] = [$"Invalid line \"{Shorten(line)}\": expected NAME=value (letters, digits, _; not starting with a digit)."];
                return null;
            }

            if (ReservedEnvironmentNames.Contains(name) || name.StartsWith("LD_", StringComparison.OrdinalIgnoreCase))
            {
                errors["environment"] = [$"{name} is set by the agent or not allowed."];
                return null;
            }

            if (!names.Add(name))
            {
                errors["environment"] = [$"{name} appears more than once."];
                return null;
            }
        }

        var normalized = string.Join('\n', lines.Select(l =>
        {
            var separator = l.IndexOf('=');
            return $"{l[..separator].Trim()}={l[(separator + 1)..].Trim()}";
        }));
        if (lines.Count > MaxEnvironmentVariables)
        {
            errors["environment"] = [$"At most {MaxEnvironmentVariables} variables."];
        }
        else if (normalized.Length > MaxEnvironmentLength)
        {
            errors["environment"] = [$"At most {MaxEnvironmentLength} characters."];
        }

        return normalized.Length == 0 ? null : normalized;
    }

    private static string Shorten(string value) => value.Length <= 40 ? value : value[..40] + "…";

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
