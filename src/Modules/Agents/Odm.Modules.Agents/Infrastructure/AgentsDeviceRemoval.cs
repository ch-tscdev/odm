using Microsoft.EntityFrameworkCore;
using Odm.SharedKernel.Contracts;

namespace Odm.Modules.Agents.Infrastructure;

/// <summary>Deleted device: its agents lose access at once (credentials removed) and pending jobs are dropped.</summary>
internal sealed class AgentsDeviceRemoval(AgentsDbContext db) : IDeviceRemovalHandler
{
    public async Task RemoveDeviceAsync(Guid deviceId, CancellationToken ct)
    {
        await db.Credentials.Where(c => c.DeviceId == deviceId).ExecuteDeleteAsync(ct);
        await db.Jobs.Where(j => j.DeviceId == deviceId).ExecuteDeleteAsync(ct);
    }
}
