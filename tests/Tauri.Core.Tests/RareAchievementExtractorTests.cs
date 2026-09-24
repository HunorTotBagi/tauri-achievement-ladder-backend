using System.Text.Json;
using Tauri.Core.Infrastructure;
using Tauri.Core.Models;

namespace Tauri.Core.Tests;

public sealed class RareAchievementExtractorTests
{
    [Fact]
    public void ExtractAchievements_ObjectPayload_ParsesTrackedDateOnly()
    {
        var response = Parse(
            """
            {
              "Achievements": {
                "6": { "obtainedAt": "2020-04-05T00:00:00Z" },
                "416": { "obtainedAt": "2021-01-01T00:00:00Z" }
              }
            }
            """
        );

        var result = RareAchievementExtractor.ExtractAchievements(response, new HashSet<int> { 6 });

        Assert.Equal(new DateTimeOffset(2020, 4, 5, 0, 0, 0, TimeSpan.Zero), result[6]);
        Assert.Null(result[416]);
    }

    [Fact]
    public void ExtractAchievements_ArrayPayload_MissingObtainedDate_ReturnsAchievementWithoutDate()
    {
        var response = Parse("""{ "Achievements": [{ "achievementId": 416 }] }""");

        var result = RareAchievementExtractor.ExtractAchievements(
            response,
            new HashSet<int> { 416 }
        );

        Assert.True(result.ContainsKey(416));
        Assert.Null(result[416]);
    }

    [Fact]
    public void ExtractAchievements_InvalidEntries_AreIgnored()
    {
        var response = Parse(
            """{ "Achievements": [null, "invalid", {}, { "id": "not-a-number" }] }"""
        );

        var result = RareAchievementExtractor.ExtractAchievements(response, new HashSet<int>());

        Assert.Empty(result);
    }

    [Fact]
    public void ExtractAchievements_UnixMilliseconds_ParsesUtcDate()
    {
        var response = Parse("""{ "Achievements": [{ "id": 416, "timestamp": 1609459200000 }] }""");

        var result = RareAchievementExtractor.ExtractAchievements(
            response,
            new HashSet<int> { 416 }
        );

        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1609459200000), result[416]);
    }

    [Fact]
    public void ExtractRareAchievements_PreservesDefinitionOrderAndObtainedDate()
    {
        var obtainedAt = new DateTimeOffset(2020, 1, 2, 0, 0, 0, TimeSpan.Zero);
        var achieved = new Dictionary<int, DateTimeOffset?> { [2] = obtainedAt, [1] = null };
        RareAchievementDefinition[] definitions =
        [
            new(1, "First"),
            new(2, "Second"),
            new(3, "Missing"),
        ];

        var result = RareAchievementExtractor.ExtractRareAchievements(achieved, definitions);

        Assert.Collection(
            result,
            first =>
            {
                Assert.Equal(1, first.Id);
                Assert.Null(first.ObtainedAt);
            },
            second =>
            {
                Assert.Equal(2, second.Id);
                Assert.Equal(obtainedAt, second.ObtainedAt);
            }
        );
    }

    [Fact]
    public void ExtractRareAchievements_MatchedDateRequirements_IncludesBothAchievements()
    {
        var requiredDate = new DateTimeOffset(2015, 5, 3, 0, 0, 0, TimeSpan.Zero);
        var achieved = new Dictionary<int, DateTimeOffset?>
        {
            [5116] = requiredDate,
            [5108] = requiredDate,
        };
        RareAchievementDefinition[] definitions =
        [
            new(5116, "Heroic: Nefarian"),
            new(5108, "Heroic: Maloriak"),
        ];
        var requirements = CreateMatchedDateRequirements(new DateOnly(2015, 5, 3));

        var result = RareAchievementExtractor.ExtractRareAchievements(
            achieved,
            definitions,
            requirements
        );

        Assert.Equal([5116, 5108], result.Select(achievement => achievement.Id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExtractRareAchievements_UnmetMatchedDateRequirements_ExcludesBothAchievements(
        bool includesBoth
    )
    {
        var achieved = new Dictionary<int, DateTimeOffset?>
        {
            [5116] = new DateTimeOffset(2015, 5, 3, 0, 0, 0, TimeSpan.Zero),
        };
        if (includesBoth)
        {
            achieved[5108] = new DateTimeOffset(2015, 5, 4, 0, 0, 0, TimeSpan.FromHours(2));
        }

        RareAchievementDefinition[] definitions =
        [
            new(5116, "Heroic: Nefarian"),
            new(5108, "Heroic: Maloriak"),
        ];

        var result = RareAchievementExtractor.ExtractRareAchievements(
            achieved,
            definitions,
            CreateMatchedDateRequirements(new DateOnly(2015, 5, 3))
        );

        Assert.Empty(result);
    }

    private static IReadOnlyDictionary<int, IReadOnlyDictionary<int, DateOnly>>
        CreateMatchedDateRequirements(DateOnly requiredDate)
    {
        IReadOnlyDictionary<int, DateOnly> pair = new Dictionary<int, DateOnly>
        {
            [5116] = requiredDate,
            [5108] = requiredDate,
        };

        return new Dictionary<int, IReadOnlyDictionary<int, DateOnly>>
        {
            [5116] = pair,
            [5108] = pair,
        };
    }

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();
}
