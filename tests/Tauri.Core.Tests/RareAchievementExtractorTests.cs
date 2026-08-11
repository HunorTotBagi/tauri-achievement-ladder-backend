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

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();
}
