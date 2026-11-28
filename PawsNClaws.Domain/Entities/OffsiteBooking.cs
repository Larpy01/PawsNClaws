namespace PawsNClaws.Domain.Entities;

public enum OffsiteKind { HomeService, Pickup, ReturnDelivery }
public enum OffsiteStatus { Requested, AwaitingDeposit, Reviewed, Rejected, Scheduled, InCustody, Completed, Cancelled, Confirmed }

public sealed class OffsiteBooking
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid Revision { get; set; } = Guid.NewGuid();
    public Guid EstimateId { get; set; }
    public int EstimateVersion { get; set; }
    public OffsiteKind Kind { get; set; }
    public OffsiteStatus Status { get; set; }
    public DateTime RequestedAtUtc { get; set; }
    public string Address { get; set; } = "";
    public string Phone { get; set; } = "";
    public Guid RecordedBy { get; set; }
    public DateTime RecordedAtUtc { get; set; }
    public Guid? HomeGroomerId { get; set; }
    public DateTime? HomeEndsAtUtc { get; set; }
    public DateTime? HoldExpiresAtUtc { get; set; }
    public decimal? DepositRequired { get; set; }
    public decimal? VerifiedDeposit { get; set; }
    public string? DepositReference { get; set; }
    public Guid? ConfirmedBy { get; set; }
    public DateTime? ConfirmedAtUtc { get; set; }
    public DateTime? HomeCancelledAtUtc { get; set; }
    public Guid? HomeCancelledBy { get; set; }
    public string? HomeCancellationReason { get; set; }
    public DateTime? RefundReviewedAtUtc { get; set; }
    public Guid? RefundReviewedBy { get; set; }
    public string? RefundReviewNote { get; set; }
    public Guid? TransportEmployeeId { get; set; }
    public DateTime? TransportStartUtc { get; set; }
    public DateTime? TransportEndUtc { get; set; }
    public Guid? TransportScheduledBy { get; set; }
    public DateTime? CustodyAtUtc { get; set; }
    public Guid? CustodyRecordedBy { get; set; }
    public string? ReleasedByName { get; set; }
    public DateTime? DeliveredAtUtc { get; set; }
    public Guid? DeliveryRecordedBy { get; set; }
    public string? TransportCancellationReason { get; set; }
    public Guid? TransportCancelledBy { get; set; }
    public DateTime? TransportCancelledAtUtc { get; set; }
    public string? AcceptedByName { get; set; }
    public Guid? ReviewedBy { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    public string? ReviewReason { get; set; }
}
