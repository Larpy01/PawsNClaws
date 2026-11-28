namespace PawsNClaws.Domain.Entities;

public sealed class CashDrawerShift
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public decimal OpeningCash { get; set; }
    public Guid OpenedByEmployeeId { get; set; }
    public DateTime OpenedAtUtc { get; set; }
    public Guid? ClosedByEmployeeId { get; set; }
    public DateTime? ClosedAtUtc { get; set; }
    public decimal? CountedCash { get; set; }
    public decimal? ExpectedAtClose { get; set; }
}

public enum CashDrawerEntryKind { RetailSale, RetailRefund, GroomingPayment, Manual }

public sealed class CashDrawerEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ShiftId { get; set; }
    public Guid ReferenceId { get; set; }
    public CashDrawerEntryKind Kind { get; set; }
    public decimal Amount { get; set; }
    public Guid RecordedByEmployeeId { get; set; }
    public DateTime RecordedAtUtc { get; set; }
    public string? Reason { get; set; }
}
