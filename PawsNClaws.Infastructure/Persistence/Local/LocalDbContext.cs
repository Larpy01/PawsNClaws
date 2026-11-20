
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using PawsNClaws.Domain.Entities;
using PawsNClaws.Infastructure.Identity.Local;

namespace PawsNClaws.Infastructure.Persistence.Local;

public class LocalDbContext
    : IdentityDbContext<
        LocalApplicationUser,
        IdentityRole<Guid>,
        Guid>
{
    public LocalDbContext(
        DbContextOptions<LocalDbContext> options)
        : base(options)
    {
    }
    public DbSet<EmployeeAuditLog> EmployeeAuditLogs
    => Set<EmployeeAuditLog>();

    public DbSet<Product> Products => Set<Product>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Product>(entity =>
        {
            entity.HasKey(p => p.Id);

            entity.Property(p => p.SKU)
                .HasMaxLength(50)
                .IsRequired();

            entity.HasIndex(p => p.SKU)
                .IsUnique();

            entity.Property(p => p.Name)
                .HasMaxLength(200)
                .IsRequired();

            entity.Property(p => p.Price)
                .HasPrecision(18, 2);
        });

        builder.Entity<LocalApplicationUser>(entity =>
        {
            entity.Property(u => u.FirstName)
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(u => u.LastName)
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(u => u.IsActive)
                .HasDefaultValue(true);
        });


        builder.Entity<EmployeeAuditLog>(entity =>
        {
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Action)
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(x => x.OldValue)
                .HasMaxLength(256);

            entity.Property(x => x.NewValue)
                .HasMaxLength(256);

            entity.HasIndex(x => x.TargetUserId);
            entity.HasIndex(x => x.CreatedAtUtc);
        });


    }


}
