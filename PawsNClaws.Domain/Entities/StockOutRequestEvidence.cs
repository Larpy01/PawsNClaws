namespace PawsNClaws.Domain.Entities;

public sealed class StockOutRequestEvidence
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid StockOutRequestId { get; set; }
    public StockOutRequest StockOutRequest { get; set; } = null!;
    public string OriginalFileName { get; set; } = string.Empty;
    public string StorageKey { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public string Sha256Hash { get; set; } = string.Empty;
    public Guid UploadedByUserId { get; set; }
    public DateTime UploadedAtUtc { get; set; } = DateTime.UtcNow;
}
