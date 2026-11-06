using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Odm.Modules.Devices.Application;
using Odm.Modules.Devices.Infrastructure;
using Odm.SharedKernel;

namespace Odm.Modules.Devices.Endpoints;

internal static class DeviceEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/devices").WithTags("Devices").RequireAuthorization();

        group.MapGet("/", async (
            DevicesDbContext db, int? skip, int? take, string? search, Guid? groupId, CancellationToken ct) =>
        {
            IQueryable<Domain.Device> query = db.Devices.AsNoTracking();

            if (groupId is { } id)
            {
                var groups = await db.Groups.AsNoTracking().ToListAsync(ct);
                var deviceGroup = groups.SingleOrDefault(g => g.Id == id);
                if (deviceGroup is null)
                {
                    return Results.NotFound();
                }

                query = query.Where(GroupFilter.For(deviceGroup, groups));
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim().ToLower();
                query = query.Where(d => d.Name.ToLower().Contains(term));
            }

            return Results.Ok(await DeviceQueries.PageAsync(query, skip, take, ct));
        });

        // Key figures for the dashboard. "Silent" = no heartbeat for silentHours hours (or never one).
        group.MapGet("/stats", async (DevicesDbContext db, int? silentHours, CancellationToken ct) =>
        {
            var now = DateTimeOffset.UtcNow;
            var silentBefore = now.AddHours(-Math.Clamp(silentHours ?? 24, 1, 24 * 365));
            var onlineAfter = now.AddMinutes(-3);

            var devices = db.Devices.AsNoTracking();
            var total = await devices.CountAsync(ct);
            var online = await devices.CountAsync(d => d.LastSeenAt != null && d.LastSeenAt > onlineAfter, ct);
            var neverSeen = await devices.CountAsync(d => d.LastSeenAt == null, ct);
            var silent = await devices.CountAsync(d => d.LastSeenAt == null || d.LastSeenAt < silentBefore, ct);

            var byOs = (await devices.GroupBy(d => d.OsName)
                    .OrderByDescending(g => g.Count())
                    .Take(20)
                    .Select(g => new { g.Key, Count = g.Count() })
                    .ToListAsync(ct))
                .Select(g => new CountDto(g.Key, g.Count)).ToList();
            var byAgent = (await devices.GroupBy(d => d.AgentVersion)
                    .OrderByDescending(g => g.Count())
                    .Take(20)
                    .Select(g => new { g.Key, Count = g.Count() })
                    .ToListAsync(ct))
                .Select(g => new CountDto(g.Key ?? "", g.Count)).ToList();

            var silentDevices = await devices
                .Where(d => d.LastSeenAt == null || d.LastSeenAt < silentBefore)
                .OrderBy(d => d.LastSeenAt)
                .Take(50)
                .Select(d => new SilentDeviceDto(d.Id, d.Name, d.OsName, d.LastSeenAt, d.RegisteredAt))
                .ToListAsync(ct);

            return Results.Ok(new DeviceStatsDto(total, online, silent, neverSeen, byOs, byAgent, silentDevices));
        });

        group.MapGet("/{id:guid}", async (Guid id, DevicesDbContext db, CancellationToken ct) =>
        {
            var device = await db.Devices.AsNoTracking().SingleOrDefaultAsync(d => d.Id == id, ct);
            return device is null ? Results.NotFound() : Results.Ok(DeviceDto.From(device));
        });

        // Groups the device is currently a member of (groups are dynamic and evaluated here).
        group.MapGet("/{id:guid}/groups", async (Guid id, DevicesDbContext db, CancellationToken ct) =>
        {
            if (!await db.Devices.AnyAsync(d => d.Id == id, ct))
            {
                return Results.NotFound();
            }

            var result = new List<DeviceGroupRefDto>();
            var groups = await db.Groups.AsNoTracking().OrderBy(g => g.Name).ToListAsync(ct);
            foreach (var g in groups)
            {
                if (await db.Devices.Where(d => d.Id == id).Where(GroupFilter.For(g, groups)).AnyAsync(ct))
                {
                    result.Add(new DeviceGroupRefDto(g.Id, g.Name, g.Description));
                }
            }

            return Results.Ok(result);
        });

        group.MapPut("/{id:guid}/assignment", async (
                Guid id, AssignmentRequest request, DevicesDbContext db, CancellationToken ct) =>
            {
                var errors = new Dictionary<string, string[]>();
                if (request.PrimaryUser is { Length: > 256 }) errors["primaryUser"] = ["At most 256 characters."];
                if (request.OrgUnit is { Length: > 256 }) errors["orgUnit"] = ["At most 256 characters."];
                if (request.CostCenter is { Length: > 64 }) errors["costCenter"] = ["At most 64 characters."];
                if (errors.Count > 0)
                {
                    return Results.ValidationProblem(errors);
                }

                var device = await db.Devices.SingleOrDefaultAsync(d => d.Id == id, ct);
                if (device is null)
                {
                    return Results.NotFound();
                }

                device.UpdateAssignment(request.PrimaryUser, request.OrgUnit, request.CostCenter);
                await db.SaveChangesAsync(ct);
                return Results.Ok(DeviceDto.From(device));
            })
            .RequireAuthorization(Policies.Operator);

        // Allow (for 24 hours, once) that the next registration with this device's name takes the device over.
        group.MapPut("/{id:guid}/reinstall", async (Guid id, ReinstallRequest request, DevicesDbContext db, CancellationToken ct) =>
            {
                var device = await db.Devices.SingleOrDefaultAsync(d => d.Id == id, ct);
                if (device is null)
                {
                    return Results.NotFound();
                }

                if (request.Allowed)
                {
                    device.AllowReinstall(DateTimeOffset.UtcNow.Add(ReinstallWindow));
                }
                else
                {
                    device.CancelReinstall();
                }

                await db.SaveChangesAsync(ct);
                return Results.Ok(DeviceDto.From(device));
            })
            .RequireAuthorization(Policies.Operator);

        // Approve a device that registered with the name of an existing device: it becomes a normal device.
        group.MapPost("/{id:guid}/approve", async (Guid id, DevicesDbContext db, CancellationToken ct) =>
            {
                var device = await db.Devices.SingleOrDefaultAsync(d => d.Id == id, ct);
                if (device is null)
                {
                    return Results.NotFound();
                }

                device.Approve();
                await db.SaveChangesAsync(ct);
                return Results.Ok(DeviceDto.From(device));
            })
            .RequireAuthorization(Policies.Operator);

        // Adopt the host name the agent reports (renamed machine).
        group.MapPost("/{id:guid}/accept-name", async (Guid id, DevicesDbContext db, CancellationToken ct) =>
            {
                var device = await db.Devices.SingleOrDefaultAsync(d => d.Id == id, ct);
                if (device is null)
                {
                    return Results.NotFound();
                }

                if (device.ReportedName is not { } reported)
                {
                    return Results.Ok(DeviceDto.From(device));
                }

                var lowered = reported.ToLower();
                if (await db.Devices.AnyAsync(d => d.Id != id && d.Name.ToLower() == lowered, ct))
                {
                    return Results.ValidationProblem(
                        new Dictionary<string, string[]> { ["name"] = ["Another device already has this name."] },
                        statusCode: StatusCodes.Status409Conflict);
                }

                device.AcceptReportedName();
                await db.SaveChangesAsync(ct);
                return Results.Ok(DeviceDto.From(device));
            })
            .RequireAuthorization(Policies.Operator);
    }

    /// <summary>How long an allowed reinstall stays open.</summary>
    public static readonly TimeSpan ReinstallWindow = TimeSpan.FromHours(24);
}
