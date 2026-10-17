using System.Globalization;

namespace DonateWeb.Services;

/// <summary>Chuyển đổi lịch sự kiện giữa giờ Việt Nam trên giao diện và UTC trong cơ sở dữ liệu.</summary>
public static class VietnamTime
{
    private static readonly TimeZoneInfo Zone = FindZone();

    public static DateTime ToLocal(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone);

    public static bool TryParseLocal(string? value, out DateTime utc)
    {
        utc = default;
        if (!DateTime.TryParseExact(value, "yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var local))
            return false;

        utc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), Zone);
        return true;
    }

    private static TimeZoneInfo FindZone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh"); }
        catch (TimeZoneNotFoundException) { return FindWindowsZoneOrFixedOffset(); }
        catch (InvalidTimeZoneException) { return FindWindowsZoneOrFixedOffset(); }
    }

    private static TimeZoneInfo FindWindowsZoneOrFixedOffset()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time"); }
        catch (TimeZoneNotFoundException)
        {
            // Viet Nam does not observe daylight saving time, so UTC+7 is a stable fallback.
            return TimeZoneInfo.CreateCustomTimeZone("Vietnam Standard Time", TimeSpan.FromHours(7), "Vietnam Standard Time", "Vietnam Standard Time");
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.CreateCustomTimeZone("Vietnam Standard Time", TimeSpan.FromHours(7), "Vietnam Standard Time", "Vietnam Standard Time");
        }
    }
}
