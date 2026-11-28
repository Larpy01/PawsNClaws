
using PawsNClaws.Application.Features.Reports;

namespace PawsNClaws.Local.Models;

public sealed class DashboardViewModel
{
    public string EmployeeName { get; init; } = string.Empty;

    public IReadOnlyList<string> Roles { get; init; } =
        Array.Empty<string>();

    public OperationsDashboard? Operations { get; init; }
}

public sealed record DashboardTrend(string Title, string PrimaryLabel, string? SecondaryLabel,
    IReadOnlyList<DashboardDay> Days);

public sealed record DashboardMix(string Title, string PrimaryLabel, string SecondaryLabel,
    decimal Primary, decimal Secondary, string UnitFormat, string? Period = null)
{
    public string PrimaryShare => ((Primary + Secondary == 0 ? 0 : Primary / (Primary + Secondary)) * 100)
        .ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "%";
}
