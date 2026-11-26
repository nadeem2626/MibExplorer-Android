using System.Globalization;
namespace MibExplorer.Services;

/// <summary>Reads the date columns of `ls -la` ("Nov 24 12:30" for recent files, "Nov 24  2025" for older ones). The unit's time zone is unknown, so no zone conversion is ever applied.</summary>
public static class LsDate
{
    private static readonly string[] Months = { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };

    public static DateTimeOffset? Parse(string month, string day, string timeOrYear, DateTime today)
    {
        int m = Array.FindIndex(Months, x => string.Equals(x, month, StringComparison.OrdinalIgnoreCase)) + 1;
        if (m == 0 || !int.TryParse(day, NumberStyles.None, CultureInfo.InvariantCulture, out int d) || d < 1 || d > 31) return null;
        try
        {
            if (timeOrYear.Contains(':'))
            {
                var hm = timeOrYear.Split(':'); if (hm.Length != 2 || !int.TryParse(hm[0], out int h) || !int.TryParse(hm[1], out int mi)) return null;
                int year = today.Year;
                // ls prints a time instead of a year for dates within about six months in the past; a date later than tomorrow therefore belongs to last year.
                if (d <= DateTime.DaysInMonth(year, m) && new DateTime(year, m, d) > today.Date.AddDays(1)) year--;
                if (d > DateTime.DaysInMonth(year, m)) return null;
                return new DateTimeOffset(new DateTime(year, m, d, h, mi, 0), TimeSpan.Zero);
            }
            if (!int.TryParse(timeOrYear, NumberStyles.None, CultureInfo.InvariantCulture, out int y) || y < 1970 || y > 2100) return null;
            if (d > DateTime.DaysInMonth(y, m)) return null;
            return new DateTimeOffset(new DateTime(y, m, d), TimeSpan.Zero);
        }
        catch (ArgumentException) { return null; }
    }

    /// <summary>Label of the design's Modified column ("24 Nov 2026"); empty when unknown.</summary>
    public static string Label(DateTimeOffset? t) => t is null ? "" : t.Value.ToString("dd MMM yyyy", CultureInfo.InvariantCulture);
}
