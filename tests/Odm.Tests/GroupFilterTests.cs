using Odm.Modules.Devices.Application;
using Odm.Modules.Devices.Domain;

namespace Odm.Tests;

/// <summary>
/// The group filter is built for EF Core; here the same expression is evaluated in memory.
/// All test devices have the checked fields set (in memory an empty field would throw a NullReferenceException,
/// in SQL it results in "no match").
/// </summary>
public class GroupFilterTests
{
    private static Device NewDevice(string name, OsFamily family, string osName, string osVersion, string orgUnit = "IT")
    {
        var device = Device.Register(name, family, osName, osVersion);
        device.UpdateAssignment("user", orgUnit, "KST-1");
        return device;
    }

    private static readonly Device Pc1 = NewDevice("PC-0001", OsFamily.Windows, "Windows 11 Pro", "24H2", "Finanz");
    private static readonly Device Pc2 = NewDevice("PC-0002", OsFamily.Windows, "Windows 10 Enterprise", "22H2");
    private static readonly Device Srv = NewDevice("SRV-WIN-01", OsFamily.Windows, "Windows Server 2022", "21H2");
    private static readonly Device Lx = NewDevice("lx-srv-01", OsFamily.Linux, "Ubuntu", "24.04");

    private static List<string> Names(GroupMatchMode mode, params GroupRule[] rules) =>
        new[] { Pc1, Pc2, Srv, Lx }
            .Where(GroupFilter.Build(mode, rules).Compile())
            .Select(d => d.Name)
            .ToList();

    [Fact]
    public void StartsWith_ignores_case()
    {
        var result = Names(GroupMatchMode.All, GroupRule.Create(GroupField.Name, MatchOperator.StartsWith, "pc-"));
        Assert.Equal(Arr("PC-0001", "PC-0002"), result);
    }

    [Fact]
    public void EndsWith_and_Contains_and_Is()
    {
        Assert.Equal(Arr("SRV-WIN-01", "lx-srv-01"),
            Names(GroupMatchMode.All, GroupRule.Create(GroupField.Name, MatchOperator.EndsWith, "-01")));
        Assert.Equal(Arr("SRV-WIN-01", "lx-srv-01"),
            Names(GroupMatchMode.All, GroupRule.Create(GroupField.Name, MatchOperator.Contains, "SRV")));
        Assert.Equal(Arr("PC-0002"),
            Names(GroupMatchMode.All, GroupRule.Create(GroupField.Name, MatchOperator.Is, "pc-0002")));
    }

    [Fact]
    public void All_requires_every_rule()
    {
        var result = Names(GroupMatchMode.All,
            GroupRule.Create(GroupField.OsFamily, MatchOperator.Is, "Windows"),
            GroupRule.Create(GroupField.OsName, MatchOperator.Contains, "windows 11"));
        Assert.Equal(Arr("PC-0001"), result);
    }

    [Fact]
    public void Any_requires_one_rule()
    {
        var result = Names(GroupMatchMode.Any,
            GroupRule.Create(GroupField.Name, MatchOperator.StartsWith, "SRV"),
            GroupRule.Create(GroupField.Name, MatchOperator.Contains, "-srv-"));
        Assert.Equal(Arr("SRV-WIN-01", "lx-srv-01"), result);
    }

    [Fact]
    public void OsFamily_rule_compares_the_enum()
    {
        Assert.Equal(Arr("lx-srv-01"), Names(GroupMatchMode.All, GroupRule.Create(GroupField.OsFamily, MatchOperator.Is, "Linux")));
    }

    [Fact]
    public void Organizational_unit_and_version_rules()
    {
        Assert.Equal(Arr("PC-0001"), Names(GroupMatchMode.All, GroupRule.Create(GroupField.OrgUnit, MatchOperator.Is, "finanz")));
        Assert.Equal(Arr("PC-0002"), Names(GroupMatchMode.All, GroupRule.Create(GroupField.OsVersion, MatchOperator.StartsWith, "22")));
    }

