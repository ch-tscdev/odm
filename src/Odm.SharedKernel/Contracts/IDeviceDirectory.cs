namespace Odm.SharedKernel.Contracts;

/// <param name="ApprovalPending">Registered under an existing name and not approved yet: in no group, gets nothing.</param>
public sealed record DeviceRef(Guid Id, string Name, string OsFamily, string OsName, bool ApprovalPending = false);

/// <summary>
/// Read access to devices for other modules (e.g. Software): who belongs to a group, who owns a device.
/// </summary>
public interface IDeviceDirectory
{
    Task<DeviceRef?> GetAsync(Guid deviceId, CancellationToken ct);

    Task<IReadOnlyDictionary<Guid, DeviceRef>> GetManyAsync(IReadOnlyCollection<Guid> deviceIds, CancellationToken ct);

    Task<bool> GroupExistsAsync(Guid groupId, CancellationToken ct);

    Task<IReadOnlyDictionary<Guid, string>> GetGroupNamesAsync(IReadOnlyCollection<Guid> groupIds, CancellationToken ct);

    /// <summary>Current members of a dynamic group (devices waiting for approval are never members).</summary>
    Task<IReadOnlyList<DeviceRef>> ResolveGroupAsync(Guid groupId, CancellationToken ct);

    /// <summary>Devices whose primary user is the given user (without devices waiting for approval).</summary>
    Task<IReadOnlyList<DeviceRef>> ResolveUserAsync(string userName, CancellationToken ct);
}
