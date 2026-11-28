using System.Globalization;
using PawsNClaws.Application.Features.Reports;

namespace PawsNClaws.Local.Models;

public sealed record ReportMetric(string Label, decimal? Value, string Unit);

public sealed record OperationsReportPage(DateOnly Date, bool Monthly, DateOnly FirstDay, DateOnly LastDay, OperationsReport Report)
{
    public IEnumerable<ReportMetric> Money =>
    [
        new("Retail sales before refunds", Report.RetailGross, "PHP"),
        new("Retail refunds recorded", Report.RetailRefundAmount, "PHP"),
        new("Retail sales less refunds recorded", Report.RetailGross - Report.RetailRefundAmount, "PHP"),
        new("Retail VAT recorded on sales", Report.RetailVat, "PHP"),
        new("Retail VAT recorded on refunds", Report.RefundedRetailVat, "PHP"),
        new("Retail VAT less refunds recorded", Report.RetailVat - Report.RefundedRetailVat, "PHP"),
        new("Grooming billed", Report.GroomingBilled, "PHP"),
        new("Grooming cash received", Report.GroomingCashReceived, "PHP"),
        new("Verified home-service deposits", Report.VerifiedDeposits, "PHP"),
        new("Outstanding grooming balance at period end", Report.Outstanding, "PHP"),
        new("Cash drawer shortages", Report.CashShortages, "PHP"),
        new("Cash drawer overages", Report.CashOverages, "PHP")
    ];

    public IEnumerable<ReportMetric> RetailPayments =>
    [
        new("Cash sales", Report.CashRetail.Sales, "PHP"),
        new("Cash refunds", Report.CashRetail.Refunds, "PHP"),
        new("Net cash retail", Report.CashRetail.Net, "PHP"),
        new("Online sales", Report.OnlineRetail.Sales, "PHP"),
        new("Online refunds", Report.OnlineRetail.Refunds, "PHP"),
        new("Net online retail", Report.OnlineRetail.Net, "PHP")
    ];

    public IEnumerable<ReportMetric> Activity =>
    [
        new("Retail sales", Report.RetailSales, "count"),
        new("Retail refunds", Report.RetailRefunds, "count"),
        new("Retail sales without saved VAT details", Report.RetailSalesWithoutTaxSnapshot, "count"),
        new("Retail refunds without saved VAT details", Report.RetailRefundsWithoutTaxSnapshot, "count"),
        new("Grooming bills issued", Report.GroomingBills, "count"),
        new("Cash shifts closed", Report.CashShiftsClosed, "count"),
        new("Grooming check-ins", Report.GroomingCheckIns, "count"),
        new("Pet pickups", Report.GroomingPickups, "count"),
        new("Walk-ins added", Report.WalkIns, "count"),
        new("Walk-ins called", Report.WalkInsCalled, "count"),
        new("Average time from arrival to call", Report.AverageWaitMinutes, "minutes"),
        new("In-shop appointments scheduled for this period", Report.Appointments, "count"),
        new("Cancelled in-shop appointments for this period", Report.CancelledAppointments, "count"),
        new("Inventory movements", Report.StockMovements, "count")
    ];

    public string FormatMetric(ReportMetric metric, CultureInfo culture) => metric.Unit switch
    {
        "PHP" => $"PHP {metric.Value?.ToString("N2", culture)}",
        "minutes" when metric.Value.HasValue => $"{metric.Value.Value.ToString("0.#", culture)} min",
        "minutes" => "No calls recorded",
        _ => metric.Value?.ToString("N0", culture) ?? ""
    };
}
