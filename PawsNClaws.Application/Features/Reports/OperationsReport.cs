namespace PawsNClaws.Application.Features.Reports;

public sealed record OperationsReport(
    int RetailSales, decimal RetailGross, int RetailRefunds, decimal RetailRefundAmount,
    int GroomingBills, decimal GroomingBilled, decimal GroomingCashReceived, decimal VerifiedDeposits,
    decimal Outstanding, int CashShiftsClosed, decimal CashShortages, decimal CashOverages,
    int GroomingCheckIns, int GroomingPickups, int WalkIns, int WalkInsCalled, decimal? AverageWaitMinutes,
    int Appointments, int CancelledAppointments, int StockMovements,
    decimal RetailVat, decimal RefundedRetailVat, int RetailSalesWithoutTaxSnapshot, int RetailRefundsWithoutTaxSnapshot,
    RetailPaymentTotals CashRetail, RetailPaymentTotals OnlineRetail);

public sealed record RetailPaymentTotals(decimal Sales, decimal Refunds)
{
    public decimal Net => Sales - Refunds;
}

public interface IOperationsReports
{
    Task<OperationsReport> ReadAsync(DateTime fromUtc, DateTime untilUtc, CancellationToken token);
}
