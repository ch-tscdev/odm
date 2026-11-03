using Odm.SharedKernel.Persistence;
using Xunit;

namespace Odm.Tests;

public class UtcTimestampTests
{
    [Fact]
    public void Local_offset_is_converted_to_utc()
    {
        var local = new DateTimeOffset(2026, 11, 2, 10, 27, 0, TimeSpan.FromHours(1));
        var utc = UtcDateTimeOffsetInterceptor.ToUtc(local);
        Assert.NotNull(utc);
        Assert.Equal(TimeSpan.Zero, utc.Value.Offset);
        Assert.Equal(local, utc.Value); // same instant
        Assert.Equal(9, utc.Value.Hour);
    }

    [Fact]
    public void Utc_values_and_other_types_are_left_alone()
    {
        Assert.Null(UtcDateTimeOffsetInterceptor.ToUtc(DateTimeOffset.UtcNow));
        Assert.Null(UtcDateTimeOffsetInterceptor.ToUtc(null));
        Assert.Null(UtcDateTimeOffsetInterceptor.ToUtc("2026-11-02T10:27:00+01:00"));
        Assert.Null(UtcDateTimeOffsetInterceptor.ToUtc(DateTime.Now));
    }
}
