using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Odm.Modules.OsUpdates.Domain;
using Odm.Modules.OsUpdates.Infrastructure;
using Odm.SharedKernel.Text;
using Odm.SharedKernel.Contracts;

namespace Odm.Modules.OsUpdates.Application;

public enum RunNowResult
{
    Started,
    NoPolicy,
    NoAgent
}

/// <summary>
/// Schedules update runs: if a maintenance window is open and a policy applies to the device,
/// an "os-update" job is issued to the agent (at most once per device and window).
/// </summary>
public sealed class UpdateSchedulingService(
    OsUpdatesDbContext db,
    IDeviceDirectory directory,
    IJobQueue jobs,
    ILogger<UpdateSchedulingService> logger)
{
    private const int TimeoutMinutes = 240;
    private const int ManualWindowMinutes = 60;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Problems inside an open window (no policy, no agent) are logged once per device and window occurrence at
    /// Information level, not on every pass. Details for every pass are logged at Debug level.
    /// </summary>
    private static readonly ConcurrentDictionary<(Guid Device, Guid Window, DateTimeOffset Start), byte> Reported = new();

    public async Task RunScheduledAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        var windows = await db.Windows.AsNoTracking().ToDictionaryAsync(w => w.Id, ct);
        if (windows.Count == 0)
        {
            logger.LogDebug("OS updates: no maintenance windows are defined; nothing is scheduled.");
            return; // without a maintenance window nothing is ever updated automatically
        }

        var policies = await db.Policies.AsNoTracking().ToDictionaryAsync(p => p.Id, ct);
        var assignments = await db.Assignments.AsNoTracking().OrderBy(a => a.CreatedAt).ToListAsync(ct);
        var groupCache = new Dictionary<Guid, IReadOnlyList<DeviceRef>>();
        var names = new Dictionary<Guid, string>();

        // Policy per device: direct assignment before group; with several groups the oldest assignment wins.
        var policyOf = new Dictionary<Guid, (Guid PolicyId, int Priority)>();
        foreach (var a in assignments.Where(a => a.Scope == AssignmentScope.Policy && policies.ContainsKey(a.ParentId)))
        {
            foreach (var device in await ResolveAsync(a, groupCache, ct))
            {
                names[device.Id] = device.Name;
                var priority = (int)a.TargetKind;
                if (!policyOf.TryGetValue(device.Id, out var current) || priority < current.Priority)
                {
                    policyOf[device.Id] = (a.ParentId, priority);
                }
            }
        }

        // Windows per device: directly assigned windows replace those of the groups; several group windows add up.
        var direct = new Dictionary<Guid, HashSet<Guid>>();
        var viaGroup = new Dictionary<Guid, HashSet<Guid>>();
        foreach (var a in assignments.Where(a => a.Scope == AssignmentScope.Window && windows.ContainsKey(a.ParentId)))
        {
            var map = a.TargetKind == TargetKind.Device ? direct : viaGroup;
            foreach (var device in await ResolveAsync(a, groupCache, ct))
            {
                names[device.Id] = device.Name;
                if (!map.TryGetValue(device.Id, out var set))
                {
                    map[device.Id] = set = [];
                }

                set.Add(a.ParentId);
            }
        }

        var since = now.AddDays(-2);
        var existing = (await db.Runs.AsNoTracking()
                .Where(r => r.OccurrenceStart >= since)
                .Select(r => new { r.DeviceId, r.WindowId, r.OccurrenceStart })
                .ToListAsync(ct))
            .Select(r => (r.DeviceId, r.WindowId, r.OccurrenceStart))
            .ToHashSet();

        var started = 0;
        var inWindow = 0;
        var candidates = policyOf.Keys.Union(direct.Keys).Union(viaGroup.Keys).ToList();
        foreach (var deviceId in candidates)
        {
            // Device names come from the agent: sanitized against log forging.
            var name = LogSanitizer.Clean(names.GetValueOrDefault(deviceId) ?? deviceId.ToString());
            var hasPolicy = policyOf.TryGetValue(deviceId, out var assigned);
            var effective = direct.TryGetValue(deviceId, out var own) ? own
                : viaGroup.TryGetValue(deviceId, out var inherited) ? inherited
                : null;
            if (effective is null)
            {
                logger.LogDebug("OS updates: {Device} has an update policy but no maintenance window; no automatic runs.", name);
                continue;
            }

            var open = new List<(Guid Id, DateTimeOffset Start, DateTimeOffset End)>();
            foreach (var windowId in effective)
            {
                if (windows[windowId].TryGetOccurrence(now, out var start, out var end))
                {
                    open.Add((windowId, start, end));
                }
            }

            if (open.Count == 0)
            {
                logger.LogDebug(
                    "OS updates: {Device} is outside its maintenance windows ({Windows}); server time {Now:u}.",
                    name, string.Join("; ", effective.Select(id => Describe(windows[id]))), now);
                continue;
            }

            inWindow++;
            var first = open[0];
            if (!hasPolicy)
            {
                ReportOnce(deviceId, first.Id, first.Start, () => logger.LogInformation(
                    "OS updates: maintenance window '{Window}' is open for {Device}, but no update policy is assigned; no run is started.",
                    windows[first.Id].Name, name));
                continue;
            }

            var due = open.Where(o => !existing.Contains((deviceId, o.Id, o.Start))).ToList();
            if (due.Count == 0)
            {
                logger.LogDebug(
                    "OS updates: {Device} already has a run for the open window '{Window}' (started {Start:u}).",
                    name, windows[first.Id].Name, first.Start);
                continue;
            }

            var target = due[0];
            var policy = policies[assigned.PolicyId];
            var jobId = await EnqueueAsync(deviceId, policy, target.End, ct);
            if (jobId is null)
            {
                ReportOnce(deviceId, target.Id, target.Start, () => logger.LogInformation(
                    "OS updates: maintenance window '{Window}' is open for {Device}, but the device has no registered agent; no run is started.",
                    windows[target.Id].Name, name));
                continue;
            }

            db.Runs.Add(UpdateRun.Create(deviceId, target.Id, target.Start, jobId.Value, now));
            await db.SaveChangesAsync(ct);
            existing.Add((deviceId, target.Id, target.Start));
            started++;
            logger.LogInformation(
                "OS updates: run started for {Device} in window '{Window}' with policy '{Policy}' (window ends {End:u}).",
                name, windows[target.Id].Name, policy.Name, target.End);
        }

        logger.LogDebug(
            "OS updates: pass at {Now:u}: {Windows} windows, {Devices} devices with a policy or window, {Open} inside an open window, {Started} runs started.",
            now, windows.Count, candidates.Count, inWindow, started);
    }

    private static string Describe(MaintenanceWindow w) =>
        $"'{w.Name}' {w.Days} {w.StartMinutes / 60:00}:{w.StartMinutes % 60:00} for {w.DurationMinutes} min ({w.TimeZoneId})";

    private static void ReportOnce(Guid deviceId, Guid windowId, DateTimeOffset start, Action log)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-2);
        foreach (var key in Reported.Keys.Where(k => k.Start < cutoff).ToList())
        {
            Reported.TryRemove(key, out _);
        }

        if (Reported.TryAdd((deviceId, windowId, start), 0))
        {
            log();
        }
    }

    /// <summary>Starts a run immediately (outside the windows), e.g. for testing or urgent patches.</summary>
    public async Task<RunNowResult> RunNowAsync(Guid deviceId, CancellationToken ct)
    {
        var policy = await FindPolicyForDeviceAsync(deviceId, ct);
        if (policy is null)
        {
            return RunNowResult.NoPolicy;
        }

        var now = DateTimeOffset.UtcNow;
        var jobId = await EnqueueAsync(deviceId, policy, now.AddMinutes(ManualWindowMinutes), ct);
        if (jobId is null)
        {
            return RunNowResult.NoAgent;
        }

        db.Runs.Add(UpdateRun.Create(deviceId, Guid.Empty, now, jobId.Value, now));
        await db.SaveChangesAsync(ct);
        return RunNowResult.Started;
    }

    private async Task<UpdatePolicy?> FindPolicyForDeviceAsync(Guid deviceId, CancellationToken ct)
    {
        var assignments = await db.Assignments.AsNoTracking()
            .Where(a => a.Scope == AssignmentScope.Policy)
            .OrderBy(a => a.CreatedAt)
            .ToListAsync(ct);

        var deviceTarget = deviceId.ToString("D");
        var match = assignments.FirstOrDefault(a => a.TargetKind == TargetKind.Device && a.Target == deviceTarget);

        if (match is null)
        {
            foreach (var a in assignments.Where(a => a.TargetKind == TargetKind.Group))
            {
                var members = await directory.ResolveGroupAsync(Guid.Parse(a.Target), ct);
                if (members.Any(m => m.Id == deviceId))
                {
                    match = a;
                    break;
                }
            }
        }

        return match is null ? null : await db.Policies.AsNoTracking().SingleOrDefaultAsync(p => p.Id == match.ParentId, ct);
    }

    private Task<Guid?> EnqueueAsync(Guid deviceId, UpdatePolicy policy, DateTimeOffset windowEnd, CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(
            new OsUpdatePayload(policy.Mode.ToString(), policy.Reboot == RebootBehavior.IfRequired, windowEnd, TimeoutMinutes),
            Json);
        return jobs.EnqueueAsync(deviceId, AgentJobTypes.OsUpdate, payload, ct);
    }

    private async Task<IReadOnlyList<DeviceRef>> ResolveAsync(
        OsAssignment assignment, Dictionary<Guid, IReadOnlyList<DeviceRef>> groupCache, CancellationToken ct)
    {
        if (!Guid.TryParse(assignment.Target, out var id))
        {
            return [];
        }

        if (assignment.TargetKind == TargetKind.Device)
        {
            return await directory.GetAsync(id, ct) is { } device ? [device] : [];
        }

        if (!groupCache.TryGetValue(id, out var members))
        {
            groupCache[id] = members = await directory.ResolveGroupAsync(id, ct);
        }

        return members;
    }
}
