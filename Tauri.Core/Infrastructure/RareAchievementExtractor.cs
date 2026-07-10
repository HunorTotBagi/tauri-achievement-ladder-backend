using System.Globalization;
using System.Text.Json;
using Tauri.Core.Models;

namespace Tauri.Core.Infrastructure;

public static class RareAchievementExtractor
{
    private static readonly string[] ObtainedAtPropertyNames =
    [
        "obtainedAt",
        "completedAt",
        "achievementDate",
        "completionDate",
        "date",
        "completed",
        "obtained",
        "timestamp",
        "time",
    ];
    private static readonly string[] AchievementIdPropertyNames =
    [
        "id",
        "achievementId",
        "achievementID",
        "achievement",
    ];
    private static readonly string[] YearPropertyNames = ["year", "y"];
    private static readonly string[] MonthPropertyNames = ["month", "m"];
    private static readonly string[] DayPropertyNames = ["day", "d"];

    public static IReadOnlyList<CharacterRareAchievement> ExtractRareAchievements(
        IReadOnlyDictionary<int, DateTimeOffset?> achievedAchievements,
        IReadOnlyList<RareAchievementDefinition> definitions
    )
    {
        var rareAchievements = new List<CharacterRareAchievement>();

        foreach (var definition in definitions)
        {
            if (achievedAchievements.TryGetValue(definition.Id, out var obtainedAt))
            {
                rareAchievements.Add(
                    new CharacterRareAchievement(definition.Id, obtainedAt)
                );
            }
        }

        return rareAchievements;
    }

    /// <summary>
    /// Extracts every achieved achievement id. Obtained dates are only parsed for ids in
    /// <paramref name="dateTrackedIds"/>; all other entries are stored with a null date,
    /// which skips the expensive multi-format date parsing for the vast majority of entries.
    /// </summary>
    public static IReadOnlyDictionary<int, DateTimeOffset?> ExtractAchievements(
        JsonElement responseElement,
        IReadOnlySet<int> dateTrackedIds
    )
    {
        if (!responseElement.TryGetProperty("Achievements", out var achievementsElement))
        {
            return new Dictionary<int, DateTimeOffset?>();
        }

        var achievements = new Dictionary<int, DateTimeOffset?>();

        if (achievementsElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in achievementsElement.EnumerateObject())
            {
                if (int.TryParse(property.Name, out var achievementId))
                {
                    achievements[achievementId] = dateTrackedIds.Contains(achievementId)
                        ? ReadAchievementObtainedAt(property.Value)
                        : null;
                }
            }
        }
        else if (achievementsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in achievementsElement.EnumerateArray())
            {
                if (
                    item.ValueKind == JsonValueKind.Number
                    && item.TryGetInt32(out var achievementId)
                )
                {
                    achievements[achievementId] = null;
                    continue;
                }

                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                achievementId = ReadInt(item, AchievementIdPropertyNames);
                if (achievementId > 0)
                {
                    achievements[achievementId] = dateTrackedIds.Contains(achievementId)
                        ? ReadAchievementObtainedAt(item)
                        : null;
                }
            }
        }

        return achievements;
    }

    private static DateTimeOffset? ReadAchievementObtainedAt(JsonElement element)
    {
        if (TryReadDateValue(element, out var obtainedAt))
        {
            return obtainedAt;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var propertyName in ObtainedAtPropertyNames)
        {
            if (
                TryGetPropertyIgnoreCase(element, propertyName, out var property)
                && TryReadDateValue(property, out obtainedAt)
            )
            {
                return obtainedAt;
            }
        }

        var year = ReadInt(element, YearPropertyNames);
        var month = ReadInt(element, MonthPropertyNames);
        var day = ReadInt(element, DayPropertyNames);

        if (year > 0 && month > 0 && day > 0 && TryCreateDate(year, month, day, out obtainedAt))
        {
            return obtainedAt;
        }

        return null;
    }

    private static bool TryReadDateValue(JsonElement element, out DateTimeOffset value)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            return TryParseDateString(element.GetString(), out value);
        }

        if (element.ValueKind == JsonValueKind.Number)
        {
            if (element.TryGetInt64(out var longValue))
            {
                return TryParseNumericDate(longValue, out value);
            }

            if (element.TryGetDouble(out var doubleValue))
            {
                return TryParseNumericDate(
                    (long)Math.Round(doubleValue, MidpointRounding.AwayFromZero),
                    out value
                );
            }
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            var parts = element
                .EnumerateArray()
                .Take(3)
                .Where(item => item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out _))
                .Select(item => item.GetInt32())
                .ToArray();

            if (
                parts.Length == 3
                && parts[0] > 31
                && TryCreateDate(parts[0], parts[1], parts[2], out value)
            )
            {
                return true;
            }
        }

        value = default;
        return false;
    }

    private static bool TryParseDateString(string? rawValue, out DateTimeOffset value)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            value = default;
            return false;
        }

        var trimmedValue = rawValue.Trim();
        if (
            long.TryParse(
                trimmedValue,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var numericValue
            ) && TryParseNumericDate(numericValue, out value)
        )
        {
            return true;
        }

        if (
            trimmedValue.Length == 8
            && DateOnly.TryParseExact(
                trimmedValue,
                "yyyyMMdd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var dateOnly
            )
        )
        {
            value = new DateTimeOffset(dateOnly.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            return true;
        }

        return DateTimeOffset.TryParse(
            trimmedValue,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces
                | DateTimeStyles.AssumeUniversal
                | DateTimeStyles.AdjustToUniversal,
            out value
        );
    }

    private static bool TryParseNumericDate(long rawValue, out DateTimeOffset value)
    {
        try
        {
            if (Math.Abs(rawValue) >= 100_000_000_000)
            {
                value = DateTimeOffset.FromUnixTimeMilliseconds(rawValue);
                return true;
            }

            if (Math.Abs(rawValue) >= 1_000_000_000)
            {
                value = DateTimeOffset.FromUnixTimeSeconds(rawValue);
                return true;
            }

            if (
                rawValue is >= 19000101 and <= 29991231
                && DateOnly.TryParseExact(
                    rawValue.ToString(CultureInfo.InvariantCulture),
                    "yyyyMMdd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var dateOnly
                )
            )
            {
                value = new DateTimeOffset(dateOnly.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
                return true;
            }
        }
        catch (ArgumentOutOfRangeException) { }

        value = default;
        return false;
    }

    private static bool TryCreateDate(int year, int month, int day, out DateTimeOffset value)
    {
        try
        {
            value = new DateTimeOffset(year, month, day, 0, 0, 0, TimeSpan.Zero);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            value = default;
            return false;
        }
    }

    private static int ReadInt(JsonElement element, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!TryGetPropertyIgnoreCase(element, propertyName, out var property))
            {
                continue;
            }

            if (
                property.ValueKind == JsonValueKind.Number
                && property.TryGetInt32(out var intValue)
            )
            {
                return intValue;
            }

            if (
                property.ValueKind == JsonValueKind.String
                && int.TryParse(
                    property.GetString(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out intValue
                )
            )
            {
                return intValue;
            }
        }

        return 0;
    }

    private static bool TryGetPropertyIgnoreCase(
        JsonElement element,
        string propertyName,
        out JsonElement value
    )
    {
        if (
            element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(propertyName, out value)
        )
        {
            return true;
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }
}
