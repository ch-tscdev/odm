namespace Odm.SharedKernel.Contracts;

/// <summary>
/// Every module that stores data per device implements this and removes that data when a device is deleted. The Devices
/// module calls all handlers before it deletes the device itself. Handlers must be idempotent: if a deletion fails half way,
/// it is simply repeated.
/// </summary>
public interface IDeviceRemovalHandler
{
    Task RemoveDeviceAsync(Guid deviceId, CancellationToken ct);
}
