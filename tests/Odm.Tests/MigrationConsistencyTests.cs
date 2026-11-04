using Microsoft.EntityFrameworkCore;
using Odm.Modules.Agents.Infrastructure;
using Odm.Modules.Audit.Infrastructure;
using Odm.Modules.Devices.Infrastructure;
using Odm.Modules.Identity.Infrastructure;
using Odm.Modules.Notifications.Infrastructure;
using Odm.Modules.OsUpdates.Infrastructure;
using Odm.Modules.Software.Infrastructure;
using Odm.SharedKernel.Persistence;
using Xunit;

namespace Odm.Tests;

/// <summary>
/// The model snapshot of every module must match its model, for both providers. Otherwise EF Core refuses to migrate at
/// startup (pending model changes). No database connection is needed for the check.
/// </summary>
public class MigrationConsistencyTests
{
    public static TheoryData<DatabaseProvider> Providers => new() { DatabaseProvider.Postgres, DatabaseProvider.SqlServer };

    private static DbContextOptions<T> Options<T>(DatabaseProvider provider, string schema) where T : DbContext
    {
        var connection = provider == DatabaseProvider.Postgres ? "Host=localhost;Database=odm" : "Server=localhost;Database=odm";
        var builder = new DbContextOptionsBuilder<T>();
        builder.UseOdmDatabase(new DatabaseOptions { Provider = provider, ConnectionString = connection }, schema);
        return builder.Options;
    }

    private static IEnumerable<DbContext> Contexts(DatabaseProvider p) =>
    [
        new IdentityDbContext(Options<IdentityDbContext>(p, IdentityDbContext.Schema)),
        new AuditDbContext(Options<AuditDbContext>(p, AuditDbContext.Schema)),
        new DevicesDbContext(Options<DevicesDbContext>(p, DevicesDbContext.Schema)),
        new AgentsDbContext(Options<AgentsDbContext>(p, AgentsDbContext.Schema)),
        new SoftwareDbContext(Options<SoftwareDbContext>(p, SoftwareDbContext.Schema)),
        new NotificationsDbContext(Options<NotificationsDbContext>(p, NotificationsDbContext.Schema)),
        new OsUpdatesDbContext(Options<OsUpdatesDbContext>(p, OsUpdatesDbContext.Schema)),
    ];

    [Theory]
    [MemberData(nameof(Providers))]
    public void Every_module_has_no_pending_model_changes(DatabaseProvider provider)
    {
        var pending = new List<string>();
        foreach (var context in Contexts(provider))
        {
            using (context)
            {
                if (context.Database.HasPendingModelChanges())
                {
                    pending.Add(context.GetType().Name);
                }
            }
        }

        Assert.Empty(pending);
    }
}
