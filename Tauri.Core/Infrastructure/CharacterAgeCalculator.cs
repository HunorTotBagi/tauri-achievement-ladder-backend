using System.Globalization;

namespace Tauri.Core.Infrastructure;

/// <summary>
/// Computes a human-readable "Character Age" from the date a character earned the
/// "Level 10" achievement up to the moment the scan was started.
/// </summary>
public static class CharacterAgeCalculator
{
    /// <summary>
    /// Formats the calendar difference between <paramref name="achievedAt"/> and
    /// <paramref name="scanStartedAt"/> as "{years} years {months} months {days} days".
    /// Returns an empty string when the achievement date is unknown.
    /// </summary>
    public static string Format(DateTimeOffset? achievedAt, DateTimeOffset scanStartedAt)
    {
        if (achievedAt is not { } earned)
        {
            return string.Empty;
        }

        var start = earned.UtcDateTime.Date;
        var end = scanStartedAt.UtcDateTime.Date;

        if (end <= start)
        {
            return "0 years 0 months 0 days";
        }

        var years = end.Year - start.Year;
        var months = end.Month - start.Month;
        var days = end.Day - start.Day;

        if (days < 0)
        {
            months--;
            var borrowedMonth = end.AddMonths(-1);
            days += DateTime.DaysInMonth(borrowedMonth.Year, borrowedMonth.Month);
        }

        if (months < 0)
        {
            years--;
            months += 12;
        }

        return string.Format(
            CultureInfo.InvariantCulture,
            "{0} years {1} months {2} days",
            years,
            months,
            days
        );
    }
}
