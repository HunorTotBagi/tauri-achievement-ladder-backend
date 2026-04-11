using System.Globalization;
using System.Text.Json;
using AchievementLadder.Models;

namespace AchievementLadder.Infrastructure;

public static class RareAchievementExtractor
{
    public static IReadOnlyList<CharacterRareAchievement> ExtractRareAchievements(
        JsonElement responseElement,
        IReadOnlyList<RareAchievementDefinition> definitions)
    {
        var achievedAchievements = ExtractAchievements(responseElement);

        return definitions
            .Where(entry => achievedAchievements.ContainsKey(entry.Id))
            .Select(entry => new CharacterRareAchievement(entry.Id, achievedAchievements[entry.Id]))
            .ToList();
    }

    private static Dictionary<int, DateTimeOffset?> ExtractAchievements(JsonElement responseElement)
    {
        if (!responseElement.TryGetProperty("Achievements", out var achievementsElement))
        {
            return [];
        }

        var achievements = new Dictionary<int, DateTimeOffset?>();

        if (achievementsElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in achievementsElement.EnumerateObject())
            {
                if (int.TryParse(property.Name, out var achievementId))
                {
                    achievements[achievementId] = ReadAchievementObtainedAt(property.Value);
                }
            }
        }
        else if (achievementsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in achievementsElement.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out var achievementId))
                {
                    achievements[achievementId] = null;
                    continue;
                }

                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                achievementId = ReadInt(item, "id", "achievementId", "achievementID", "achievement");
                if (achievementId > 0)
                {
                    achievements[achievementId] = ReadAchievementObtainedAt(item);
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

        foreach (var propertyName in new[]
                 {
                     "obtainedAt",
                     "completedAt",
                     "achievementDate",
                     "completionDate",
                     "date",
                     "completed",
                     "obtained",
                     "timestamp",
                     "time"
                 })
        {
            if (TryGetPropertyIgnoreCase(element, propertyName, out var property) &&
                TryReadDateValue(property, out obtainedAt))
            {
                return obtainedAt;
            }
        }

        var year = ReadInt(element, "year", "y");
        var month = ReadInt(element, "month", "m");
        var day = ReadInt(element, "day", "d");

        if (year > 0 && month > 0 && day > 0 &&
            TryCreateDate(year, month, day, out obtainedAt))
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
                return TryParseNumericDate((long)Math.Round(doubleValue, MidpointRounding.AwayFromZero), out value);
            }
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            var parts = element.EnumerateArray()
                .Take(3)
                .Where(item => item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out _))
                .Select(item => item.GetInt32())
                .ToArray();

            if (parts.Length == 3 && parts[0] > 31 &&
                TryCreateDate(parts[0], parts[1], parts[2], out value))
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
        if (long.TryParse(trimmedValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numericValue) &&
            TryParseNumericDate(numericValue, out value))
        {
            return true;
        }

        if (trimmedValue.Length == 8 &&
            DateOnly.TryParseExact(trimmedValue, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateOnly))
        {
            value = new DateTimeOffset(dateOnly.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            return true;
        }

        return DateTimeOffset.TryParse(
            trimmedValue,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out value);
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

            if (rawValue is >= 19000101 and <= 29991231 &&
                DateOnly.TryParseExact(rawValue.ToString(CultureInfo.InvariantCulture), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateOnly))
            {
                value = new DateTimeOffset(dateOnly.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
                return true;
            }
        }
        catch (ArgumentOutOfRangeException)
        {
        }

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

            if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var intValue))
            {
                return intValue;
            }

            if (property.ValueKind == JsonValueKind.String &&
                int.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out intValue))
            {
                return intValue;
            }
        }

        return 0;
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(propertyName, out value))
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
