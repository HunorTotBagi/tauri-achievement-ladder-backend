using System.Text;
using System.Text.Json;
using EndlessGuildExporter;
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
            ItemLevelResult itemLevel;
            if (reputation.IsLevel110)
            {
                artifact = await LoadArtifactDetailsAsync(
                    apiRealmName,
                    playerName,
                    cancellationToken
                );
                itemLevel = await LoadItemLevelAsync(
                    apiRealmName,
                    playerName,
                    cancellationToken
                );
            }
            else
            {
                artifact = ArtifactResult.Excluded;
                itemLevel = ItemLevelResult.Excluded;
            }

            characterResults[playerName] = new CharacterScanResult(
                reputation,
                artifact,
                itemLevel
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
        var outputLines = BuildOutputLines(sortedRows);

        Console.WriteLine();
        foreach (var line in outputLines)
        {
            Console.WriteLine(line);
        }
        Console.WriteLine();

        var timestamp = DateTimeOffset.Now.ToString(
            "dd-MMM-yyyy-HH-mm",
            System.Globalization.CultureInfo.InvariantCulture
        );
        var outputName = $"{MakeFileNamePart(guildName)}-{timestamp}";
        var outputPath = Path.Combine(_outputDirectory, outputName + ".txt");
        var spreadsheetOutputPath = Path.Combine(_outputDirectory, outputName + ".xlsx");
        await WriteLinesAsync(outputPath, outputLines, cancellationToken);
        await WriteSpreadsheetAsync(spreadsheetOutputPath, guildName, sortedRows, cancellationToken);

        var level110Count = characterResults.Values.Count(result =>
            result.Reputation.IsLevel110
        );
        var reputationCount = characterResults.Values.Count(result => result.Reputation.Found);
        return new GuildMemberExportResult(
            playerNames.Count,
            level110Count,
            reputationCount,
            level110Count - reputationCount,
            outputPath,
            spreadsheetOutputPath
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

    private async Task<ItemLevelResult> LoadItemLevelAsync(
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
            || !response.TryGetProperty("characterItems", out var characterItems)
            || characterItems.ValueKind != JsonValueKind.Array
        )
        {
            return ItemLevelResult.Missing;
        }

        var equippedItems = characterItems
            .EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.Object)
            .Select(item => new EquippedItem(
                ReadInt(item, "InventoryType"),
                ReadInt(item, "ilevel"),
                ReadInt(item, "rarity"),
                item.TryGetProperty("artifact", out var artifact)
                    && artifact.ValueKind == JsonValueKind.Object
            ))
            .Where(item => IsCombatEquipment(item.InventoryType) && item.ItemLevel > 0)
            .ToList();

        if (equippedItems.Count == 0)
        {
            return ItemLevelResult.Missing;
        }

        var mainHand = equippedItems.FirstOrDefault(item =>
            item.InventoryType is 13 or 15 or 17 or 21 or 25 or 26
        );
        var offHandIndex = equippedItems.FindIndex(item => item.InventoryType is 14 or 22 or 23);
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

        // WoW counts a two-handed weapon in both the main-hand and off-hand slots.
        if (
            mainHand is not null
            && mainHand.InventoryType is 17 or 26
            && offHandIndex < 0
        )
        {
            itemLevelTotal += mainHand.ItemLevel;
            itemCount++;
        }

        return new ItemLevelResult(
            true,
            Math.Round((decimal)itemLevelTotal / itemCount, 2, MidpointRounding.AwayFromZero)
        );
    }

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

    private static IReadOnlyList<string> BuildOutputLines(IReadOnlyList<OutputRow> rows)
    {
        var numberWidth = Math.Max("No.".Length, rows.Count.ToString().Length);
        var characterWidth = Math.Max(
            "Character".Length,
            rows.Count == 0 ? 0 : rows.Max(row => row.PlayerName.Length)
        );
        var repWidth = Math.Max(
            "Rep".Length,
            rows
                .Where(row => row.Reputation.Found)
                .Select(row => row.Reputation.Reputation.ToString().Length)
                .DefaultIfEmpty("N/A".Length)
                .Max()
        );
        var maxWidth = Math.Max(
            "Max".Length,
            rows
                .Where(row => row.Reputation.Found)
                .Select(row => row.Reputation.Maximum.ToString().Length)
                .DefaultIfEmpty("N/A".Length)
                .Max()
        );
        var relicsWidth = Math.Max(
            "Relics".Length,
            rows
                .Where(row => row.Result.Artifact.Found)
                .Select(row => row.Result.Artifact.RelicCount.ToString().Length)
                .DefaultIfEmpty("N/A".Length)
                .Max()
        );
        var traitsWidth = Math.Max(
            "Trait".Length,
            rows
                .Where(row => row.Result.Artifact.Found)
                .Select(row => row.Result.Artifact.TraitCount.ToString().Length)
                .DefaultIfEmpty("N/A".Length)
                .Max()
        );
        var itemLevelWidth = Math.Max(
            "ilvl".Length,
            rows
                .Where(row => row.Result.ItemLevel.Found)
                .Select(row => FormatItemLevel(row.Result.ItemLevel.Average).Length)
                .DefaultIfEmpty("N/A".Length)
                .Max()
        );

        var separator =
            $"{new string('-', numberWidth)}-+-{new string('-', characterWidth)}-+-{new string('-', repWidth)}-+-{new string('-', maxWidth)}-+-{new string('-', relicsWidth)}-+-{new string('-', traitsWidth)}-+-{new string('-', itemLevelWidth)}";
        var capLabel = " CAP: 8000 / 12000 ";
        var capSeparator =
            capLabel.Length >= separator.Length
                ? capLabel
                : capLabel.PadLeft((separator.Length + capLabel.Length) / 2, '-')
                    .PadRight(separator.Length, '-');

        var lines = new List<string>
        {
            $"{PadLeft("No.", numberWidth)} | {PadRight("Character", characterWidth)} | {PadLeft("Rep", repWidth)} | {PadLeft("Max", maxWidth)} | {PadLeft("Relics", relicsWidth)} | {PadLeft("Trait", traitsWidth)} | {PadLeft("ilvl", itemLevelWidth)}",
            separator,
        };

        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var rep = row.Reputation.Found ? row.Reputation.Reputation.ToString() : "N/A";
            var max = row.Reputation.Found ? row.Reputation.Maximum.ToString() : "N/A";
            var relics = row.Result.Artifact.Found
                ? row.Result.Artifact.RelicCount.ToString()
                : "N/A";
            var traits = row.Result.Artifact.Found
                ? row.Result.Artifact.TraitCount.ToString()
                : "N/A";
            var itemLevel = row.Result.ItemLevel.Found
                ? FormatItemLevel(row.Result.ItemLevel.Average)
                : "N/A";

            lines.Add(
                $"{PadLeft((index + 1).ToString(), numberWidth)} | {PadRight(row.PlayerName, characterWidth)} | {PadLeft(rep, repWidth)} | {PadLeft(max, maxWidth)} | {PadLeft(relics, relicsWidth)} | {PadLeft(traits, traitsWidth)} | {PadLeft(itemLevel, itemLevelWidth)}"
            );

            var nextRow = index + 1 < rows.Count ? rows[index + 1] : null;
            var crossesCap =
                row.Reputation.Found
                && row.Reputation.Maximum == 12000
                && row.Reputation.Reputation >= 8000
                && (
                    nextRow is null
                    || !nextRow.Reputation.Found
                    || nextRow.Reputation.Maximum != 12000
                    || nextRow.Reputation.Reputation < 8000
                );

            if (crossesCap)
            {
                lines.Add(capSeparator);
            }
            else if (
                nextRow is not null
                && GetMaximumSortOrder(row.Reputation)
                    != GetMaximumSortOrder(nextRow.Reputation)
            )
            {
                lines.Add(separator);
            }
        }

        return lines;
    }

    private static string PadLeft(string value, int width) => value.PadLeft(width);

    private static string PadRight(string value, int width) => value.PadRight(width);

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

    private static async Task WriteLinesAsync(
        string outputPath,
        IReadOnlyList<string> lines,
        CancellationToken cancellationToken
    )
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        var temporaryPath = outputPath + ".tmp";

        try
        {
            await File.WriteAllLinesAsync(
                temporaryPath,
                lines,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken
            );
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

    private static Task WriteSpreadsheetAsync(
        string outputPath,
        string guildName,
        IReadOnlyList<OutputRow> rows,
        CancellationToken cancellationToken
    )
    {
        var header = new List<SimpleXlsxWriter.CellData>
        {
            new("No."),
            new("Character"),
            new("Rep"),
            new("Max"),
            new("Relics"),
            new("Trait"),
            new("ilvl"),
        };
        var sheetRows = rows
            .Select(
                (row, index) =>
                    (IReadOnlyList<SimpleXlsxWriter.CellData>)
                    [
                        NumberCell(index + 1),
                        new(row.PlayerName),
                        ResultCell(row.Reputation.Found, row.Reputation.Reputation),
                        ResultCell(row.Reputation.Found, row.Reputation.Maximum),
                        ResultCell(row.Result.Artifact.Found, row.Result.Artifact.RelicCount),
                        ResultCell(row.Result.Artifact.Found, row.Result.Artifact.TraitCount),
                        ItemLevelCell(row.Result.ItemLevel),
                    ]
            )
            .ToList();

        return SimpleXlsxWriter.WriteSingleWorksheetAsync(
            outputPath,
            MakeWorksheetName(guildName),
            header,
            sheetRows,
            cellStyles: null,
            dataValidations: null,
            autoFilterRef: null,
            cancellationToken
        );
    }

    private static SimpleXlsxWriter.CellData ResultCell(bool found, int value) =>
        found ? NumberCell(value) : new("N/A");

    private static SimpleXlsxWriter.CellData ItemLevelCell(ItemLevelResult result) =>
        result.Found
            ? new(
                FormatItemLevel(result.Average),
                ValueKind: SimpleXlsxWriter.CellValueKind.Number
            )
            : new("N/A");

    private static SimpleXlsxWriter.CellData NumberCell(int value) =>
        new(
            value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ValueKind: SimpleXlsxWriter.CellValueKind.Number
        );

    private static string FormatItemLevel(decimal value) =>
        value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

    private static string MakeWorksheetName(string value)
    {
        char[] invalidCharacters = ['[', ']', ':', '*', '?', '/', '\\'];
        var sanitized = new string(
            value.Trim().Where(character => !invalidCharacters.Contains(character)).ToArray()
        );
        return string.IsNullOrWhiteSpace(sanitized)
            ? "Guild"
            : sanitized[..Math.Min(sanitized.Length, 31)];
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

    private readonly record struct ItemLevelResult(bool Found, decimal Average)
    {
        public static ItemLevelResult Excluded => new(false, 0);

        public static ItemLevelResult Missing => new(false, 0);
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
        ItemLevelResult ItemLevel
    );

    private sealed record OutputRow(string PlayerName, CharacterScanResult Result)
    {
        public ReputationResult Reputation => Result.Reputation;
    }
}
