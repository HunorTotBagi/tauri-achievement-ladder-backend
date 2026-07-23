using System.Collections.Concurrent;
using System.Text;
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

        var characterResults = new ConcurrentDictionary<string, CharacterScanResult>(
            StringComparer.OrdinalIgnoreCase
        );
        var processedPlayerCount = 0;

        await Parallel.ForEachAsync(
            playerNames,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = 20,
                CancellationToken = cancellationToken,
            },
            async (playerName, ct) =>
            {
                var reputation = await LoadNightfallenReputationAsync(
                    apiRealmName,
                    playerName,
                    ct
                );
                var artifact = reputation.IsLevel110
                    ? await LoadArtifactDetailsAsync(apiRealmName, playerName, ct)
                    : ArtifactResult.Excluded;

                characterResults[playerName] = new CharacterScanResult(reputation, artifact);

                var processed = Interlocked.Increment(ref processedPlayerCount);
                if (processed % 25 == 0 || processed == playerNames.Count)
                {
                    Console.WriteLine(
                        $"Scanned character reputations and artifacts {processed}/{playerNames.Count}"
                    );
                }
            }
        );

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
        var fileName = $"{MakeFileNamePart(guildName)}-{timestamp}.txt";
        var outputPath = Path.Combine(_outputDirectory, fileName);
        await WriteLinesAsync(outputPath, outputLines, cancellationToken);

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

        var separator =
            $"{new string('-', numberWidth)}-+-{new string('-', characterWidth)}-+-{new string('-', repWidth)}-+-{new string('-', maxWidth)}-+-{new string('-', relicsWidth)}-+-{new string('-', traitsWidth)}";
        var capLabel = " CAP: 8000 / 12000 ";
        var capSeparator =
            capLabel.Length >= separator.Length
                ? capLabel
                : capLabel.PadLeft((separator.Length + capLabel.Length) / 2, '-')
                    .PadRight(separator.Length, '-');

        var lines = new List<string>
        {
            $"{PadLeft("No.", numberWidth)} | {PadRight("Character", characterWidth)} | {PadLeft("Rep", repWidth)} | {PadLeft("Max", maxWidth)} | {PadLeft("Relics", relicsWidth)} | {PadLeft("Trait", traitsWidth)}",
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

            lines.Add(
                $"{PadLeft((index + 1).ToString(), numberWidth)} | {PadRight(row.PlayerName, characterWidth)} | {PadLeft(rep, repWidth)} | {PadLeft(max, maxWidth)} | {PadLeft(relics, relicsWidth)} | {PadLeft(traits, traitsWidth)}"
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

    private readonly record struct CharacterScanResult(
        ReputationResult Reputation,
        ArtifactResult Artifact
    );

    private sealed record OutputRow(string PlayerName, CharacterScanResult Result)
    {
        public ReputationResult Reputation => Result.Reputation;
    }
}
