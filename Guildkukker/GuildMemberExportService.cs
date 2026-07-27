using System.Text.Json;
using Tauri.Core.Dtos;
using Tauri.Core.Infrastructure;

namespace Guildkukker;

public sealed class GuildMemberExportService(string outputDirectory, ITauriApiClient apiClient)
{
    private readonly string _outputDirectory = Path.GetFullPath(outputDirectory);
    private readonly ITauriApiClient _apiClient = apiClient;

    public async Task<GuildMemberExportResult> ExportAsync(
        string realmName,
        string guildName,
        CancellationToken cancellationToken
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(realmName);
        ArgumentException.ThrowIfNullOrWhiteSpace(guildName);

        var apiRealmName = ResolveApiRealmName(realmName);
        var result = await _apiClient.FetchResponseElementAsync(
            "guild-info",
            new { r = apiRealmName, gn = guildName },
            $"guild '{guildName}' on {apiRealmName}",
            cancellationToken
        );

        if (!result.Succeeded || result.ResponseElement is not { } response)
        {
            throw new InvalidOperationException(
                result.FailureMessage ?? $"Could not load guild '{guildName}' on {realmName}."
            );
        }

        if (
            response.ValueKind != JsonValueKind.Object
            || !response.TryGetProperty("guildList", out var guildList)
            || guildList.ValueKind != JsonValueKind.Object
        )
        {
            throw new InvalidDataException("The API response did not contain a guildList object.");
        }

        GuildInfoInner guildInfo;
        try
        {
            guildInfo =
                response.Deserialize<GuildInfoInner>()
                ?? throw new InvalidDataException("The guild response was empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("The guild response could not be parsed.", ex);
        }

        var playerNames = guildInfo
            .guildList.Values.Select(member => member.name?.Trim())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Console.WriteLine($"Loading The Nightfallen reputation for {playerNames.Count} players...");

        var characterResults = new Dictionary<string, CharacterScanResult>(
            StringComparer.OrdinalIgnoreCase
        );
        var processedPlayerCount = 0;

        foreach (var playerName in playerNames)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var reputation = await LoadNightfallenReputationAsync(
                apiRealmName,
                playerName,
                cancellationToken
            );
            ArtifactResult artifact;
            CharacterDetailsResult details;
            if (reputation.IsLevel110)
            {
                artifact = await LoadArtifactDetailsAsync(
                    apiRealmName,
                    playerName,
                    cancellationToken
                );
                details = await LoadCharacterDetailsAsync(
                    apiRealmName,
                    playerName,
                    cancellationToken
                );
            }
            else
            {
                artifact = ArtifactResult.Excluded;
                details = CharacterDetailsResult.Excluded;
            }

            characterResults[playerName] = new CharacterScanResult(
                reputation,
                artifact,
                details
            );

            processedPlayerCount++;
            if (processedPlayerCount % 25 == 0 || processedPlayerCount == playerNames.Count)
            {
                Console.WriteLine(
                    $"Scanned character reputations, artifacts, and item levels {processedPlayerCount}/{playerNames.Count}"
                );
            }
        }

        var sortedRows = playerNames
            .Where(playerName => characterResults[playerName].Reputation.IsLevel110)
            .Select(playerName => new OutputRow(playerName, characterResults[playerName]))
            .OrderBy(row => GetMaximumSortOrder(row.Reputation))
            .ThenByDescending(row => row.Reputation.Reputation)
            .ThenBy(row => row.PlayerName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var outputName = MakeFileNamePart(guildName).ToLowerInvariant();
        var outputPath = Path.Combine(_outputDirectory, outputName + ".json");
        await WriteJsonAsync(outputPath, sortedRows, cancellationToken);

        var level110Count = characterResults.Values.Count(result =>
            result.Reputation.IsLevel110
        );
        var reputationCount = characterResults.Values.Count(result => result.Reputation.Found);
        return new GuildMemberExportResult(
            playerNames.Count,
            level110Count,
            reputationCount,
            level110Count - reputationCount,
            outputPath
        );
    }

    private async Task<ArtifactResult> LoadArtifactDetailsAsync(
        string realmName,
        string playerName,
        CancellationToken cancellationToken
    )
    {
        var result = await _apiClient.FetchResponseElementAsync(
            "character-artifact",
            new { r = realmName, n = playerName },
            $"character artifact for '{playerName}' on {realmName}",
            cancellationToken
        );

        if (
            !result.Succeeded
            || result.ResponseElement is not { } response
            || response.ValueKind != JsonValueKind.Object
        )
        {
            return ArtifactResult.Missing;
        }

        if (
            !response.TryGetProperty("artifacts", out var artifacts)
            || artifacts.ValueKind != JsonValueKind.Array
        )
        {
            return ArtifactResult.Missing;
        }

        if (artifacts.GetArrayLength() == 0)
        {
            return new ArtifactResult(true, 0, 0);
        }

        var artifact = artifacts[0];
        if (artifact.ValueKind != JsonValueKind.Object)
        {
            return ArtifactResult.Missing;
        }

        var relicCount =
            artifact.TryGetProperty("SocketContainedGem", out var gems)
            && gems.ValueKind == JsonValueKind.Array
                ? gems.GetArrayLength()
                : 0;

        var traitCount = 0;
        if (
            artifact.TryGetProperty("artifact", out var artifactInfo)
            && artifactInfo.ValueKind == JsonValueKind.Object
            && artifactInfo.TryGetProperty("artifactpowers", out var artifactPowers)
            && artifactPowers.ValueKind == JsonValueKind.Array
        )
        {
            foreach (var artifactPower in artifactPowers.EnumerateArray())
            {
                if (
                    artifactPower.ValueKind == JsonValueKind.Object
                    && artifactPower.TryGetProperty("purchasedrank", out var purchasedRank)
                    && purchasedRank.TryGetInt32(out var rank)
                )
                {
                    traitCount += rank;
                }
            }
        }

        return new ArtifactResult(true, relicCount, traitCount);
    }

    private async Task<CharacterDetailsResult> LoadCharacterDetailsAsync(
        string realmName,
        string playerName,
        CancellationToken cancellationToken
    )
    {
        var result = await _apiClient.FetchResponseElementAsync(
            "character-sheet",
            new { r = realmName, n = playerName },
            $"character sheet for '{playerName}' on {realmName}",
            cancellationToken
        );

        if (
            !result.Succeeded
            || result.ResponseElement is not { } response
            || response.ValueKind != JsonValueKind.Object
        )
        {
            return CharacterDetailsResult.Missing;
        }

        var equippedItems =
            response.TryGetProperty("characterItems", out var characterItems)
            && characterItems.ValueKind == JsonValueKind.Array
            ? characterItems.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.Object)
            .Select(item => new EquippedItem(
                ReadInt(item, "InventoryType"),
                ReadInt(item, "ilevel"),
                ReadInt(item, "rarity"),
                item.TryGetProperty("artifact", out var artifact)
                    && artifact.ValueKind == JsonValueKind.Object
            ))
            .Where(item => IsCombatEquipment(item.InventoryType) && item.ItemLevel > 0)
            .ToList()
            : [];

        decimal? averageItemLevel = null;
        if (equippedItems.Count > 0)
        {
            var mainHand = equippedItems.FirstOrDefault(item =>
                item.InventoryType is 13 or 15 or 17 or 21 or 25 or 26
            );
            var offHandIndex = equippedItems.FindIndex(item =>
                item.InventoryType is 14 or 22 or 23
            );
            if (
                mainHand is not null
                && mainHand.IsArtifact
                && offHandIndex >= 0
                && equippedItems[offHandIndex] is { ItemLevel: 750, Rarity: 6 }
            )
            {
                equippedItems[offHandIndex] = equippedItems[offHandIndex] with
                {
                    ItemLevel = mainHand.ItemLevel,
                };
            }

            var itemLevelTotal = equippedItems.Sum(item => item.ItemLevel);
            var itemCount = equippedItems.Count;
            if (mainHand is not null && mainHand.InventoryType is 17 or 26 && offHandIndex < 0)
            {
                itemLevelTotal += mainHand.ItemLevel;
                itemCount++;
            }

            averageItemLevel = Math.Round(
                (decimal)itemLevelTotal / itemCount,
                2,
                MidpointRounding.AwayFromZero
            );
        }

        return new CharacterDetailsResult(
            true,
            ReadInt(response, "race"),
            ReadInt(response, "gender"),
            ReadInt(response, "class"),
            ReadLong(response, "played_time"),
            ReadInt(response, "pts"),
            averageItemLevel
        );
    }

