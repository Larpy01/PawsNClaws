namespace PawsNClaws.Application.Features.Grooming;

public sealed record VisitBillSummary(Guid VisitId, string Pet, string Owner, DateTime IssuedAtUtc, decimal Total, decimal Paid)
{
    public decimal Balance => Total - Paid;
}

public sealed record VisitCashEntry(Guid Id, decimal Amount, decimal Tendered, DateTime RecordedAtUtc)
{
    public decimal Change => Tendered - Amount;
}

public sealed record VisitBillDetails(Guid VisitId, int EstimateVersion, Guid Revision, QuoteSnapshot Estimate,
    decimal Total, IReadOnlyList<VisitCashEntry> Payments, decimal HomeDepositAmount, string? HomeDepositReference)
{
    public decimal Paid => HomeDepositAmount + Payments.Sum(x => x.Amount);
    public decimal Balance => Total - Paid;
}

public sealed record VisitCashPrintout(Guid VisitId, int EstimateVersion, string Pet, string Owner, VisitCashEntry Payment);

public interface IVisitBilling
{
    Task<IReadOnlyList<VisitBillSummary>> ListAsync(int page, CancellationToken token);
    Task<VisitCashPrintout?> FindPaymentAsync(Guid visitId, Guid paymentId, CancellationToken token);
    Task<VisitBillDetails?> FindAsync(Guid visitId, CancellationToken token);
    Task<string?> IssueAsync(Guid visitId, Guid employeeId, CancellationToken token);
    Task<string?> RecordCashAsync(Guid visitId, Guid revision, Guid operationId, decimal amount, decimal tendered,
        Guid employeeId, CancellationToken token);
}
