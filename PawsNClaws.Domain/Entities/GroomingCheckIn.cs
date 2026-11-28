namespace PawsNClaws.Domain.Entities;

public sealed class GroomingCheckIn
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid VisitId { get; set; }
    public Guid EmployeeId { get; set; }
    public DateTime RecordedAtUtc { get; set; }
    public string? TaskChecklist { get; set; }
    public string Assessment { get; set; } = "";
    public byte[] WaiverBytes { get; set; } = [];
    public string WaiverContentType { get; set; } = "";
    public string WaiverSha256 { get; set; } = "";
    public DateTime? CancelledAtUtc { get; set; }
    public Guid? CancelledByEmployeeId { get; set; }
    public int? CancelledEstimateVersion { get; set; }
    public string? CancellationReason { get; set; }
}
