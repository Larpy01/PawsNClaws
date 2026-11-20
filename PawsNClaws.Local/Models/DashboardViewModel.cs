
namespace PawsNClaws.Local.Models;

public sealed class DashboardViewModel
{
    public string EmployeeName { get; init; } = string.Empty;

    public IReadOnlyList<string> Roles { get; init; } =
        Array.Empty<string>();

    public DateTime CurrentDateUtc { get; init; } =
        DateTime.UtcNow;
}
