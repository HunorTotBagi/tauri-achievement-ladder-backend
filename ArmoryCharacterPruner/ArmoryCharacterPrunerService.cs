using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using AchievementLadder.Configuration;
using AchievementLadder.Helpers;
using AchievementLadder.Infrastructure;

namespace ArmoryCharacterPruner;

public sealed class ArmoryCharacterPrunerService(
    string solutionRoot,
    string projectRoot,
    TauriApiOptions apiOptions)
{
    private const int ProgressInterval = 100;

    private readonly string _solutionRoot = Path.GetFullPath(solutionRoot);
    private readonly string _projectRoot = Path.GetFullPath(projectRoot);
    private readonly TauriApiOptions _apiOptions = apiOptions;

    public async Task<ArmoryCharacterPrunerResult> PruneAsync(
        ArmoryCharacterPrunerOptions options,
        CancellationToken cancellationToken)
    {
        var inputPath = ProjectPaths.ResolveCharacterBatchFilePath(
            _solutionRoot,
            _projectRoot,
            options.NamesFilePath);
        var outputPath = string.IsNullOrWhiteSpace(options.OutputPath)
            ? inputPath
            : Path.GetFullPath(options.OutputPath, _projectRoot);

        var lines = await File.ReadAllLinesAsync(inputPath, cancellationToken);
        var entries = ParseInput(lines, inputPath, options.Realm, cancellationToken);
        var characters = entries
            .Where(entry => entry.Character is not null)
            .Select(entry => entry.Character!)
            .Distinct(new CharacterToScanComparer())
            .ToList();

        var lookupResults = await CheckCharactersAsync(
            characters,
            options.MaxDegreeOfParallelism,
            cancellationToken);

        var keptLines = new List<string>(entries.Count);
        var removedRowCount = 0;

        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (entry.Character is null)
            {
                keptLines.Add(entry.OriginalLine);
                continue;
            }

            if (!lookupResults.TryGetValue(entry.Character, out var result))
            {
                keptLines.Add(entry.OriginalLine);
                continue;
            }

            if (result.Status == CharacterLookupStatus.Missing)
            {
                removedRowCount++;
                continue;
            }

            keptLines.Add(entry.OriginalLine);
        }

        await WriteOutputAsync(outputPath, keptLines, cancellationToken);

        return new ArmoryCharacterPrunerResult(
            inputPath,
            outputPath,
            entries.Count,
            entries.Count(entry => entry.Character is not null),
            characters.Count,
            removedRowCount,
            keptLines.Count,
            entries.Count(entry => entry.WasUnparsed),
            lookupResults.Values.Count(result => result.Status == CharacterLookupStatus.Error));
    }

    private static List<InputEntry> ParseInput(
        IReadOnlyList<string> lines,
        string inputPath,
        string? fallbackRealm,
        CancellationToken cancellationToken)
    {
        var entries = new List<InputEntry>(lines.Count);
        var hasFallbackRealm = CharacterHelpers.TryResolveRealm(
            fallbackRealm,
            out var fallbackApiRealm,
            out var fallbackDisplayRealm);

        foreach (var line in lines)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (CharacterHelpers.TryExtractCharacterWithRealm(
                    line,
                    out var nameWithRealm,
                    out var apiRealm,
                    out var displayRealm))
            {
                entries.Add(new InputEntry(
                    line,
                    new CharacterToScan(nameWithRealm, apiRealm, displayRealm),
                    false));
                continue;
            }

            if (CharacterHelpers.TryExtractCharacterName(line, out var name))
            {
                if (!hasFallbackRealm)
                {
                    throw new InvalidOperationException(
                        $"The names file contains entries without realm information. Provide --realm or use Name-Realm lines. File: {inputPath}");
                }

                entries.Add(new InputEntry(
                    line,
                    new CharacterToScan(name, fallbackApiRealm, fallbackDisplayRealm),
                    false));
                continue;
            }

            var trimmedLine = line.Trim();
            entries.Add(new InputEntry(
                line,
                null,
                !string.IsNullOrWhiteSpace(trimmedLine) &&
                !trimmedLine.StartsWith('#')));
        }

        return entries;
    }

    private async Task<Dictionary<CharacterToScan, CharacterLookupResult>> CheckCharactersAsync(
        IReadOnlyList<CharacterToScan> characters,
        int maxDegreeOfParallelism,
        CancellationToken cancellationToken)
    {
        var apiUrl = BuildApiUrl(_apiOptions.BaseUrl, _apiOptions.ApiKey);
        var results = new ConcurrentDictionary<CharacterToScan, CharacterLookupResult>(new CharacterToScanComparer());
        var processedCount = 0;

        using var client = new HttpClient();

        await Parallel.ForEachAsync(
            characters,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = maxDegreeOfParallelism,
                CancellationToken = cancellationToken
            },
            async (character, ct) =>
            {
                var result = await CheckCharacterAsync(client, apiUrl, _apiOptions.Secret, character, ct);
                results[character] = result;

                if (result.Status == CharacterLookupStatus.Error)
                {
                    Console.Error.WriteLine(
                        $"Keeping {character.Name}-{character.DisplayRealm}: {result.Message}");
                }

                var processed = Interlocked.Increment(ref processedCount);
                if (processed % ProgressInterval == 0 || processed == characters.Count)
                {
                    Console.Write($"\rArmory prune progress: {processed}/{characters.Count}");
                }
            });

        if (characters.Count > 0)
        {
            Console.WriteLine();
        }

        return results.ToDictionary(pair => pair.Key, pair => pair.Value, new CharacterToScanComparer());
    }

    private static async Task<CharacterLookupResult> CheckCharacterAsync(
        HttpClient client,
        string apiUrl,
        string secret,
        CharacterToScan character,
        CancellationToken cancellationToken)
    {
        var body = new
        {
            secret,
            url = "character-sheet",
            @params = new
            {
                r = character.ApiRealm,
                n = character.Name
            }
        };

        using var content = new StringContent(
            JsonSerializer.Serialize(body),
            Encoding.UTF8,
            "application/json");

        try
        {
            using var response = await client.PostAsync(apiUrl, content, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == HttpStatusCode.BadRequest ||
                    response.StatusCode == HttpStatusCode.NotFound)
                {
                    return CharacterLookupResult.Missing();
                }

                return CharacterLookupResult.Error(
                    $"API returned {(int)response.StatusCode} {response.ReasonPhrase}.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            if (!document.RootElement.TryGetProperty("response", out var responseElement) ||
                IsEmptyResponse(responseElement))
            {
                return CharacterLookupResult.Missing();
            }

            return CharacterLookupResult.Found();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return CharacterLookupResult.Error(ex.Message);
        }
    }

    private static bool IsEmptyResponse(JsonElement responseElement)
    {
        return responseElement.ValueKind switch
        {
            JsonValueKind.Undefined => true,
            JsonValueKind.Null => true,
            JsonValueKind.Object => !responseElement.EnumerateObject().Any(),
            JsonValueKind.Array => responseElement.GetArrayLength() == 0,
            JsonValueKind.String => string.IsNullOrWhiteSpace(responseElement.GetString()),
            _ => false
        };
    }

    private static async Task WriteOutputAsync(
        string outputPath,
        IReadOnlyList<string> lines,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = outputPath + ".tmp";
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        await using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true))
        await using (var writer = new StreamWriter(stream, encoding))
        {
            for (var index = 0; index < lines.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await writer.WriteAsync(lines[index]);

                if (index < lines.Count - 1)
                {
                    await writer.WriteLineAsync();
                }
            }
        }

        File.Move(tempPath, outputPath, overwrite: true);
    }

    private static string BuildApiUrl(string baseUrl, string apiKey)
    {
        var separator = baseUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return $"{baseUrl}{separator}apikey={Uri.EscapeDataString(apiKey)}";
    }

    private sealed record InputEntry(string OriginalLine, CharacterToScan? Character, bool WasUnparsed);

    private sealed record CharacterToScan(string Name, string ApiRealm, string DisplayRealm);

    private sealed record CharacterLookupResult(CharacterLookupStatus Status, string? Message)
    {
        public static CharacterLookupResult Found() => new(CharacterLookupStatus.Found, null);

        public static CharacterLookupResult Missing() => new(CharacterLookupStatus.Missing, null);

        public static CharacterLookupResult Error(string message) => new(CharacterLookupStatus.Error, message);
    }

    private enum CharacterLookupStatus
    {
        Found,
        Missing,
        Error
    }

    private sealed class CharacterToScanComparer : IEqualityComparer<CharacterToScan>
    {
        public bool Equals(CharacterToScan? x, CharacterToScan? y)
        {
            if (ReferenceEquals(x, y))
            {
                return true;
            }

            if (x is null || y is null)
            {
                return false;
            }

            return string.Equals(x.Name, y.Name, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(x.DisplayRealm, y.DisplayRealm, StringComparison.OrdinalIgnoreCase);
        }

        public int GetHashCode(CharacterToScan obj)
        {
            return HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Name),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.DisplayRealm));
        }
    }
}
