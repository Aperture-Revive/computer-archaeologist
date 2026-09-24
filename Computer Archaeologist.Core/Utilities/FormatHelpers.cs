using System.Globalization;

namespace ComputerArchaeologist.Core.Utilities;

/// <summary>Human readable formatting helpers shared by the UI and the report generator.</summary>
public static class FormatHelpers
{
    private static readonly string[] SizeUnits = { "B", "KB", "MB", "GB", "TB", "PB" };

    public static string FileSize(long bytes)
    {
        if (bytes < 0)
        {
            return "?";
        }

        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < SizeUnits.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0
            ? $"{bytes} {SizeUnits[0]}"
            : $"{value.ToString(value >= 100 ? "0" : "0.#", CultureInfo.CurrentCulture)} {SizeUnits[unit]}";
    }

    public static string Date(DateTimeOffset? value) =>
        value is null ? "—" : value.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture);

    public static string Year(DateTimeOffset? value) => value is null ? "—" : value.Value.ToLocalTime().Year.ToString(CultureInfo.InvariantCulture);

    public static string Duration(TimeSpan span) =>
        span.TotalHours >= 1
            ? $"{(int)span.TotalHours}h {span.Minutes}m {span.Seconds}s"
            : span.TotalMinutes >= 1
                ? $"{span.Minutes}m {span.Seconds}s"
                : $"{span.TotalSeconds:0.#}s";

    /// <summary>Years between <paramref name="then"/> and now, never negative.</summary>
    public static double YearsSince(DateTimeOffset? then, DateTimeOffset now)
    {
        if (then is null)
        {
            return 0;
        }

        var days = (now - then.Value).TotalDays;
        return days <= 0 ? 0 : days / 365.2425;
    }

    /// <summary>Truncates long text for compact UI surfaces.</summary>
    public static string Ellipsis(string? text, int max)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= max)
        {
            return text ?? string.Empty;
        }

        return string.Concat(text.AsSpan(0, Math.Max(1, max - 1)), "…");
    }
}
