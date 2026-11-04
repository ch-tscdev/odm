using Odm.SharedKernel;

namespace Odm.Modules.Devices.Domain;

/// <summary>Device property that a group rule targets.</summary>
public enum GroupField
{
    Name = 0,
    OsFamily = 1,
    OsName = 2,
    OsVersion = 3,
    PrimaryUser = 4,
    OrgUnit = 5,
    CostCenter = 6
}

public enum MatchOperator
{
    Is = 0,
    StartsWith = 1,
    EndsWith = 2,
    Contains = 3
}

public enum GroupMatchMode
{
    /// <summary>All rules must match.</summary>
    All = 0,

    /// <summary>At least one rule must match.</summary>
    Any = 1
}

/// <summary>Single rule of a group (value object, owned by the group).</summary>
public sealed class GroupRule
{
    public GroupField Field { get; private set; }
    public MatchOperator Operator { get; private set; }
    public string Value { get; private set; } = "";

    private GroupRule() { }

    public static GroupRule Create(GroupField field, MatchOperator op, string value) =>
        new() { Field = field, Operator = op, Value = value.Trim() };
}

/// <summary>
/// Dynamic device group: members result from the rules. A catch-all group ("Restgruppe") contains only devices that are in
/// no other group counting for catch-all groups, optionally narrowed by its own rules.
/// </summary>
public sealed class DeviceGroup : Entity
{
    private readonly List<GroupRule> _rules = [];

    public string Name { get; private set; } = "";
    public string? Description { get; private set; }
    public GroupMatchMode MatchMode { get; private set; }

    /// <summary>Members are the devices in no other group (that counts for catch-all groups); rules are optional.</summary>
    public bool IsCatchAll { get; private set; }

    /// <summary>Devices of this group are excluded from catch-all groups. Off for broad groups such as "all Windows".</summary>
    public bool CountsForCatchAll { get; private set; } = true;
    public IReadOnlyList<GroupRule> Rules => _rules;

    private DeviceGroup() { }

    public static DeviceGroup Create(
        string name, string? description, GroupMatchMode mode, IEnumerable<GroupRule> rules,
        bool isCatchAll = false, bool countsForCatchAll = true)
    {
        var group = new DeviceGroup();
        group.Update(name, description, mode, rules, isCatchAll, countsForCatchAll);
        return group;
    }

    public void Update(
        string name, string? description, GroupMatchMode mode, IEnumerable<GroupRule> rules,
        bool isCatchAll = false, bool countsForCatchAll = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Name = name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        MatchMode = mode;
        IsCatchAll = isCatchAll;
        CountsForCatchAll = countsForCatchAll;
        _rules.Clear();
        _rules.AddRange(rules);
    }
}
