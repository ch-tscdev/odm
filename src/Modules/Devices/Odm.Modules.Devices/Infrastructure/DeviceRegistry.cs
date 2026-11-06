using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Odm.Modules.Devices.Domain;
using Odm.SharedKernel.Contracts;

namespace Odm.Modules.Devices.Infrastructure;

internal sealed class DeviceRegistry(DevicesDbContext db, ILogger<DeviceRegistry> logger) : IDeviceRegistry
{
    public async Task<Guid> RegisterAsync(DeviceReport report, CancellationToken ct)
    {
        var name = report.Hostname.Trim();
        var lowered = name.ToLower();
        var family = ParseFamily(report.OsFamily);

        // A registration takes over an existing device only if an operator allowed a reinstall for it. Otherwise a token
        // holder could take over any device by its name (credentials, jobs, secrets). A registration with the name of an
        // existing device creates a new device that waits for approval.
        var now = DateTimeOffset.UtcNow;
        var sameName = await db.Devices.Where(d => d.Name.ToLower() == lowered).ToListAsync(ct);
        var device = sameName.FirstOrDefault(d => d.ReinstallAllowed(now));
        if (device is not null)
        {
            device.TryUseReinstall(now);
            logger.LogInformation("Device {Device} ({Id}) was taken over by a reinstall.", device.Name, device.Id);
        }
        else
        {
            device = Device.Register(name, family, report.OsName, report.OsVersion, approvalPending: sameName.Count > 0);
            db.Devices.Add(device);
            if (device.ApprovalPending)
            {
                logger.LogWarning(
                    "Registration with the name of an existing device: new device {Device} ({Id}) waits for approval.",
                    device.Name, device.Id);
            }
        }

        device.ApplyReport(family, report.OsName, report.OsVersion, report.AgentVersion);
        await db.SaveChangesAsync(ct);
        return device.Id;
    }

    public Task<bool> IsApprovedAsync(Guid deviceId, CancellationToken ct) =>
        db.Devices.AnyAsync(d => d.Id == deviceId && !d.ApprovalPending, ct);

    public async Task ReportAsync(Guid deviceId, DeviceReport report, CancellationToken ct)
    {
        var device = await db.Devices.SingleOrDefaultAsync(d => d.Id == deviceId, ct);
        if (device is null)
        {
            return;
        }

        // A renamed machine is only shown; the agent could otherwise move the device into groups that match on the name.
        device.ReportHostname(report.Hostname);

        device.ApplyReport(ParseFamily(report.OsFamily), report.OsName, report.OsVersion, report.AgentVersion);
        await db.SaveChangesAsync(ct);
    }

    private static OsFamily ParseFamily(string family) =>
        Enum.TryParse<OsFamily>(family, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : throw new ArgumentException("Unsupported OS family.", nameof(family));
}
