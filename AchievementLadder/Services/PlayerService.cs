using System.Text;
using System.Text.Json;
using AchievementLadder.Configuration;
using AchievementLadder.Helpers;
using AchievementLadder.Models;

namespace AchievementLadder.Services;

public class PlayerService(string projectRoot, TauriApiOptions apiOptions, PlayerCsvStore csvStore)
{
    public async Task<SyncResult> SyncDataAsync(CancellationToken cancellationToken)
    {
        using var client = new HttpClient();
        string apiUrl = BuildApiUrl(apiOptions.BaseUrl, apiOptions.ApiKey);
        string secret = apiOptions.Secret;

        var allCharacters = new List<(string Name, string ApiRealm, string DisplayRealm)>();

        CharacterHelpers.LoadCharacters(projectRoot, "evermoon-achi.txt", "[EN] Evermoon", "Evermoon", allCharacters);
        CharacterHelpers.LoadCharacters(projectRoot, "evermoon-hk.txt", "[EN] Evermoon", "Evermoon", allCharacters);
        CharacterHelpers.LoadCharacters(projectRoot, "evermoon-playTime.txt", "[EN] Evermoon", "Evermoon", allCharacters);

        CharacterHelpers.LoadCharacters(projectRoot, "tauri-achi.txt", "[HU] Tauri WoW Server", "Tauri", allCharacters);
        CharacterHelpers.LoadCharacters(projectRoot, "tauri-hk.txt", "[HU] Tauri WoW Server", "Tauri", allCharacters);
        CharacterHelpers.LoadCharacters(projectRoot, "tauri-playTime.txt", "[HU] Tauri WoW Server", "Tauri", allCharacters);

        CharacterHelpers.LoadCharacters(projectRoot, "wod-achi.txt", "[HU] Warriors of Darkness", "WoD", allCharacters);
        CharacterHelpers.LoadCharacters(projectRoot, "wod-hk.txt", "[HU] Warriors of Darkness", "WoD", allCharacters);
        CharacterHelpers.LoadCharacters(projectRoot, "wod-playTime.txt", "[HU] Warriors of Darkness", "WoD", allCharacters);

        await GuildHelpers.LoadGuildMembersFromFilesAsync(
            projectRoot,
            apiUrl,
            secret,
            client,
            allCharacters,
            cancellationToken);

        var distinctCharacters = allCharacters
            .Where(x => !string.IsNullOrWhiteSpace(x.Name) && !x.Name.Contains('#'))
            .Distinct()
            .ToList();

        var players = new List<Player>(distinctCharacters.Count);
        int done = 0;

        foreach (var character in distinctCharacters)
        {
            var player = await FetchPlayerAsync(
                client,
                apiUrl,
                secret,
                character.Name,
                character.ApiRealm,
                character.DisplayRealm,
                cancellationToken);

            if (player is not null)
            {
                players.Add(player);
            }

            done++;
            if (done % 250 == 0)
            {
                Console.WriteLine($"Fetched {done}/{distinctCharacters.Count}");
            }
        }

        var orderedPlayers = players
            .OrderByDescending(player => player.AchievementPoints)
            .ThenByDescending(player => player.HonorableKills)
            .ThenBy(player => player.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(player => player.Realm, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var playersCsvPath = await csvStore.WriteAsync(orderedPlayers, "Players.csv", cancellationToken);
        csvStore.DeleteIfExists("lastUpdated.txt");

        return new SyncResult(orderedPlayers.Count, playersCsvPath);
    }

    private static async Task<Player?> FetchPlayerAsync(
        HttpClient client,
        string apiUrl,
        string secret,
        string name,
        string apiRealm,
        string displayRealm,
        CancellationToken ct)
    {
        var body = new
        {
            secret,
            url = "character-sheet",
            @params = new { r = apiRealm, n = name }
        };

        using var content = new StringContent(
            JsonSerializer.Serialize(body),
            Encoding.UTF8,
            "application/json");

        using var response = await client.PostAsync(apiUrl, content, ct);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        var root = doc.RootElement;
        if (!root.TryGetProperty("response", out var responseElement))
        {
            return null;
        }

        int race = responseElement.TryGetProperty("race", out var value) ? value.GetInt32() : 0;
        int gender = responseElement.TryGetProperty("gender", out value) ? value.GetInt32() : 0;
        int @class = responseElement.TryGetProperty("class", out value) ? value.GetInt32() : 0;
        int achievementPoints = responseElement.TryGetProperty("pts", out value) ? value.GetInt32() : 0;
        int honorableKills = responseElement.TryGetProperty("playerHonorKills", out value) ? value.GetInt32() : 0;
        string faction = responseElement.TryGetProperty("faction_string_class", out value) ? (value.GetString() ?? string.Empty) : string.Empty;
        string guild = responseElement.TryGetProperty("guildName", out value) ? (value.GetString() ?? string.Empty) : string.Empty;

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
            Faction = faction
        };
    }

    private static string BuildApiUrl(string baseUrl, string apiKey)
    {
        var separator = baseUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return $"{baseUrl}{separator}apikey={Uri.EscapeDataString(apiKey)}";
    }

    public static class GuildHelpers
    {
        public static IEnumerable<(string GuildName, string ApiRealm, string DisplayRealm)> LoadGuilds(
            string projectRoot,
            string fileName,
            string apiRealm,
            string displayRealm)
        {
            var path = Path.Combine(projectRoot, "Data", "Guilds", fileName);
            if (!File.Exists(path))
            {
                yield break;
            }

            foreach (var raw in File.ReadLines(path))
            {
                var line = raw?.Trim();
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                yield return (line, apiRealm, displayRealm);
            }
        }

        public static async Task LoadGuildMembersFromFilesAsync(
            string projectRoot,
            string apiUrl,
            string secret,
            HttpClient client,
            List<(string Name, string ApiRealm, string DisplayRealm)> allCharacters,
            CancellationToken ct)
        {
            var guilds = new List<(string GuildName, string ApiRealm, string DisplayRealm)>();

            guilds.AddRange(LoadGuilds(projectRoot, "evermoon-guilds.txt", "[EN] Evermoon", "Evermoon"));
            guilds.AddRange(LoadGuilds(projectRoot, "tauri-guilds.txt", "[HU] Tauri WoW Server", "Tauri"));
            guilds.AddRange(LoadGuilds(projectRoot, "wod-guilds.txt", "[HU] Warriors of Darkness", "WoD"));

            var distinctGuilds = guilds
                .DistinctBy(guild => (guild.GuildName.ToLowerInvariant(), guild.ApiRealm))
                .ToList();

            int done = 0;

            foreach (var guild in distinctGuilds)
            {
                await CharacterHelpers.LoadGuildMembersLevel90Async(
                    guild.GuildName,
                    guild.ApiRealm,
                    guild.DisplayRealm,
                    apiUrl,
                    secret,
                    allCharacters,
                    client,
                    ct);

                done++;
                if (done % 25 == 0)
                {
                    Console.WriteLine($"Loaded guilds {done}/{distinctGuilds.Count}");
                }
            }
        }
    }
}
