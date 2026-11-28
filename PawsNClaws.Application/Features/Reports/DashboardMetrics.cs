namespace PawsNClaws.Application.Features.Reports;

public sealed record DashboardDay(DateOnly Date, int PrimaryCount, int SecondaryCount = 0);
public sealed record OperationsDashboard(IReadOnlyList<DashboardDay> Days, decimal CashSales, decimal OnlineSales,
    int TodayAppointments, int TodayPickups, int OpenWalkIns);
public sealed record TechnicalDashboard(IReadOnlyList<DashboardDay> Days, int ActiveAccounts, int InactiveAccounts);

public interface IDashboardMetrics
{
    Task<OperationsDashboard> OperationsAsync(DateTime nowUtc, CancellationToken token);
    Task<TechnicalDashboard> TechnicalAsync(DateTime nowUtc, CancellationToken token);
}
