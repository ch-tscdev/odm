using Microsoft.EntityFrameworkCore;
using Odm.Modules.Software.Domain;

namespace Odm.Modules.Software.Application;

/// <summary>
/// What the UI shows as a version. PSADT versions are uploads ("1.1"). Repo packages have internal revisions
/// ("latest (r5)": one per change of the settings) that mean nothing to users: for them the pinned version is
/// shown, or <see cref="Latest"/> when the package manager installs the newest one (the UI translates it).
/// </summary>
public static class VersionLabels
{
    public const string Latest = "latest";

    public static string Display(string version, string? specSnapshot) =>
        specSnapshot is null ? version : PackageSpec.Parse(specSnapshot).Version ?? Latest;

    public static async Task<Dictionary<Guid, string>> LoadAsync(IQueryable<PackageVersion> versions, CancellationToken ct) =>
        (await versions.AsNoTracking().Select(v => new { v.Id, v.Version, v.SpecSnapshot }).ToListAsync(ct))
        .ToDictionary(v => v.Id, v => Display(v.Version, v.SpecSnapshot));

    /// <summary>Installed version: what the package manager reported, otherwise the label of the installed version.</summary>
    public static string? Installed(Installation installation, IReadOnlyDictionary<Guid, string> labels) =>
        installation.ReportedVersion
        ?? (installation.InstalledVersionId is { } id ? labels.GetValueOrDefault(id) : null);
}
