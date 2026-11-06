using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Odm.Modules.Identity.Domain;
using Odm.SharedKernel.Persistence;

namespace Odm.Modules.Identity.Infrastructure;

public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options) : DbContext(options)
{
    public const string Schema = "identity";

    public DbSet<User> Users => Set<User>();
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<User>(b =>
        {
            b.ToTable("users");
            b.HasKey(u => u.Id);
            b.Property(u => u.UserName).HasMaxLength(128).IsRequired();
            b.HasIndex(u => u.UserName).IsUnique();
            b.Property(u => u.DisplayName).HasMaxLength(256).IsRequired();
            b.Property(u => u.PasswordHash).HasMaxLength(512).IsRequired();
            b.Property(u => u.Role).HasConversion<string>().HasMaxLength(32);
        });

        modelBuilder.Entity<ApiKey>(b =>
        {
            b.ToTable("api_keys");
            b.HasKey(k => k.Id);
            b.Property(k => k.Name).HasMaxLength(ApiKey.MaxNameLength).IsRequired();
            b.HasIndex(k => k.Name).IsUnique();
            b.Property(k => k.SecretHash).HasMaxLength(64).IsRequired();
            b.Property(k => k.CreatedBy).HasMaxLength(128).IsRequired();
        });
    }
}

/// <summary>Allows 'dotnet ef migrations add' without a running host.</summary>
public sealed class IdentityDbContextFactory : IDesignTimeDbContextFactory<IdentityDbContext>
{
    public IdentityDbContext CreateDbContext(string[] args) =>
        new(DatabaseExtensions.CreateDesignTimeOptions<IdentityDbContext>(IdentityDbContext.Schema));
}
