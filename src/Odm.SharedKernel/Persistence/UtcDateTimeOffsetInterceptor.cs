using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Odm.SharedKernel.Persistence;

/// <summary>
/// Converts every <see cref="DateTimeOffset"/> of added or modified entities to UTC before saving.
/// PostgreSQL (Npgsql) rejects other offsets for <c>timestamp with time zone</c>, and values reported by agents may carry
/// the device's local offset (e.g. update times read from <c>dpkg.log</c>). On SQL Server it keeps stored values uniform.
/// </summary>
public sealed class UtcDateTimeOffsetInterceptor : SaveChangesInterceptor
{
    public static readonly UtcDateTimeOffsetInterceptor Instance = new();

    private UtcDateTimeOffsetInterceptor() { }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Normalize(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Normalize(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private static void Normalize(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified))
            {
                continue;
            }

            foreach (var property in entry.Properties)
            {
                if (ToUtc(property.CurrentValue) is { } utc)
                {
                    property.CurrentValue = utc;
                }
            }
        }
    }

    /// <summary>The value in UTC if it is a <see cref="DateTimeOffset"/> with a different offset; otherwise null (nothing to change).</summary>
    public static DateTimeOffset? ToUtc(object? value) =>
        value is DateTimeOffset time && time.Offset != TimeSpan.Zero ? time.ToUniversalTime() : null;
}
