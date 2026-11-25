namespace PawsNClaws.Domain.Entities;

public sealed class InventoryMovementEvidence
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid InventoryMovementId { get; set; }

    public InventoryMovement InventoryMovement { get; set; }
        = null!;

    public string OriginalFileName { get; set; }
        = string.Empty;

    public string StorageKey { get; set; }
        = string.Empty;

    public string ContentType { get; set; }
        = string.Empty;

    public long FileSizeBytes { get; set; }

    public string Sha256Hash { get; set; }
        = string.Empty;

    public Guid UploadedByUserId { get; set; }

    public DateTime UploadedAtUtc { get; set; }
        = DateTime.UtcNow;
}
