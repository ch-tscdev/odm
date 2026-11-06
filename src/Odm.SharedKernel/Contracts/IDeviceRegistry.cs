namespace Odm.SharedKernel.Contracts;

/// <summary>Device information reported by the agent.</summary>
public sealed record DeviceReport(
    string Hostname,
    string OsFamily,
    string OsName,
    string OsVersion,
    string? AgentVersion);

/// <summary>
/// Interface of the Devices module for other modules (e.g. Agents).
/// This keeps the modules decoupled: they only know this contract, not the implementation.
/// </summary>
public interface IDeviceRegistry
{
    /// <summary>
    /// Creates the device. An existing device with the same name is taken over only if an operator allowed a reinstall;
    /// otherwise the new device waits for approval.
    /// </summary>
    Task<Guid> RegisterAsync(DeviceReport report, CancellationToken ct);

    /// <summary>False while a device waits for approval: its agent gets no jobs, packages or secrets.</summary>
    Task<bool> IsApprovedAsync(Guid deviceId, CancellationToken ct);

    /// <summary>Updates OS information and "last seen". Unknown devices are ignored.</summary>
    Task ReportAsync(Guid deviceId, DeviceReport report, CancellationToken ct);
}
