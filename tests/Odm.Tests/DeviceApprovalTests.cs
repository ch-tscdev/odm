using Odm.Modules.Devices.Domain;
using Xunit;

namespace Odm.Tests;

public class DeviceApprovalTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 10, 0, 0, TimeSpan.Zero);

    private static Device NewDevice() => Device.Register("PC-0001", OsFamily.Windows, "Windows 11 Pro", "10.0.26100");

    [Fact]
    public void Reinstall_is_only_possible_after_it_was_allowed()
    {
        var device = NewDevice();
        Assert.False(device.TryUseReinstall(Now));

        device.AllowReinstall(Now.AddHours(24));
        Assert.True(device.ReinstallAllowed(Now));
        Assert.True(device.TryUseReinstall(Now.AddHours(1)));
    }

    [Fact]
    public void Allowed_reinstall_can_be_used_once()
    {
        var device = NewDevice();
        device.AllowReinstall(Now.AddHours(24));

        Assert.True(device.TryUseReinstall(Now));
        Assert.False(device.TryUseReinstall(Now));
        Assert.Null(device.ReinstallAllowedUntil);
    }

    [Fact]
    public void Allowed_reinstall_expires_and_can_be_cancelled()
    {
        var expired = NewDevice();
        expired.AllowReinstall(Now.AddHours(24));
        Assert.False(expired.TryUseReinstall(Now.AddHours(24)));

        var cancelled = NewDevice();
        cancelled.AllowReinstall(Now.AddHours(24));
        cancelled.CancelReinstall();
        Assert.False(cancelled.TryUseReinstall(Now));
    }

    [Fact]
    public void Device_registered_under_an_existing_name_waits_for_approval()
    {
        var device = Device.Register("PC-0001", OsFamily.Windows, "Windows 11 Pro", "10.0", approvalPending: true);
        Assert.True(device.ApprovalPending);

        device.Approve();
        Assert.False(device.ApprovalPending);
        Assert.False(NewDevice().ApprovalPending);
    }

    [Fact]
    public void Reported_host_name_does_not_rename_the_device()
    {
        var device = NewDevice();
        device.ReportHostname("SRV-FINANCE-99");

        Assert.Equal("PC-0001", device.Name);
        Assert.Equal("SRV-FINANCE-99", device.ReportedName);
    }

    [Fact]
    public void Operator_adopts_the_reported_name()
    {
        var device = NewDevice();
        device.ReportHostname("PC-0042");
        device.AcceptReportedName();

        Assert.Equal("PC-0042", device.Name);
        Assert.Null(device.ReportedName);
    }

    [Fact]
    public void Reporting_the_current_name_again_clears_the_pending_rename()
    {
        var device = NewDevice();
        device.ReportHostname("PC-0042");
        device.ReportHostname("PC-0001");
        Assert.Null(device.ReportedName);

        device.ReportHostname("   ");
        Assert.Null(device.ReportedName);
    }
}
