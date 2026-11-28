namespace PawsNClaws.Domain.Entities;

public sealed class GroomingBill
{
    public Guid VisitId { get; set; }
    public int EstimateVersion { get; set; }
    public decimal Total { get; set; }
    public string EstimateSnapshot { get; set; } = "";
    public Guid? HomeDepositBookingId { get; set; }
    public decimal HomeDepositAmount { get; set; }
    public string? HomeDepositReference { get; set; }
    public Guid Revision { get; set; } = Guid.NewGuid();
    public Guid IssuedByEmployeeId { get; set; }
    public DateTime IssuedAtUtc { get; set; }
    public ICollection<GroomingCashPayment> Payments { get; set; } = [];
}

public sealed class GroomingCashPayment
{
    public Guid Id { get; set; }
    public Guid VisitId { get; set; }
    public decimal Amount { get; set; }
    public decimal Tendered { get; set; }
    public Guid RecordedByEmployeeId { get; set; }
    public DateTime RecordedAtUtc { get; set; }
}
