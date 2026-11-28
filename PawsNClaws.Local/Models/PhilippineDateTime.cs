using System.Globalization;

namespace PawsNClaws.Local.Models;

public static class PhilippineDateTime
{
    private static readonly TimeZoneInfo TimeZone = TimeZoneInfo.FindSystemTimeZoneById(
        OperatingSystem.IsWindows() ? "Singapore Standard Time" : "Asia/Manila");

    public static DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZone));

    public static DateTime ToUtc(DateTime local) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), TimeZone);

    public static string FormatUtc(DateTime timestamp)
    {
        // SQL Server timestamps stored as UTC can come back with an unspecified kind.
        var utc = DateTime.SpecifyKind(timestamp, DateTimeKind.Utc);
        var local = TimeZoneInfo.ConvertTimeFromUtc(utc, TimeZone);
        return local.ToString("MM'/'dd'/'yyyy hh':'mm tt", CultureInfo.InvariantCulture);
    }
}
