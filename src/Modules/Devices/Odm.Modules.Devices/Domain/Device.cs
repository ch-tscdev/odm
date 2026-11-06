using Odm.SharedKernel;

namespace Odm.Modules.Devices.Domain;

public enum OsFamily
{
    Windows = 0,
    Linux = 1
}

public sealed class Device : Entity
{
    public string Name { get; private set; } = "";
    public OsFamily OsFamily { get; private set; }
    public string OsName { get; private set; } = "";
    public string OsVersion { get; private set; } = "";

    /// <summary>Primary user (directory user, not necessarily an ODM login).</summary>
    public string? PrimaryUser { get; private set; }

    /// <summary>Organizational unit of the primary user (later synchronized from the directory).</summary>
    public string? PrimaryUserOrgUnit { get; private set; }

    public string? CostCenter { get; private set; }
    public DateTimeOffset RegisteredAt { get; private set; }
    public DateTimeOffset? LastSeenAt { get; private set; }
    public string? AgentVersion { get; private set; }

    /// <summary>Until then a new registration with this device's name takes the device over (reinstall), once.</summary>
    public DateTimeOffset? ReinstallAllowedUntil { get; private set; }

    /// <summary>
    /// Registered with the name of an existing device without an allowed reinstall: until an operator approves it, the device
    /// is in no group and its agent gets no jobs, packages or secrets.
    /// </summary>
    public bool ApprovalPending { get; private set; }

    /// <summary>Host name the agent reports when it differs from <see cref="Name"/>; adopted only by an operator.</summary>
    public string? ReportedName { get; private set; }

    private Device() { }

    public static Device Register(string name, OsFamily family, string osName, string osVersion, bool approvalPending = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return new Device
        {
            Name = name.Trim(),
            OsFamily = family,
            OsName = osName,
            OsVersion = osVersion,
            RegisteredAt = DateTimeOffset.UtcNow,
            ApprovalPending = approvalPending
        };
    }

    public void AllowReinstall(DateTimeOffset until) => ReinstallAllowedUntil = until;

    public void CancelReinstall() => ReinstallAllowedUntil = null;

    public bool ReinstallAllowed(DateTimeOffset now) => ReinstallAllowedUntil is { } until && now < until;

    /// <summary>Takes the device over for a reinstall if allowed; the permission is used up.</summary>
    public bool TryUseReinstall(DateTimeOffset now)
    {
        if (!ReinstallAllowed(now))
        {
            return false;
        }

        ReinstallAllowedUntil = null;
        return true;
    }

    public void Approve() => ApprovalPending = false;

    /// <summary>Remembers the host name reported by the agent; the device name itself does not change.</summary>
    public void ReportHostname(string hostname)
    {
        var reported = hostname.Trim();
        ReportedName = reported.Length == 0 || reported == Name ? null : reported;
    }

    /// <summary>Adopts the reported host name (operator decision).</summary>
    public void AcceptReportedName()
    {
        if (ReportedName is { } reported)
        {
            Rename(reported);
            ReportedName = null;
        }
    }

    public void UpdateAssignment(string? primaryUser, string? primaryUserOrgUnit, string? costCenter)
    {
        PrimaryUser = Clean(primaryUser);
        PrimaryUserOrgUnit = Clean(primaryUserOrgUnit);
        CostCenter = Clean(costCenter);
    }

    /// <summary>Adopts a changed host name (a change of letter case counts as a change).</summary>
    public void Rename(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name.Trim();
    }

    public void MarkSeen() => LastSeenAt = DateTimeOffset.UtcNow;

    /// <summary>Adopts the values reported by the agent.</summary>
    public void ApplyReport(OsFamily family, string osName, string osVersion, string? agentVersion)
    {
        OsFamily = family;
        OsName = osName;
        OsVersion = osVersion;
        AgentVersion = agentVersion;
        MarkSeen();
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
