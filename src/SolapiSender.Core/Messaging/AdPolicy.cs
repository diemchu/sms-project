namespace SolapiSender.Core.Messaging;

/// <summary>
/// 정보통신망법 제50조: cấm gửi tin quảng cáo trong khoảng 21:00–08:00 (giờ Hàn Quốc)
/// khi chưa có sự đồng ý riêng của người nhận.
/// </summary>
public static class AdPolicy
{
    public const int QuietStartHour = 21;
    public const int QuietEndHour = 8;

    public static readonly TimeZoneInfo KoreaTimeZone = ResolveKoreaTimeZone();

    public static DateTimeOffset ToKoreaTime(DateTimeOffset time) => TimeZoneInfo.ConvertTime(time, KoreaTimeZone);

    public static bool IsQuietHours(DateTimeOffset time)
    {
        var hour = ToKoreaTime(time).Hour;
        return hour >= QuietStartHour || hour < QuietEndHour;
    }

    /// <summary>Thời điểm sớm nhất được gửi quảng cáo kể từ <paramref name="time"/>.</summary>
    public static DateTimeOffset NextAllowedTime(DateTimeOffset time)
    {
        if (!IsQuietHours(time)) return time;

        var kst = ToKoreaTime(time);
        var date = kst.Hour >= QuietStartHour ? kst.Date.AddDays(1) : kst.Date;
        var allowed = new DateTimeOffset(date.AddHours(QuietEndHour), KoreaTimeZone.GetUtcOffset(date));
        return allowed;
    }

    private static TimeZoneInfo ResolveKoreaTimeZone()
    {
        foreach (var id in new[] { "Korea Standard Time", "Asia/Seoul" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return TimeZoneInfo.CreateCustomTimeZone("KST", TimeSpan.FromHours(9), "KST", "KST");
    }
}