    [Fact]
    public void Group_without_rules_contains_no_devices()
    {
        Assert.Empty(Names(GroupMatchMode.All));
        Assert.Empty(Names(GroupMatchMode.Any));
    }
}

public class CatchAllGroupTests
{
    private static Device NewDevice(string name, OsFamily family, string? user = null)
    {
        var device = Device.Register(name, family, family == OsFamily.Windows ? "Windows 11 Pro" : "Ubuntu", "1");
        device.UpdateAssignment(user, null, null);
        return device;
    }

    private static readonly Device[] Devices =
    [
        NewDevice("PC-0001", OsFamily.Windows, "anna"),
        NewDevice("PC-0002", OsFamily.Windows),
        NewDevice("SRV-01", OsFamily.Windows),
        NewDevice("lx-01", OsFamily.Linux),
    ];

    private static DeviceGroup Group(string name, params GroupRule[] rules) =>
        DeviceGroup.Create(name, null, GroupMatchMode.All, rules);

    private static List<string> Members(DeviceGroup group, params DeviceGroup[] all) =>
        Devices.Where(GroupFilter.For(group, all).Compile()).Select(d => d.Name).ToList();

    [Fact]
    public void Catch_all_contains_devices_in_no_other_group()
    {
        var servers = Group("Server", GroupRule.Create(GroupField.Name, MatchOperator.StartsWith, "SRV"));
        var anna = Group("Anna", GroupRule.Create(GroupField.PrimaryUser, MatchOperator.Is, "anna"));
        var rest = DeviceGroup.Create("Rest", null, GroupMatchMode.All, [], isCatchAll: true);

        // PC-0002 and lx-01 have no primary user: the PrimaryUser rule must not drop them from the catch-all group.
        Assert.Equal(new[] { "PC-0002", "lx-01" }, Members(rest, servers, anna, rest));
    }

    [Fact]
    public void Groups_that_do_not_count_are_ignored()
    {
        var windows = DeviceGroup.Create("All Windows", null, GroupMatchMode.All,
            [GroupRule.Create(GroupField.OsFamily, MatchOperator.Is, "Windows")], countsForCatchAll: false);
        var servers = Group("Server", GroupRule.Create(GroupField.Name, MatchOperator.StartsWith, "SRV"));
        var rest = DeviceGroup.Create("Rest", null, GroupMatchMode.All, [], isCatchAll: true);

        Assert.Equal(new[] { "PC-0001", "PC-0002", "lx-01" }, Members(rest, windows, servers, rest));
    }

    [Fact]
    public void Own_rules_narrow_the_catch_all_group_and_other_catch_alls_do_not_count()
    {
        var servers = Group("Server", GroupRule.Create(GroupField.Name, MatchOperator.StartsWith, "SRV"));
        var restWindows = DeviceGroup.Create("Rest Windows", null, GroupMatchMode.All,
            [GroupRule.Create(GroupField.OsFamily, MatchOperator.Is, "Windows")], isCatchAll: true);
        var restAll = DeviceGroup.Create("Rest", null, GroupMatchMode.All, [], isCatchAll: true);

        Assert.Equal(new[] { "PC-0001", "PC-0002" }, Members(restWindows, servers, restWindows, restAll));
        Assert.Equal(new[] { "PC-0001", "PC-0002", "lx-01" }, Members(restAll, servers, restWindows, restAll));
    }

    [Fact]
    public void Without_other_groups_the_catch_all_contains_everything()
    {
        var rest = DeviceGroup.Create("Rest", null, GroupMatchMode.All, [], isCatchAll: true);
        Assert.Equal(4, Members(rest, rest).Count);
    }

    [Fact]
    public void Normal_groups_are_unchanged() =>
        Assert.Equal(new[] { "SRV-01" }, Members(Group("Server", GroupRule.Create(GroupField.Name, MatchOperator.StartsWith, "srv"))));
}
