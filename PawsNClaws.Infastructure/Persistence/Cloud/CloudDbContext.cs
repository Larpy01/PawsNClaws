
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using PawsNClaws.Domain.Entities;
using PawsNClaws.Infastructure.Identity.Cloud;

namespace PawsNClaws.Infastructure.Persistence.Cloud;

public class CloudDbContext
    : IdentityDbContext<
        CloudApplicationUser,
        IdentityRole<Guid>,
        Guid>
{
    public CloudDbContext(
        DbContextOptions<CloudDbContext> options)
        : base(options)
    {
    }

    public DbSet<Customer> Customers => Set<Customer>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Customer>(entity =>
        {
            entity.HasKey(c => c.Id);

            entity.Property(c => c.FirstName)
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(c => c.LastName)
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(c => c.Email)
                .HasMaxLength(256)
                .IsRequired();

            entity.HasIndex(c => c.Email)
                .IsUnique();
        });

        builder.Entity<CloudApplicationUser>(entity =>
        {
            entity.HasOne<Customer>()
                .WithOne()
                .HasForeignKey<CloudApplicationUser>(
                    u => u.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(u => u.CustomerId)
                .IsUnique();
        });
    }
}
