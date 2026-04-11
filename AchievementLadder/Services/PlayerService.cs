using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Globalization;
using AchievementLadder.Configuration;
using AchievementLadder.Helpers;
using AchievementLadder.Models;
using RareAchiAndItemScan;

namespace AchievementLadder.Services;

public class PlayerService(string projectRoot, TauriApiOptions apiOptions, PlayerCsvStore csvStore)
{
    private const int ProgressInterval = 250;
    private const int ProgressBarWidth = 30;
    private const int MaxDegreeOfParallelism = 20;
    private const int ApiRequestMaxAttempts = 3;
    private static readonly IReadOnlyList<RareAchievementDefinition> RareAchievementDefinitions = RareScanCatalog.RareAchievementNames
        .Select(entry => new RareAchievementDefinition(entry.Key, entry.Value))
        .ToList();
    private static readonly IReadOnlyList<TimeSpan> ApiRetryDelays =
    [
        TimeSpan.FromMilliseconds(300),
        TimeSpan.FromSeconds(1)
    ];

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
        var rareAchievementEntries = new ConcurrentBag<CharacterRareAchievementEntry>();
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
                var syncResult = await FetchCharacterSyncAsync(
                    client,
                    apiUrl,
                    secret,
                    character.Name,
                    character.ApiRealm,
                    character.DisplayRealm,
                    ct);

                if (syncResult is not null)
                {
                    players.Add(syncResult.Player);

                    if (syncResult.RareAchievements.Count > 0)
                    {
                        rareAchievementEntries.Add(new CharacterRareAchievementEntry(
                            syncResult.Player.Name,
                            syncResult.Player.Realm,
                            syncResult.Player.Race,
                            syncResult.Player.Gender,
                            syncResult.Player.Class,
                            syncResult.Player.Guild,
                            syncResult.RareAchievements));
                    }
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
        var orderedRareAchievementEntries = rareAchievementEntries
            .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Realm, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var generatedAt = DateTimeOffset.UtcNow;

        var playersCsvPath = await csvStore.WriteAsync(orderedPlayers, "Players.csv", cancellationToken);
        var rareAchievementsPath = await csvStore.WriteJsonAsync(
            "RareAchievements.json",
            new RareAchievementExport(
                generatedAt,
                RareAchievementDefinitions,
                orderedRareAchievementEntries),
            cancellationToken);
        var lastUpdatedPath = await csvStore.WriteTextAsync(
            "lastUpdated.txt",
            generatedAt.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
            cancellationToken);

        return new SyncResult(orderedPlayers.Count, playersCsvPath, rareAchievementsPath, lastUpdatedPath);

        void LoadCharacterSources(List<(string Name, string ApiRealm, string DisplayRealm)> output)
        {
            CharacterHelpers.LoadDefaultCharacterSources(
                projectRoot,
                output,
                includePvPSeasonCharacters: true);
        }
    }

    private static async Task<CharacterSyncResult?> FetchCharacterSyncAsync(
        HttpClient client,
        string apiUrl,
        string secret,
        string name,
        string apiRealm,
        string displayRealm,
        CancellationToken ct)
    {
        var playerTask = FetchPlayerAsync(client, apiUrl, secret, name, apiRealm, displayRealm, ct);
        var rareAchievementsTask = FetchRareAchievementsAsync(
            client,
            apiUrl,
            secret,
            name,
            apiRealm,
            displayRealm,
            ct);

        await Task.WhenAll(playerTask, rareAchievementsTask);

        var player = await playerTask;
        if (player is null)
        {
            return null;
        }

        return new CharacterSyncResult(player, await rareAchievementsTask);
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
        var responseElement = await FetchResponseElementAsync(
            client,
            apiUrl,
            secret,
            "character-sheet",
            new { r = apiRealm, n = name },
            $"{name}-{displayRealm}",
            ct);

        if (responseElement is null)
        {
            return null;
        }

        var response = responseElement.Value;
        int race = response.TryGetProperty("race", out var value) ? value.GetInt32() : 0;
        int gender = response.TryGetProperty("gender", out value) ? value.GetInt32() : 0;
        int @class = response.TryGetProperty("class", out value) ? value.GetInt32() : 0;
        int achievementPoints = response.TryGetProperty("pts", out value) ? value.GetInt32() : 0;
        int honorableKills = response.TryGetProperty("playerHonorKills", out value) ? value.GetInt32() : 0;
        string faction = response.TryGetProperty("faction_string_class", out value) ? (value.GetString() ?? string.Empty) : string.Empty;
        string guild = response.TryGetProperty("guildName", out value) ? (value.GetString() ?? string.Empty) : string.Empty;

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

    private static async Task<IReadOnlyList<CharacterRareAchievement>> FetchRareAchievementsAsync(
        HttpClient client,
        string apiUrl,
        string secret,
        string name,
        string apiRealm,
        string displayRealm,
        CancellationToken ct)
    {
        var responseElement = await FetchResponseElementAsync(
            client,
            apiUrl,
            secret,
            "character-achievements",
            new { r = apiRealm, n = name },
            $"{name}-{displayRealm}",
            ct);

        if (responseElement is null)
        {
            return Array.Empty<CharacterRareAchievement>();
        }

        var achievedAchievements = ExtractAchievements(responseElement.Value);
        return RareAchievementDefinitions
            .Where(entry => achievedAchievements.ContainsKey(entry.Id))
            .Select(entry => new CharacterRareAchievement(entry.Id, achievedAchievements[entry.Id]))
            .ToList();
    }

    private static async Task<JsonElement?> FetchResponseElementAsync(
        HttpClient client,
        string apiUrl,
        string secret,
        string endpoint,
        object parameters,
        string characterLabel,
        CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(new
        {
            secret,
            url = endpoint,
            @params = parameters
        });

        string? lastFailure = null;

        for (var attempt = 1; attempt <= ApiRequestMaxAttempts; attempt++)
        {
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");

            try
            {
                using var response = await client.PostAsync(apiUrl, content, ct);
                if (!response.IsSuccessStatusCode)
                {
                    lastFailure = $"API returned {(int)response.StatusCode} {response.ReasonPhrase}";
                }
                else
                {
                    await using var stream = await response.Content.ReadAsStreamAsync(ct);
                    using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

                    if (doc.RootElement.TryGetProperty("response", out var responseElement))
                    {
                        return responseElement.Clone();
                    }

                    lastFailure = "Missing response payload.";
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                lastFailure = "Request timed out.";
            }
            catch (Exception ex)
            {
                lastFailure = ex.Message;
            }

            if (attempt < ApiRequestMaxAttempts)
            {
                var delay = ApiRetryDelays[Math.Min(attempt - 1, ApiRetryDelays.Count - 1)];
                await Task.Delay(delay, ct);
            }
        }

        Console.Error.WriteLine($"[{endpoint}] Skipping {characterLabel}: {lastFailure ?? "Unknown failure."}");
        return null;
    }

    private static string BuildApiUrl(string baseUrl, string apiKey)
    {
        var separator = baseUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return $"{baseUrl}{separator}apikey={Uri.EscapeDataString(apiKey)}";
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

    private sealed record CharacterSyncResult(
        Player Player,
        IReadOnlyList<CharacterRareAchievement> RareAchievements);
}
