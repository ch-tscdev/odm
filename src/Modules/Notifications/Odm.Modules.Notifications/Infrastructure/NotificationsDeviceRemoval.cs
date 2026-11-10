using Microsoft.EntityFrameworkCore;
using Odm.SharedKernel.Contracts;

namespace Odm.Modules.Notifications.Infrastructure;

/// <summary>Deleted device: records of alerts sent about it are removed.</summary>
internal sealed class NotificationsDeviceRemoval(NotificationsDbContext db) : IDeviceRemovalHandler
{
    public Task RemoveDeviceAsync(Guid deviceId, CancellationToken ct) =>
        db.Sent.Where(s => s.DeviceId == deviceId).ExecuteDeleteAsync(ct);
}
