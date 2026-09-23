using System;

namespace PragmaticBot.Server.Services;

/// <summary>
/// 화면 표시는 서버(VPS) 시간대가 아니라 항상 한국시간(KST, UTC+9)으로 변환한다.
/// DB에는 UTC로 저장하고, 표시/필터에서만 KST로 바꾼다. (VPS가 UTC든 다른 지역이든 무관)
/// </summary>
public static class KoreaTime
{
    public static readonly TimeZoneInfo Zone = Resolve();

    private static TimeZoneInfo Resolve()
    {
        // 리눅스는 IANA("Asia/Seoul"), 윈도우는 "Korea Standard Time". 둘 다 못 찾으면 +9 고정.
        foreach (var id in new[] { "Asia/Seoul", "Korea Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch { /* try next */ }
        }
        return TimeZoneInfo.CreateCustomTimeZone("KST", TimeSpan.FromHours(9), "KST", "KST");
    }

    /// <summary>현재 시각을 KST로.</summary>
    public static DateTime Now => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Zone);

    /// <summary>UTC 시각을 KST 문자열로 포맷.</summary>
    public static string Fmt(DateTime utc, string fmt) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone).ToString(fmt);

    /// <summary>널 허용 UTC 시각을 KST 문자열로. 널이면 null.</summary>
    public static string? Fmt(DateTime? utc, string fmt) =>
        utc.HasValue ? Fmt(utc.Value, fmt) : null;

    /// <summary>KST 달력의 날짜/시각(Unspecified)을 그 순간의 UTC로 되돌린다(필터·만료일 계산용).</summary>
    public static DateTime DateToUtc(DateTime kstLocal) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(kstLocal, DateTimeKind.Unspecified), Zone);

    /// <summary>UTC 시각을 KST 달력 날짜(00:00)로. 만료일 연장 기준 계산용.</summary>
    public static DateTime KstDateOf(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone).Date;
}
