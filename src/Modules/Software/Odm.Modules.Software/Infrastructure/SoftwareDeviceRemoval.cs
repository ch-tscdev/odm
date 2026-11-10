using Microsoft.EntityFrameworkCore;
using Odm.Modules.Software.Domain;
using Odm.SharedKernel.Contracts;

namespace Odm.Modules.Software.Infrastructure;

/// <summary>Deleted device: its installation records and the assignments made directly to it are removed.</summary>
internal sealed class SoftwareDeviceRemoval(SoftwareDbContext db) : IDeviceRemovalHandler
{
    public async Task RemoveDeviceAsync(Guid deviceId, CancellationToken ct)
    {
        var target = deviceId.ToString("D");
        await db.Installations.Where(i => i.DeviceId == deviceId).ExecuteDeleteAsync(ct);
        await db.Assignments.Where(a => a.TargetKind == TargetKind.Device && a.Target == target).ExecuteDeleteAsync(ct);
        await db.ProfileAssignments.Where(a => a.TargetKind == TargetKind.Device && a.Target == target).ExecuteDeleteAsync(ct);
    }
}
