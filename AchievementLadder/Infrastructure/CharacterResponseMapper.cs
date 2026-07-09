using System.Text.Json;
using AchievementLadder.Models;

namespace AchievementLadder.Infrastructure;

public static class CharacterResponseMapper
{
    // "Level 10" achievement; its obtained date marks when the character was created/started.
    private const int Level10AchievementId = 6;

    public static Player CreatePlayer(
        JsonElement response,
        string name,
        string displayRealm,
        DateTimeOffset scanStartedAt
    )
    {
        int race = response.TryGetProperty("race", out var value) ? value.GetInt32() : 0;
        int gender = response.TryGetProperty("gender", out value) ? value.GetInt32() : 0;
        int @class = response.TryGetProperty("class", out value) ? value.GetInt32() : 0;
        int achievementPoints = response.TryGetProperty("pts", out value) ? value.GetInt32() : 0;
        int honorableKills = response.TryGetProperty("playerHonorKills", out value)
            ? value.GetInt32()
            : 0;
        string faction = response.TryGetProperty("faction_string_class", out value)
            ? (value.GetString() ?? string.Empty)
            : string.Empty;
        string guild = response.TryGetProperty("guildName", out value)
            ? (value.GetString() ?? string.Empty)
            : string.Empty;

        var level10ObtainedAt = RareAchievementExtractor.TryGetAchievementObtainedAt(
            response,
            Level10AchievementId
        );
        var characterAge = CharacterAgeCalculator.Format(level10ObtainedAt, scanStartedAt);

        return new Player
        {
            Name = name,
            Race = race,
            Gender = gender,
            Class = @class,
            Realm = displayRealm,
            Guild = guild,
            AchievementPoints = achievementPoints,
            HonorableKills = honorableKills,
            Faction = faction,
            CharacterAge = characterAge,
        };
    }
}
