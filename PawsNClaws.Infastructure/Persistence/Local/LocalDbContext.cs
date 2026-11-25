
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

    public DbSet<InventoryStock> InventoryStocks
        => Set<InventoryStock>();

    public DbSet<InventoryMovement> InventoryMovements
        => Set<InventoryMovement>();

    public DbSet<InventoryMovementEvidence> InventoryMovementEvidence
        => Set<InventoryMovementEvidence>();


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

            entity.HasIndex(p => p.Name)
                .IsUnique()
                .HasDatabaseName("UX_Products_Name");

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


        builder.Entity<InventoryStock>(entity =>
        {
            entity.ToTable("InventoryStocks", table =>
            {
                table.HasCheckConstraint(
                    "CK_InventoryStocks_QuantityOnHand",
                    "[QuantityOnHand] >= 0");

                table.HasCheckConstraint(
                    "CK_InventoryStocks_ReorderLevel",
                    "[ReorderLevel] >= 0");
            });

            entity.HasKey(x => x.Id);

            entity.HasIndex(x => x.ProductId)
                .IsUnique()
                .HasDatabaseName("UX_InventoryStocks_ProductId");

            entity.HasOne(x => x.Product)
                .WithOne()
                .HasForeignKey<InventoryStock>(x => x.ProductId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.Property(x => x.QuantityOnHand)
                .IsRequired();

            entity.Property(x => x.ReorderLevel)
                .IsRequired();

            entity.Property(x => x.UpdatedAtUtc)
                .IsRequired();

            entity.Property(x => x.RowVersion)
                .IsRowVersion();
        });


        builder.Entity<InventoryMovement>(entity =>
        {
            entity.ToTable("InventoryMovements", table =>
            {
                table.HasCheckConstraint(
                    "CK_InventoryMovements_QuantityChange",
                    "[QuantityChange] <> 0");

                table.HasCheckConstraint(
                    "CK_InventoryMovements_QuantityBefore",
                    "[QuantityBefore] >= 0");

                table.HasCheckConstraint(
                    "CK_InventoryMovements_QuantityAfter",
                    "[QuantityAfter] >= 0");

                table.HasCheckConstraint(
                    "CK_InventoryMovements_QuantityBalance",
                    "CAST([QuantityAfter] AS bigint) = " +
                    "CAST([QuantityBefore] AS bigint) + [QuantityChange]");

                table.HasCheckConstraint(
                    "CK_InventoryMovements_MovementType",
                    "[MovementType] IN " +
                    "('StockIn', 'StockOut', 'Adjustment', " +
                    "'Sale', 'SaleReversal')");

                table.HasCheckConstraint(
                    "CK_InventoryMovements_Direction",
                    "([MovementType] IN ('StockIn', 'SaleReversal') " +
                    "AND [QuantityChange] > 0) OR " +
                    "([MovementType] IN ('StockOut', 'Sale') " +
                    "AND [QuantityChange] < 0) OR " +
                    "([MovementType] = 'Adjustment')");

                table.HasCheckConstraint(
                    "CK_InventoryMovements_StockOutReason",
                    """
            (
                [MovementType] = 'StockOut'
                AND [StockOutReason] IS NOT NULL
                AND [StockOutReason] IN
                (
                    'Defective',
                    'Expired',
                    'DamagedUponDelivery',
                    'TornPackaging',
                    'DamagedInStore',
                    'Other'
                )
                AND [Reason] IS NOT NULL
                AND LEN(LTRIM(RTRIM([Reason]))) > 0
            )
            OR
            (
                [MovementType] <> 'StockOut'
                AND [StockOutReason] IS NULL
            )
            """);
            });

            entity.HasKey(x => x.Id);

            entity.Property(x => x.MovementType)
                .HasConversion<string>()
                .HasMaxLength(30)
                .IsRequired();

            entity.Property(x => x.StockOutReason)
                .HasConversion<string>()
                .HasMaxLength(30);

            entity.Property(x => x.Reason)
                .HasMaxLength(500);

            entity.Property(x => x.CreatedAtUtc)
                .IsRequired();

            entity.HasIndex(x => x.OperationId)
                .IsUnique()
                .HasDatabaseName("UX_InventoryMovements_OperationId");

            entity.HasIndex(x => new
            {
                x.ProductId,
                x.CreatedAtUtc
            });

            entity.HasIndex(x => x.PerformedByUserId);

            entity.HasOne(x => x.Product)
                .WithMany()
                .HasForeignKey(x => x.ProductId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne<LocalApplicationUser>()
                .WithMany()
                .HasForeignKey(x => x.PerformedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });


        builder.Entity<InventoryMovementEvidence>(entity =>
        {
            entity.ToTable("InventoryMovementEvidence", table =>
            {
                table.HasCheckConstraint(
                    "CK_InventoryMovementEvidence_FileSize",
                    "[FileSizeBytes] > 0 AND " +
                    "[FileSizeBytes] <= 5242880");

                table.HasCheckConstraint(
                    "CK_InventoryMovementEvidence_ContentType",
                    "[ContentType] IN ('image/jpeg', 'image/png')");

                table.HasCheckConstraint(
                    "CK_InventoryMovementEvidence_HashLength",
                    "LEN([Sha256Hash]) = 64");
            });

            entity.HasKey(x => x.Id);

            entity.Property(x => x.OriginalFileName)
                .HasMaxLength(255)
                .IsRequired();

            entity.Property(x => x.StorageKey)
                .HasMaxLength(500)
                .IsRequired();

            entity.Property(x => x.ContentType)
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(x => x.Sha256Hash)
                .HasMaxLength(64)
                .IsUnicode(false)
                .IsRequired();

            entity.Property(x => x.UploadedAtUtc)
                .IsRequired();

            entity.HasIndex(x => x.StorageKey)
                .IsUnique()
                .HasDatabaseName("UX_InventoryMovementEvidence_StorageKey");

            entity.HasOne(x => x.InventoryMovement)
                .WithMany(x => x.Evidence)
                .HasForeignKey(x => x.InventoryMovementId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne<LocalApplicationUser>()
                .WithMany()
                .HasForeignKey(x => x.UploadedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });



    }


}
