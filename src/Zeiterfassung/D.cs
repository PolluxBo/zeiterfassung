using System.Globalization;

namespace Zeiterfassung;

/// <summary>Datums- und Zeithelfer (Arbeitswoche Mo–Fr, KW nach ISO 8601).</summary>
public static class D
{
    static readonly CultureInfo De = new("de-DE");

    public static string Key(DateTime d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    public static DateTime FromKey(string k) => DateTime.ParseExact(k, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static DateTime Monday(DateTime d) => d.Date.AddDays(-(((int)d.DayOfWeek + 6) % 7));
    public static int Kw(DateTime d) => ISOWeek.GetWeekOfYear(d);
    public static int KwYear(DateTime d) => ISOWeek.GetYear(d);

    public static DateTime LastWorkday(DateTime d) => d.DayOfWeek switch
    {
        DayOfWeek.Saturday => d.Date.AddDays(-1),
        DayOfWeek.Sunday => d.Date.AddDays(-2),
        _ => d.Date
    };

    static readonly string[] ShortNames = { "So", "Mo", "Di", "Mi", "Do", "Fr", "Sa" };

    public static string WdShort(DateTime d) => ShortNames[(int)d.DayOfWeek];
    public static string WdLong(DateTime d) => De.DateTimeFormat.GetDayName(d.DayOfWeek);
    public static string Dm(DateTime d) => d.ToString("dd.MM.", De);
    public static string Dmy(DateTime d) => d.ToString("dd.MM.yyyy", De);
    public static string Long(DateTime d) => $"{WdLong(d)}, {Dmy(d)}";
    public static string Short(DateTime d) => $"{WdShort(d)}, {Dmy(d)}";

    /// <summary>90 → "1:30"</summary>
    public static string Hm(int minutes) => $"{minutes / 60}:{minutes % 60:00}";
}
