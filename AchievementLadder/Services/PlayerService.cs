using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Globalization;
using AchievementLadder.Configuration;
using AchievementLadder.Helpers;
using AchievementLadder.Models;

namespace AchievementLadder.Services;

public class PlayerService(string projectRoot, TauriApiOptions apiOptions, PlayerCsvStore csvStore)
{
    private const int ProgressInterval = 250;
    private const int ProgressBarWidth = 30;
    private const int MaxDegreeOfParallelism = 20;

    public async Task<SyncResult> SyncDataAsync(CancellationToken cancellationToken)
    {
        using var client = new HttpClient();
        string apiUrl = BuildApiUrl(apiOptions.BaseUrl, apiOptions.ApiKey);
        string secret = apiOptions.Secret;

        var allCharacters = new List<(string Name, string ApiRealm, string DisplayRealm)>();
        LoadCharacterSources(allCharacters);

        var distinctCharacters = allCharacters
            .Where(x =>
                !string.IsNullOrWhiteSpace(x.Name) &&
                !x.Name.Contains('#') &&
                !string.IsNullOrWhiteSpace(x.ApiRealm) &&
                !string.IsNullOrWhiteSpace(x.DisplayRealm))
            .Distinct()
            .ToList();

        Console.WriteLine($"Scanning {distinctCharacters.Count} characters...");
        WriteProgress(0, distinctCharacters.Count);

        var players = new ConcurrentBag<Player>();
        var totalCharacters = distinctCharacters.Count;
        var done = 0;
        var progressLock = new Lock();

        await Parallel.ForEachAsync(
            distinctCharacters,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = MaxDegreeOfParallelism,
                CancellationToken = cancellationToken
            },
            async (character, ct) =>
            {
                var player = await FetchPlayerAsync(
                    client,
                    apiUrl,
                    secret,
                    character.Name,
                    character.ApiRealm,
                    character.DisplayRealm,
                    ct);

                if (player is not null)
                {
                    players.Add(player);
                }

                var processed = Interlocked.Increment(ref done);
                if (processed % ProgressInterval == 0 || processed == totalCharacters)
                {
                    lock (progressLock)
                    {
                        WriteProgress(processed, totalCharacters);
                    }
                }
            });

        if (totalCharacters > 0)
        {
            Console.WriteLine();
        }

        var orderedPlayers = players
            .OrderByDescending(player => player.AchievementPoints)
            .ThenByDescending(player => player.HonorableKills)
            .ThenBy(player => player.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(player => player.Realm, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var playersCsvPath = await csvStore.WriteAsync(orderedPlayers, "Players.csv", cancellationToken);
        var lastUpdatedPath = await csvStore.WriteTextAsync(
            "lastUpdated.txt",
            DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
            cancellationToken);

        return new SyncResult(orderedPlayers.Count, playersCsvPath, lastUpdatedPath);

        void LoadCharacterSources(List<(string Name, string ApiRealm, string DisplayRealm)> output)
        {
            CharacterHelpers.LoadGuildCharacters(projectRoot, "GuildCharacters.txt", output);

            CharacterHelpers.LoadCharacters(projectRoot, "evermoon-achi.txt", "[EN] Evermoon", "Evermoon", output);
            CharacterHelpers.LoadCharacters(projectRoot, "evermoon-hk.txt", "[EN] Evermoon", "Evermoon", output);
            CharacterHelpers.LoadCharacters(projectRoot, "evermoon-playTime.txt", "[EN] Evermoon", "Evermoon", output);

            CharacterHelpers.LoadCharacters(projectRoot, "tauri-achi.txt", "[HU] Tauri WoW Server", "Tauri", output);
            CharacterHelpers.LoadCharacters(projectRoot, "tauri-hk.txt", "[HU] Tauri WoW Server", "Tauri", output);
            CharacterHelpers.LoadCharacters(projectRoot, "tauri-playTime.txt", "[HU] Tauri WoW Server", "Tauri", output);

            CharacterHelpers.LoadCharacters(projectRoot, "wod-achi.txt", "[HU] Warriors of Darkness", "WoD", output);
            CharacterHelpers.LoadCharacters(projectRoot, "wod-hk.txt", "[HU] Warriors of Darkness", "WoD", output);
            CharacterHelpers.LoadCharacters(projectRoot, "wod-playTime.txt", "[HU] Warriors of Darkness", "WoD", output);
        }
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

    private static void WriteProgress(int processed, int total)
    {
        if (total <= 0)
        {
            Console.WriteLine("Progress: [------------------------------] 0/0 (100.0%)");
            return;
        }

        var ratio = (double)processed / total;
        var filledWidth = Math.Min(ProgressBarWidth, (int)Math.Round(ratio * ProgressBarWidth, MidpointRounding.AwayFromZero));
        var bar = new string('#', filledWidth) + new string('-', ProgressBarWidth - filledWidth);

        Console.Write($"\rProgress: [{bar}] {processed}/{total} ({ratio:P1})");
    }
}
