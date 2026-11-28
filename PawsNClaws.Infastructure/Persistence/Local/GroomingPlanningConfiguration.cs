using Microsoft.EntityFrameworkCore;
using PawsNClaws.Domain.Entities;

namespace PawsNClaws.Infastructure.Persistence.Local;

internal static class GroomingPlanningConfiguration
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<GroomingPackage>(e =>
        {
            e.ToTable("GroomingPackages");
            e.Property(x => x.Version).IsConcurrencyToken();
            e.Property(x => x.Name).HasMaxLength(120);
            e.Property(x => x.Description).HasMaxLength(500);
            e.HasMany(x => x.Services).WithOne().HasForeignKey(x => x.PackageId);
        });
        model.Entity<GroomingPackageService>(e =>
        {
            e.ToTable("GroomingPackageServices");
            e.HasKey(x => new { x.PackageId, x.ServiceId });
            e.HasOne(x => x.Service).WithMany().HasForeignKey(x => x.ServiceId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<GroomingPriceRule>(e =>
        {
            e.ToTable("GroomingPriceRules", table =>
            {
                table.HasCheckConstraint("CK_GroomingPriceRules_Target", "([ServiceId] IS NULL AND [PackageId] IS NOT NULL) OR ([ServiceId] IS NOT NULL AND [PackageId] IS NULL)");
                table.HasCheckConstraint("CK_GroomingPriceRules_Amount", "[Amount] >= 0");
            });
            e.Property(x => x.Version).IsConcurrencyToken();
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.Property(x => x.Species).HasMaxLength(80);
            e.Property(x => x.Breed).HasMaxLength(100);
            e.Property(x => x.Size).HasMaxLength(60);
            e.Property(x => x.Condition).HasMaxLength(100);
            e.HasOne<GroomingService>().WithMany().HasForeignKey(x => x.ServiceId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<GroomingPackage>().WithMany().HasForeignKey(x => x.PackageId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<GroomingRecipe>(e =>
        {
            e.ToTable("GroomingRecipes", table => table.HasCheckConstraint("CK_GroomingRecipes_Quantity", "[Quantity] > 0"));
            e.Property(x => x.Version).IsConcurrencyToken();
            e.Property(x => x.Quantity).HasPrecision(18, 3);
            e.Property(x => x.Unit).HasMaxLength(20);
            e.HasIndex(x => new { x.ServiceId, x.ProductId }).IsUnique();
            e.HasOne<GroomingService>().WithMany().HasForeignKey(x => x.ServiceId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<GroomingQuote>(e =>
        {
            e.ToTable("GroomingQuotes");
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.LatestRevision).IsConcurrencyToken();
            e.HasOne<PetRecord>().WithMany().HasForeignKey(x => x.PetId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<GroomingQuoteRevision>(e =>
        {
            e.ToTable("GroomingQuoteRevisions");
            e.HasKey(x => new { x.QuoteId, x.Number });
            e.HasOne<GroomingQuote>().WithMany().HasForeignKey(x => x.QuoteId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<GroomingVisit>(e =>
        {
            e.ToTable("GroomingVisits");
            e.Property(x => x.RevisionToken).IsConcurrencyToken();
            e.HasIndex(x => x.EstimateId).IsUnique();
            e.HasOne<GroomingQuoteRevision>().WithMany()
                .HasForeignKey(x => new { x.EstimateId, x.EstimateVersion }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<PawsNClaws.Infastructure.Identity.Local.LocalApplicationUser>().WithMany()
                .HasForeignKey(x => x.StartedByEmployeeId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<GroomingCheckIn>(e =>
        {
            e.ToTable("GroomingCheckIns", table => table.HasCheckConstraint(
                "CK_GroomingCheckIns_WaiverSize", "DATALENGTH([WaiverBytes]) BETWEEN 1 AND 5242880"));
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.VisitId).IsUnique().HasFilter("[CancelledAtUtc] IS NULL");
            e.HasOne<GroomingVisit>().WithMany().HasForeignKey(x => x.VisitId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<PawsNClaws.Infastructure.Identity.Local.LocalApplicationUser>().WithMany()
                .HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.WaiverContentType).HasMaxLength(32);
            e.Property(x => x.WaiverSha256).HasMaxLength(64);
            e.Property(x => x.CancellationReason).HasMaxLength(500);
        });
    }
}
