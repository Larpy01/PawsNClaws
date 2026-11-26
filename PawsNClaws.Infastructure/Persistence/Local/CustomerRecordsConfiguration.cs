using Microsoft.EntityFrameworkCore;
using PawsNClaws.Domain.Entities;

namespace PawsNClaws.Infastructure.Persistence.Local;

internal static class CustomerRecordsConfiguration
{
    internal static void Configure(ModelBuilder model)
    {
        model.Entity<CustomerRecord>(entity =>
        {
            entity.ToTable("CustomerRecords");
            entity.Property(x => x.Version).IsConcurrencyToken();
            entity.Property(x => x.FirstName).HasMaxLength(100);
            entity.Property(x => x.LastName).HasMaxLength(100);
            entity.Property(x => x.Phone).HasMaxLength(40);
            entity.Property(x => x.Email).HasMaxLength(256);
            entity.Property(x => x.Address).HasMaxLength(400);
            entity.HasIndex(x => new { x.LastName, x.FirstName });
        });
        model.Entity<PetRecord>(entity =>
        {
            entity.ToTable("PetRecords");
            entity.Property(x => x.Version).IsConcurrencyToken();
            entity.Property(x => x.Name).HasMaxLength(100);
            entity.Property(x => x.Species).HasMaxLength(80);
            entity.Property(x => x.Breed).HasMaxLength(100);
            entity.Property(x => x.Sex).HasMaxLength(30);
            entity.Property(x => x.Size).HasMaxLength(60);
            entity.Property(x => x.VaccinationStatus).HasMaxLength(100);
            entity.Property(x => x.Allergies).HasMaxLength(500);
            entity.Property(x => x.MedicalNotes).HasMaxLength(1000);
            entity.Property(x => x.BehaviorNotes).HasMaxLength(1000);
            entity.Property(x => x.SpecialInstructions).HasMaxLength(1000);
            entity.Property(x => x.WeightKg).HasPrecision(8, 3);
            entity.HasOne<CustomerRecord>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.CustomerId, x.Name });
        });
        model.Entity<ProfileChange>(entity =>
        {
            entity.ToTable("ProfileChanges");
            entity.Property(x => x.Action).HasMaxLength(40);
            entity.HasIndex(x => x.RecordVersion).IsUnique();
            entity.HasOne<CustomerRecord>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<PetRecord>().WithMany().HasForeignKey(x => x.PetId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
