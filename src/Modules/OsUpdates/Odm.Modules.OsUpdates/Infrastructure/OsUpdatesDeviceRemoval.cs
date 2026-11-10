using Microsoft.EntityFrameworkCore;
using Odm.Modules.OsUpdates.Domain;
using Odm.SharedKernel.Contracts;

namespace Odm.Modules.OsUpdates.Infrastructure;

/// <summary>Deleted device: its update runs (with their items), update history and direct assignments are removed.</summary>
internal sealed class OsUpdatesDeviceRemoval(OsUpdatesDbContext db) : IDeviceRemovalHandler
{
    public async Task RemoveDeviceAsync(Guid deviceId, CancellationToken ct)
    {
        var target = deviceId.ToString("D");
        await db.Runs.Where(r => r.DeviceId == deviceId).ExecuteDeleteAsync(ct);
        await db.ReportedUpdates.Where(e => e.DeviceId == deviceId).ExecuteDeleteAsync(ct);
        await db.Assignments.Where(a => a.TargetKind == TargetKind.Device && a.Target == target).ExecuteDeleteAsync(ct);
    }
}
