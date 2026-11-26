using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PawsNClaws.Domain.Entities;
using PawsNClaws.Domain.Enums;
using PawsNClaws.Infastructure.Identity.Local;

namespace PawsNClaws.Infastructure.Persistence.Local;

internal sealed class StockOutRequestConfiguration : IEntityTypeConfiguration<StockOutRequest>
{
    public void Configure(EntityTypeBuilder<StockOutRequest> entity)
    {
        var reasonCategories = string.Join(", ", Enum.GetNames<StockOutReason>().Select(name => $"'{name}'"));
        entity.ToTable("StockOutRequests", table =>
        {
            table.HasCheckConstraint("CK_StockOutRequests_Quantity", "[Quantity] > 0");
            table.HasCheckConstraint("CK_StockOutRequests_ReasonCategory",
                $"[ReasonCategory] IN ({reasonCategories})");
            table.HasCheckConstraint("CK_StockOutRequests_Reason", "LEN(LTRIM(RTRIM([Reason]))) > 0");
            table.HasCheckConstraint("CK_StockOutRequests_Decision",
                """
                ([Status] = 'Pending' AND [DecidedByUserId] IS NULL AND [DecidedAtUtc] IS NULL AND [DecisionComment] IS NULL)
                OR
                ([Status] IN ('Approved', 'Rejected', 'Cancelled') AND [DecidedByUserId] IS NOT NULL
                    AND [DecidedAtUtc] IS NOT NULL AND [DecidedAtUtc] >= [SubmittedAtUtc]
                    AND ([Status] = 'Cancelled' OR [DecidedByUserId] <> [SubmittedByUserId])
                    AND ([Status] <> 'Rejected'
                        OR ([DecisionComment] IS NOT NULL AND LEN(LTRIM(RTRIM([DecisionComment]))) > 0)))
                """);
        });

        entity.Property(request => request.ReasonCategory).HasConversion<string>().HasMaxLength(30);
        entity.Property(request => request.Reason).HasMaxLength(500);
        entity.Property(request => request.Status).HasConversion<string>().HasMaxLength(30);
        entity.Property(request => request.DecisionComment).HasMaxLength(500);
        entity.Property(request => request.RowVersion).IsRowVersion();
        entity.HasIndex(request => request.SubmissionOperationId)
            .IsUnique()
            .HasDatabaseName("UX_StockOutRequests_SubmissionOperationId");
        entity.HasIndex(request => new { request.Status, request.SubmittedAtUtc });

        entity.HasOne(request => request.Product)
            .WithMany()
            .HasForeignKey(request => request.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<LocalApplicationUser>()
            .WithMany()
            .HasForeignKey(request => request.SubmittedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<LocalApplicationUser>()
            .WithMany()
            .HasForeignKey(request => request.DecidedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class StockOutRequestEvidenceConfiguration : IEntityTypeConfiguration<StockOutRequestEvidence>
{
    public void Configure(EntityTypeBuilder<StockOutRequestEvidence> entity)
    {
        entity.ToTable("StockOutRequestEvidence", table =>
        {
            table.HasCheckConstraint("CK_StockOutRequestEvidence_FileSize",
                "[FileSizeBytes] > 0 AND [FileSizeBytes] <= 5242880");
            table.HasCheckConstraint("CK_StockOutRequestEvidence_ContentType",
                "[ContentType] IN ('image/jpeg', 'image/png')");
            table.HasCheckConstraint("CK_StockOutRequestEvidence_HashLength", "LEN([Sha256Hash]) = 64");
        });

        entity.Property(evidence => evidence.OriginalFileName).HasMaxLength(255);
        entity.Property(evidence => evidence.StorageKey).HasMaxLength(500);
        entity.Property(evidence => evidence.ContentType).HasMaxLength(100);
        entity.Property(evidence => evidence.Sha256Hash).HasMaxLength(64).IsUnicode(false);
        entity.HasIndex(evidence => evidence.StorageKey)
            .IsUnique()
            .HasDatabaseName("UX_StockOutRequestEvidence_StorageKey");

        entity.HasOne(evidence => evidence.StockOutRequest)
            .WithMany(request => request.Evidence)
            .HasForeignKey(evidence => evidence.StockOutRequestId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<LocalApplicationUser>()
            .WithMany()
            .HasForeignKey(evidence => evidence.UploadedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