    private static long ReadLong(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property)
        && property.TryGetInt64(out var value)
            ? value
            : 0;

    private static bool IsCombatEquipment(int inventoryType) =>
        inventoryType is
            1
            or 2
            or 3
            or 5
            or 6
            or 7
            or 8
            or 9
            or 10
            or 11
            or 12
            or 13
            or 14
            or 15
            or 16
            or 17
            or 21
            or 22
            or 23
            or 25
            or 26;

    private static int ReadInt(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property)
        && property.TryGetInt32(out var value)
            ? value
            : 0;

    private async Task<ReputationResult> LoadNightfallenReputationAsync(
        string realmName,
        string playerName,
        CancellationToken cancellationToken
    )
    {
        var result = await _apiClient.FetchResponseElementAsync(
            "character-reputation",
            new { r = realmName, n = playerName },
            $"character reputation for '{playerName}' on {realmName}",
            cancellationToken
        );

        if (!result.Succeeded || result.ResponseElement is not { } response)
        {
            return ReputationResult.Excluded;
        }

        if (
            response.ValueKind != JsonValueKind.Object
            || !response.TryGetProperty("level", out var level)
            || !level.TryGetInt32(out var levelValue)
            || levelValue != 110
        )
        {
            return ReputationResult.Excluded;
        }

        if (
            !response.TryGetProperty("characterReputation", out var reputations)
            || reputations.ValueKind != JsonValueKind.Array
        )
        {
            return ReputationResult.Missing;
        }

        foreach (var reputation in reputations.EnumerateArray())
        {
            if (
                reputation.ValueKind != JsonValueKind.Object
                || !reputation.TryGetProperty("name", out var name)
                || !string.Equals(
                    name.GetString(),
                    "The Nightfallen",
                    StringComparison.OrdinalIgnoreCase
                )
                || !reputation.TryGetProperty("standings", out var standings)
                || standings.ValueKind != JsonValueKind.Object
                || !standings.TryGetProperty("rep", out var reputationValue)
                || !reputationValue.TryGetInt32(out var reputationAmount)
                || !standings.TryGetProperty("max", out var maximumValue)
                || !maximumValue.TryGetInt32(out var maximumAmount)
            )
            {
                continue;
            }

            return new ReputationResult(true, true, reputationAmount, maximumAmount);
        }

        return ReputationResult.Missing;
    }

