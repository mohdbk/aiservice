namespace Moet.Recruitment.Application.Common;

/// <summary>Times are stored in UTC; dates and times shown to people are in the ministry's time zone.</summary>
public static class AppTime
{
    public static DateTime Local(this IClock c, DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), c.Zone);

    public static DateOnly Today(this IClock c) => DateOnly.FromDateTime(c.Local(c.UtcNow));

    public static string LocalDate(this IClock c, DateTime? utc) => utc == null ? "" : c.Local(utc.Value).ToString("yyyy-MM-dd");

    public static string LocalMinute(this IClock c, DateTime? utc) => utc == null ? "" : c.Local(utc.Value).ToString("yyyy-MM-dd HH:mm");

    public static string LocalHuman(this IClock c, DateTime? utc) => utc == null ? "" : c.Local(utc.Value).ToString("dd/MM/yyyy HH:mm");

    /// <summary>A local date and time (in the ministry's zone) to UTC.</summary>
    public static DateTime ToUtc(this IClock c, DateOnly date, TimeOnly time) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(date.ToDateTime(time), DateTimeKind.Unspecified), c.Zone);

    public static string Iso(DateTime? utc) =>
        utc == null ? "" : DateTime.SpecifyKind(utc.Value, DateTimeKind.Utc).ToString("yyyy-MM-ddTHH:mm:ssZ");

    public static string Date(DateOnly? d) => d?.ToString("yyyy-MM-dd") ?? "";
}
