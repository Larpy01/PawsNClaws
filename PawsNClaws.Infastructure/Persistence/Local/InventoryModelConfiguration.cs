using Microsoft.EntityFrameworkCore;
using PawsNClaws.Domain.Entities;
using PawsNClaws.Infastructure.Identity.Local;

namespace PawsNClaws.Infastructure.Persistence.Local;

internal static class InventoryModelConfiguration
{
    internal static void Configure(ModelBuilder builder)
    {
        builder.Entity<Product>(entity =>
        {
            entity.Property(product => product.Unit).HasMaxLength(16).HasDefaultValue("Each");
            entity.Property(product => product.IsSellable).HasDefaultValue(true).HasSentinel(true);
            entity.Property(product => product.Category).HasMaxLength(80);
            entity.Property(product => product.Barcode).HasMaxLength(64);
            entity.Property(product => product.Description).HasMaxLength(500);
            entity.Property(product => product.UnitCost).HasPrecision(18, 2);
            entity.HasIndex(product => product.Barcode).IsUnique();
        });

        builder.Entity<InventoryStock>(entity =>
        {
            entity.Property(stock => stock.QuantityOnHand).HasPrecision(18, 3);
            entity.Property(stock => stock.ReorderLevel).HasPrecision(18, 3);
        });
        builder.Entity<InventoryMovement>(entity =>
        {
            entity.Property(movement => movement.QuantityBefore).HasPrecision(18, 3);
            entity.Property(movement => movement.QuantityChange).HasPrecision(18, 3);
            entity.Property(movement => movement.QuantityAfter).HasPrecision(18, 3);
            entity.HasIndex(movement => movement.ReversesMovementId).IsUnique();
            entity.HasOne<InventoryMovement>().WithMany()
                .HasForeignKey(movement => movement.ReversesMovementId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(movement => new { movement.GroomingVisitId, movement.ProductId }).IsUnique()
                .HasFilter("[GroomingVisitId] IS NOT NULL");
            entity.HasOne<GroomingVisit>().WithMany()
                .HasForeignKey(movement => movement.GroomingVisitId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(movement => movement.UsageEstimateId).IsUnique();
            entity.HasOne<GroomingUsageEstimate>().WithMany()
                .HasForeignKey(movement => movement.UsageEstimateId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<StockOutRequest>(entity =>
        {
            entity.Property(request => request.Quantity).HasPrecision(18, 3);
            entity.HasOne<InventoryBatch>().WithMany()
                .HasForeignKey(request => new { request.BatchId, request.ProductId })
                .HasPrincipalKey(batch => new { batch.Id, batch.ProductId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<InventoryBatch>(entity =>
        {
            entity.ToTable("InventoryBatches", table => table.HasCheckConstraint(
                "CK_InventoryBatches_Balance", "[Quarantined] >= 0 AND [OnHand] >= [Quarantined]"));
            entity.Property(batch => batch.Lot).HasMaxLength(100);
            entity.Property(batch => batch.OnHand).HasPrecision(18, 3);
            entity.Property(batch => batch.Quarantined).HasPrecision(18, 3);
            entity.Property(batch => batch.RowVersion).IsRowVersion();
            entity.HasIndex(batch => new { batch.ProductId, batch.ExpiryDate, batch.ReceivedAtUtc });
            entity.HasOne<Product>().WithMany().HasForeignKey(batch => batch.ProductId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<InventoryMovementAllocation>(entity =>
        {
            entity.ToTable("InventoryMovementAllocations", table => table.HasCheckConstraint(
                "CK_InventoryMovementAllocations_Quantity", "[QuantityChange] <> 0"));
            entity.HasKey(allocation => new { allocation.MovementId, allocation.BatchId });
            entity.Property(allocation => allocation.QuantityChange).HasPrecision(18, 3);
            entity.HasOne<InventoryMovement>().WithMany(movement => movement.Allocations)
                .HasForeignKey(allocation => allocation.MovementId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<InventoryBatch>().WithMany().HasForeignKey(allocation => allocation.BatchId)
                .OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<InventoryQuarantineEvent>(entity =>
        {
            entity.ToTable("InventoryQuarantineEvents", table => table.HasCheckConstraint(
                "CK_InventoryQuarantineEvents_Quantity", "[QuantityChange] <> 0"));
            entity.HasKey(entry => entry.OperationId);
            entity.Property(entry => entry.QuantityChange).HasPrecision(18, 3);
            entity.Property(entry => entry.Reason).HasMaxLength(500);
            entity.HasOne<InventoryBatch>().WithMany().HasForeignKey(entry => entry.BatchId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<LocalApplicationUser>().WithMany().HasForeignKey(entry => entry.ActorId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<StockReceipt>(entity =>
        {
            entity.ToTable("StockReceipts", table =>
            {
                table.HasCheckConstraint("CK_StockReceipts_Quantities",
                    "[Delivered] > 0 AND [Accepted] >= 0 AND [Rejected] >= 0 AND [Delivered] = [Accepted] + [Rejected]");
                table.HasCheckConstraint("CK_StockReceipts_SupplierInvoiceVat",
                    "[SupplierInvoiceVat] IS NULL OR ([SupplierInvoiceVat] >= 0 AND [Accepted] > 0 AND " +
                    "[Reference] IS NOT NULL AND LEN([Reference]) > 0)");
            });
            entity.Property(receipt => receipt.Supplier).HasMaxLength(150);
            entity.Property(receipt => receipt.Reference).HasMaxLength(100);
            entity.Property(receipt => receipt.Lot).HasMaxLength(100);
            entity.Property(receipt => receipt.RejectionReason).HasMaxLength(500);
            entity.Property(receipt => receipt.Delivered).HasPrecision(18, 3);
            entity.Property(receipt => receipt.Accepted).HasPrecision(18, 3);
            entity.Property(receipt => receipt.Rejected).HasPrecision(18, 3);
            entity.Property(receipt => receipt.UnitCost).HasPrecision(18, 2);
            entity.Property(receipt => receipt.SupplierInvoiceVat).HasPrecision(18, 2);
            entity.HasOne<Product>().WithMany().HasForeignKey(receipt => receipt.ProductId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<InventoryBatch>().WithMany()
                .HasForeignKey(receipt => new { receipt.BatchId, receipt.ProductId })
                .HasPrincipalKey(batch => new { batch.Id, batch.ProductId }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<LocalApplicationUser>().WithMany().HasForeignKey(receipt => receipt.ActorId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<InventorySupplier>().WithMany().HasForeignKey(receipt => receipt.SupplierId)
                .OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<InventorySupplier>(entity =>
        {
            entity.ToTable("InventorySuppliers");
            entity.Property(supplier => supplier.Name).HasMaxLength(150);
            entity.Property(supplier => supplier.NormalizedName).HasMaxLength(150);
            entity.HasIndex(supplier => supplier.NormalizedName).IsUnique();
        });
        builder.Entity<GroomingUsageEstimate>(entity =>
        {
            entity.ToTable("GroomingUsageEstimates", table => table.HasCheckConstraint(
                "CK_GroomingUsageEstimates_Quantity", "[Quantity] > 0"));
            entity.Property(estimate => estimate.JobReference).HasMaxLength(100);
            entity.Property(estimate => estimate.Quantity).HasPrecision(18, 3);
            entity.HasIndex(estimate => new { estimate.ProductId, estimate.JobReference }).IsUnique();
            entity.HasOne<Product>().WithMany().HasForeignKey(estimate => estimate.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<LocalApplicationUser>().WithMany().HasForeignKey(estimate => estimate.ActorId)
                .OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<StockReceiptEvidence>(entity =>
        {
            entity.ToTable("StockReceiptEvidence");
            entity.Property(evidence => evidence.StorageKey).HasMaxLength(500);
            entity.Property(evidence => evidence.ContentType).HasMaxLength(100);
            entity.Property(evidence => evidence.Sha256Hash).HasMaxLength(64).IsUnicode(false);
            entity.HasIndex(evidence => evidence.StorageKey).IsUnique();
            entity.HasOne<StockReceipt>().WithMany(receipt => receipt.Evidence)
                .HasForeignKey(evidence => evidence.StockReceiptId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