    private static string ResolveApiRealmName(string realmName) =>
        realmName.Trim().ToLowerInvariant() switch
        {
            "evermoon" => "[EN] Evermoon",
            "tauri" => "[HU] Tauri WoW Server",
            "wod" => "[HU] Warriors of Darkness",
            _ => realmName.Trim(),
        };

    private static int GetMaximumSortOrder(ReputationResult reputation)
    {
        if (!reputation.Found)
        {
            return 5;
        }

        return reputation.Maximum switch
        {
            21000 => 0,
            12000 => 1,
            6000 => 2,
            3000 => 3,
            _ => 4,
        };
    }

    private static string MakeFileNamePart(string value)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars().ToHashSet();
        var sanitized = new string(
            value
                .Trim()
                .Select(character =>
                    invalidCharacters.Contains(character) || char.IsWhiteSpace(character)
                        ? '-'
                        : character
                )
                .ToArray()
        );

        var fileNamePart = sanitized.Trim('-', '.');
        if (string.IsNullOrWhiteSpace(fileNamePart))
        {
            throw new ArgumentException($"'{value}' cannot be used as part of a file name.");
        }

        return fileNamePart;
    }

    private static async Task WriteJsonAsync(
        string outputPath,
        IReadOnlyList<OutputRow> rows,
        CancellationToken cancellationToken
    )
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        var temporaryPath = outputPath + ".tmp";
        var data = new GuildExport(
            GetCentralEuropeanTimestamp(),
            rows.Select(row => new CharacterExport(
                    row.PlayerName,
                    row.Result.Details.Race,
                    row.Result.Details.Gender,
                    row.Result.Details.Class,
                    row.Result.Details.PlayedTime,
                    row.Result.Details.AchievementPoints,
                    row.Reputation.Found ? row.Reputation.Reputation : null,
                    row.Reputation.Found ? row.Reputation.Maximum : null,
                    row.Result.Artifact.Found ? row.Result.Artifact.RelicCount : null,
                    row.Result.Artifact.Found ? row.Result.Artifact.TraitCount : null,
                    row.Result.Details.ItemLevel
                ))
                .ToList()
        );

        try
        {
            await using var stream = File.Create(temporaryPath);
            await JsonSerializer.SerializeAsync(
                stream,
                data,
                new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = true,
                },
                cancellationToken
            );
            await stream.FlushAsync(cancellationToken);
            stream.Close();
            File.Move(temporaryPath, outputPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static string GetCentralEuropeanTimestamp()
    {
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById("Central European Standard Time");
        var centralEuropeanTime = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, timeZone);
        return centralEuropeanTime.ToString(
            "yyyy-MM-dd HH:mm:ss 'CET'",
            System.Globalization.CultureInfo.InvariantCulture
        );
    }

    private readonly record struct ReputationResult(
        bool IsLevel110,
        bool Found,
        int Reputation,
        int Maximum
    )
    {
        public static ReputationResult Excluded => new(false, false, 0, 0);

        public static ReputationResult Missing => new(true, false, 0, 0);
    }

    private readonly record struct ArtifactResult(
        bool Found,
        int RelicCount,
        int TraitCount
    )
    {
        public static ArtifactResult Excluded => new(false, 0, 0);

        public static ArtifactResult Missing => new(false, 0, 0);
    }

    private readonly record struct CharacterDetailsResult(
        bool Found,
        int Race,
        int Gender,
        int Class,
        long PlayedTime,
        int AchievementPoints,
        decimal? ItemLevel
    )
    {
        public static CharacterDetailsResult Excluded => new(false, 0, 0, 0, 0, 0, null);

        public static CharacterDetailsResult Missing => new(false, 0, 0, 0, 0, 0, null);
    }

    private sealed record EquippedItem(
        int InventoryType,
        int ItemLevel,
        int Rarity,
        bool IsArtifact
    );

    private readonly record struct CharacterScanResult(
        ReputationResult Reputation,
        ArtifactResult Artifact,
        CharacterDetailsResult Details
    );

    private sealed record OutputRow(string PlayerName, CharacterScanResult Result)
    {
        public ReputationResult Reputation => Result.Reputation;
    }

    private sealed record GuildExport(
        string Timestamp,
        IReadOnlyList<CharacterExport> Players
    );

    private sealed record CharacterExport(
        string Name,
        int Race,
        int Gender,
        int Class,
        long PlayedTime,
        int AchievementPoints,
        int? NightfallenReputation,
        int? NightfallenReputationMaximum,
        int? ArtifactRelics,
        int? ArtifactTraits,
        decimal? ItemLevel
    );
}
