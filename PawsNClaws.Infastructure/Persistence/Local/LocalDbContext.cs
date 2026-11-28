
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
    public DbSet<GroomingService> GroomingServices => Set<GroomingService>();

    public DbSet<InventoryStock> InventoryStocks
        => Set<InventoryStock>();

    public DbSet<InventoryMovement> InventoryMovements
        => Set<InventoryMovement>();

    public DbSet<InventoryMovementEvidence> InventoryMovementEvidence
        => Set<InventoryMovementEvidence>();

    public DbSet<StockOutRequest> StockOutRequests => Set<StockOutRequest>();
    public DbSet<StockOutRequestEvidence> StockOutRequestEvidence => Set<StockOutRequestEvidence>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfiguration(new StockOutRequestConfiguration());
        builder.ApplyConfiguration(new StockOutRequestEvidenceConfiguration());
        InventoryModelConfiguration.Configure(builder);
        CustomerRecordsConfiguration.Configure(builder);
        GroomingPlanningConfiguration.Configure(builder);
        builder.Entity<SmsNotification>(notification =>
        {
            notification.HasKey(x => new { x.Kind, x.SourceId });
            notification.HasIndex(x => new { x.Status, x.NextAttemptUtc });
            notification.Property(x => x.Status).IsConcurrencyToken();
            notification.Property(x => x.NextAttemptUtc).IsConcurrencyToken();
            notification.Property(x => x.ProviderBatchId).HasMaxLength(80);
            notification.Property(x => x.Outcome).HasMaxLength(40);
        });
        builder.Entity<CashDrawerShift>(shift =>
        {
            shift.ToTable("CashDrawerShifts", table => table.HasCheckConstraint("CK_CashDrawerShifts_Amounts",
                "[OpeningCash] >= 0 AND (([ClosedAtUtc] IS NULL AND [CountedCash] IS NULL AND [ExpectedAtClose] IS NULL AND [ClosedByEmployeeId] IS NULL) OR " +
                "([ClosedAtUtc] IS NOT NULL AND [CountedCash] IS NOT NULL AND [CountedCash] >= 0 AND [ExpectedAtClose] IS NOT NULL AND [ExpectedAtClose] >= 0 AND [ClosedByEmployeeId] IS NOT NULL))"));
            shift.Property(x => x.OpeningCash).HasPrecision(18, 2);
            shift.Property(x => x.CountedCash).HasPrecision(18, 2);
            shift.Property(x => x.ExpectedAtClose).HasPrecision(18, 2);
            shift.HasIndex(x => x.ClosedAtUtc, "IX_CashDrawerShifts_OnlyOpenShift").IsUnique().HasFilter("[ClosedAtUtc] IS NULL");
            shift.HasIndex(x => x.ClosedAtUtc, "IX_CashDrawerShifts_ClosedHistory");
        });
        builder.Entity<CashDrawerEntry>(entry =>
        {
            entry.ToTable("CashDrawerEntries", table => table.HasCheckConstraint("CK_CashDrawerEntries_Amount", "[Amount] <> 0"));
            entry.Property(x => x.Amount).HasPrecision(18, 2);
            entry.Property(x => x.Reason).HasMaxLength(500);
            entry.HasIndex(x => new { x.Kind, x.ReferenceId }).IsUnique();
            entry.HasIndex(x => new { x.ShiftId, x.RecordedAtUtc });
            entry.HasOne<CashDrawerShift>().WithMany().HasForeignKey(x => x.ShiftId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<RetailSale>(sale =>
        {
            sale.ToTable("RetailSales", table =>
            {
                table.HasCheckConstraint("CK_RetailSales_Amounts", "[Total] >= 0 AND [Tendered] >= [Total]");
                table.HasCheckConstraint("CK_RetailSales_Payment",
                    "([PaymentMethod] = 0 AND [OnlinePaymentReference] IS NULL) OR " +
                    "([PaymentMethod] = 1 AND [Total] > 0 AND [Tendered] = [Total] AND " +
                    "[OnlinePaymentReference] IS NOT NULL AND LEN([OnlinePaymentReference]) > 0)");
                table.HasCheckConstraint("CK_RetailSales_Vat",
                    "([VatRate] IS NULL AND [VatAmount] IS NULL) OR " +
                    "([VatRate] IS NOT NULL AND [VatAmount] IS NOT NULL AND " +
                    "(([VatRate] = 0 AND [VatAmount] = 0) OR ([VatRate] > 0 AND [VatRate] <= 1 AND " +
                    "[VatAmount] = ROUND([Total] * [VatRate] / (1 + [VatRate]), 2))))");
            });
            sale.Property(x => x.Total).HasPrecision(18, 2);
            sale.Property(x => x.VatRate).HasPrecision(5, 4);
            sale.Property(x => x.VatAmount).HasPrecision(18, 2);
            sale.Property(x => x.VoidReason).HasMaxLength(500);
            sale.HasIndex(x => new { x.RecordedAtUtc, x.Id });
            sale.Property(x => x.Tendered).HasPrecision(18, 2);
            sale.Property(x => x.OnlinePaymentReference).HasMaxLength(80).IsUnicode(false);
            sale.HasIndex(x => x.OnlinePaymentReference).IsUnique().HasFilter("[OnlinePaymentReference] IS NOT NULL");
            sale.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.SaleId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<RetailSaleLine>(line =>
        {
            line.ToTable("RetailSaleLines", table => table.HasCheckConstraint("CK_RetailSaleLines_Amounts",
                "[Quantity] > 0 AND [UnitPrice] >= 0"));
            line.HasKey(x => new { x.SaleId, x.ProductId });
            line.Property(x => x.UnitPrice).HasPrecision(18, 2);
            line.HasOne<InventoryMovement>().WithOne().HasForeignKey<RetailSaleLine>(x => x.InventoryMovementId)
                .OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<GroomingBill>(bill =>
        {
            bill.ToTable("GroomingBills", table => table.HasCheckConstraint("CK_GroomingBills_Total", "[Total] >= 0"));
            bill.HasKey(x => x.VisitId);
            bill.Property(x => x.Total).HasPrecision(18, 2);
            bill.Property(x => x.HomeDepositAmount).HasPrecision(18, 2);
            bill.Property(x => x.HomeDepositReference).HasMaxLength(100);
            bill.HasOne<OffsiteBooking>().WithOne().HasForeignKey<GroomingBill>(x => x.HomeDepositBookingId)
                .OnDelete(DeleteBehavior.Restrict);
            bill.Property(x => x.Revision).IsConcurrencyToken();
            bill.HasIndex(x => x.IssuedAtUtc);
            bill.HasOne<GroomingVisit>().WithOne().HasForeignKey<GroomingBill>(x => x.VisitId)
                .OnDelete(DeleteBehavior.Restrict);
            bill.HasMany(x => x.Payments).WithOne().HasForeignKey(x => x.VisitId)
                .OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<GroomingCashPayment>(payment =>
        {
            payment.ToTable("GroomingCashPayments", table => table.HasCheckConstraint(
                "CK_GroomingCashPayments_Amounts", "[Amount] > 0 AND [Tendered] >= [Amount]"));
            payment.Property(x => x.Amount).HasPrecision(18, 2);
            payment.Property(x => x.Tendered).HasPrecision(18, 2);
        });

        builder.Entity<OffsiteBooking>(entry =>
        {
            entry.ToTable("OffsiteBookings");
            entry.Property(x => x.Revision).IsConcurrencyToken();
            entry.Property(x => x.Address).HasMaxLength(300);
            entry.Property(x => x.Phone).HasMaxLength(40);
            entry.Property(x => x.ReviewReason).HasMaxLength(500);
            entry.Property(x => x.DepositRequired).HasPrecision(18, 2);
            entry.Property(x => x.VerifiedDeposit).HasPrecision(18, 2);
            entry.Property(x => x.DepositReference).HasMaxLength(100);
            entry.HasIndex(x => new { x.HomeGroomerId, x.RequestedAtUtc });
            entry.HasOne<LocalApplicationUser>().WithMany().HasForeignKey(x => x.HomeGroomerId)
                .OnDelete(DeleteBehavior.Restrict);
            entry.Property(x => x.HomeCancellationReason).HasMaxLength(500);
            entry.Property(x => x.RefundReviewNote).HasMaxLength(500);
            entry.Property(x => x.TransportCancellationReason).HasMaxLength(500);
            entry.HasIndex(x => new { x.EstimateId, x.Kind }).IsUnique()
                .HasFilter("[Status] <> 3 AND [Status] <> 7");
            entry.HasIndex(x => new { x.Status, x.RequestedAtUtc });
            entry.HasIndex(x => new { x.TransportEmployeeId, x.TransportStartUtc });
            entry.Property(x => x.ReleasedByName).HasMaxLength(150);
            entry.Property(x => x.AcceptedByName).HasMaxLength(150);
            entry.HasOne<LocalApplicationUser>().WithMany().HasForeignKey(x => x.TransportEmployeeId)
                .OnDelete(DeleteBehavior.Restrict);
            entry.HasOne<GroomingQuoteRevision>().WithMany().HasForeignKey(x => new { x.EstimateId, x.EstimateVersion })
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<GroomingAppointment>(entry =>
        {
            entry.ToTable("GroomingAppointments");
            entry.Property(x => x.Revision).IsConcurrencyToken();
            entry.Property(x => x.CancellationReason).HasMaxLength(500);
            entry.HasIndex(x => x.EstimateId).IsUnique().HasFilter("[CancelledAtUtc] IS NULL");
            entry.HasIndex(x => new { x.GroomerId, x.StartsAtUtc });
            entry.HasIndex(x => new { x.PetId, x.StartsAtUtc });
            entry.HasIndex(x => x.StartsAtUtc);
            entry.HasOne<PetRecord>().WithMany().HasForeignKey(x => x.PetId).OnDelete(DeleteBehavior.Restrict);
            entry.HasOne<LocalApplicationUser>().WithMany().HasForeignKey(x => x.GroomerId).OnDelete(DeleteBehavior.Restrict);
            entry.HasOne<GroomingQuoteRevision>().WithMany().HasForeignKey(x => new { x.EstimateId, x.EstimateVersion })
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<WalkInEntry>(entry =>
        {
            entry.ToTable("WalkInQueue");
            entry.Property(x => x.Revision).IsConcurrencyToken();
            entry.Property(x => x.PriorityReason).HasMaxLength(500);
            entry.Property(x => x.CancellationReason).HasMaxLength(500);
            entry.HasIndex(x => x.PetId).IsUnique().HasFilter("[IsOpen] = 1");
            entry.HasIndex(x => new { x.IsOpen, x.Status, x.ArrivedAtUtc, x.Id });
            entry.HasOne<PetRecord>().WithMany().HasForeignKey(x => x.PetId).OnDelete(DeleteBehavior.Restrict);
            entry.HasOne<GroomingVisit>().WithMany().HasForeignKey(x => x.VisitId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<GroomingService>(entity =>
        {
            entity.ToTable("GroomingServices", table => table.HasCheckConstraint(
                "CK_GroomingServices_Duration",
                "([DurationMinutes] IS NULL AND [IsActive] = 0) OR ([DurationMinutes] IS NOT NULL AND [DurationMinutes] > 0)"));
            entity.Property(x => x.Version).IsConcurrencyToken();
            entity.Property(x => x.Name).HasMaxLength(120);
            entity.Property(x => x.Description).HasMaxLength(500);
            entity.Property(x => x.Species).HasMaxLength(80);
            entity.Property(x => x.Breed).HasMaxLength(100);
            entity.Property(x => x.Size).HasMaxLength(60);
        });

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
                    "CK_InventoryMovements_StockOutRequest",
                    "[StockOutRequestId] IS NULL OR [MovementType] = 'StockOut'");

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
                    "[QuantityAfter] = [QuantityBefore] + [QuantityChange]");

                table.HasCheckConstraint(
                    "CK_InventoryMovements_MovementType",
                    "[MovementType] IN " +
                    "('StockIn', 'StockOut', 'Adjustment', " +
                    "'Sale', 'SaleReversal', 'GroomingUsage', 'GroomingUsageCorrection')");

                table.HasCheckConstraint(
                    "CK_InventoryMovements_Direction",
                    "([MovementType] IN ('StockIn', 'SaleReversal', 'GroomingUsageCorrection') " +
                    "AND [QuantityChange] > 0) OR " +
                    "([MovementType] IN ('StockOut', 'Sale', 'GroomingUsage') " +
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
            entity.HasIndex(x => new { x.StockOutRequestId, x.ProductId })
                .IsUnique()
                .HasFilter("[StockOutRequestId] IS NOT NULL")
                .HasDatabaseName("UX_InventoryMovements_StockOutRequestId");
            entity.HasOne(x => x.StockOutRequest)
                .WithOne(x => x.Movement)
                .HasForeignKey<InventoryMovement>(x => new { x.StockOutRequestId, x.ProductId })
                .HasPrincipalKey<StockOutRequest>(x => new { x.Id, x.ProductId })
                .OnDelete(DeleteBehavior.Restrict);

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
