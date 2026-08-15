using System.Text.Json;
using Tauri.Core.Infrastructure;
using Tauri.Core.Models;

namespace Tauri.Core.Tests;

public sealed class CharacterResponseMapperTests
{
    [Fact]
    public void CreatePlayer_CompleteResponse_MapsProfileAndCharacterAge()
    {
        var response = Parse(
            """
            {
              "race": 1,
              "gender": 0,
              "class": 8,
              "level": 110,
              "pts": 12345,
              "playerHonorKills": 678,
              "faction_string_class": "Alliance",
              "guildName": "Example Guild"
            }
            """
        );
        var achievements = new Dictionary<int, DateTimeOffset?>
        {
            [CharacterResponseMapper.Level10AchievementId] = new DateTimeOffset(
                2020,
                1,
                1,
                0,
                0,
                0,
                TimeSpan.Zero
            ),
        };

        var player = CharacterResponseMapper.CreatePlayer(
            response,
            achievements,
            "Examplemage",
            "Evermoon",
            new DateTimeOffset(2022, 2, 3, 0, 0, 0, TimeSpan.Zero)
        );

        Assert.Equal("Examplemage", player.Name);
        Assert.Equal("Evermoon", player.Realm);
        Assert.Equal(1, player.Race);
        Assert.Equal(8, player.Class);
        Assert.Equal(110, player.Level);
        Assert.Equal(12345, player.AchievementPoints);
        Assert.Equal(678, player.HonorableKills);
        Assert.Equal("Alliance", player.Faction);
        Assert.Equal("Example Guild", player.Guild);
        Assert.Equal("2 years 1 months 2 days", player.CharacterAge);
    }

    [Fact]
    public void CreatePlayer_MissingOptionalProperties_UsesSafeDefaults()
    {
        var player = CharacterResponseMapper.CreatePlayer(
            Parse("{}"),
            new Dictionary<int, DateTimeOffset?>(),
            "Unknown",
            "Tauri",
            DateTimeOffset.UtcNow
        );

        Assert.Equal(0, player.Race);
        Assert.Equal(0, player.AchievementPoints);
        Assert.Equal(string.Empty, player.Guild);
        Assert.Equal(string.Empty, player.CharacterAge);
    }

    [Fact]
    public void ApplyMinimalSheet_MissingFields_ResetsValuesToDefaults()
    {
        var player = new Player { PlayedTime = 10, AchievementsTotal = 20 };

        CharacterResponseMapper.ApplyMinimalSheet(Parse("{}"), player);

        Assert.Equal(0, player.PlayedTime);
        Assert.Equal(0, player.AchievementsTotal);
    }

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();
}
