using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Odm.Modules.Devices.Infrastructure;
using Odm.SharedKernel.Contracts;
using Odm.SharedKernel.Text;

namespace Odm.Modules.Devices.Application;

/// <summary>
/// Deletes a device with everything the modules store about it. The other modules go first (the agent loses access before
/// anything else); the device row is removed last, so a failed deletion leaves the device visible and can be repeated.
/// The audit log keeps the deletion itself.
/// </summary>
internal sealed class DeviceRemoval(
    DevicesDbContext db, IEnumerable<IDeviceRemovalHandler> handlers, ILogger<DeviceRemoval> logger)
{
    /// <summary>False if the device does not exist.</summary>
    public async Task<bool> RemoveAsync(Guid deviceId, CancellationToken ct)
    {
        var device = await db.Devices.AsNoTracking().SingleOrDefaultAsync(d => d.Id == deviceId, ct);
        if (device is null)
        {
            return false;
        }

        foreach (var handler in handlers)
        {
            await handler.RemoveDeviceAsync(deviceId, ct);
        }

        await db.Software.Where(s => s.DeviceId == deviceId).ExecuteDeleteAsync(ct);
        await db.Hardware.Where(h => h.DeviceId == deviceId).ExecuteDeleteAsync(ct);
        await db.Devices.Where(d => d.Id == deviceId).ExecuteDeleteAsync(ct);

        logger.LogInformation("Device {Device} ({Id}) deleted.", LogSanitizer.Clean(device.Name), deviceId);
        return true;
    }
}
