using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using AchievementLadder.Configuration;
using AchievementLadder.Helpers;
using RareAchiAndItemScan;

namespace MissingItemFinder;

public sealed class MissingItemFinderService(
    string solutionRoot,
    string projectRoot,
    string achievementLadderProjectRoot,
    TauriApiOptions apiOptions)
{
    private static readonly JsonSerializerOptions ReportJsonOptions = new()
    {
        WriteIndented = true
    };

    private const int ProgressInterval = 100;

    private readonly string _solutionRoot = Path.GetFullPath(solutionRoot);
    private readonly string _projectRoot = Path.GetFullPath(projectRoot);
    private readonly string _achievementLadderProjectRoot = Path.GetFullPath(achievementLadderProjectRoot);
    private readonly TauriApiOptions _apiOptions = apiOptions;

    public async Task<MissingItemFinderResult> ExecuteAsync(
        MissingItemFinderOptions options,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_achievementLadderProjectRoot))
        {
            throw new DirectoryNotFoundException(
                $"Could not find AchievementLadder project folder: {_achievementLadderProjectRoot}");
        }

        var characters = LoadCharacters(options, cancellationToken);
        var targetItems = options.ItemIds.ToDictionary(
            itemId => itemId,
            RareScanCatalog.ItemNameFromId);
        var results = new ConcurrentBag<ItemCharacterScanResult>();
        var failures = new ConcurrentBag<string>();
        var unresolvedCharacters = new ConcurrentBag<CharacterToScan>();
        var apiUrl = BuildApiUrl(_apiOptions.BaseUrl, _apiOptions.ApiKey);
        var processedCount = 0;

        using var client = new HttpClient();

        await Parallel.ForEachAsync(
            characters,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = options.MaxDegreeOfParallelism,
                CancellationToken = cancellationToken
            },
            async (character, ct) =>
            {
                var scanResult = await FetchOwnedMatchingItemsAsync(
                    client,
                    apiUrl,
                    _apiOptions.Secret,
                    character,
                    targetItems,
                    ct);

                if (!scanResult.Success)
                {
                    unresolvedCharacters.Add(character);
                    failures.Add($"{character.Name}-{character.DisplayRealm}: {scanResult.FailureMessage}");
                    Console.Error.WriteLine(
                        $"Skipping {character.Name}-{character.DisplayRealm}: {scanResult.FailureMessage}");
                }
                else if (scanResult.Items.Count > 0)
                {
                    results.Add(new ItemCharacterScanResult(
                        character.Name,
                        character.DisplayRealm,
                        scanResult.Items));

                    Console.WriteLine(
                        $"{character.Name}-{character.DisplayRealm}: {string.Join(", ", scanResult.Items.Select(FormatItemMatch))}");
                }

                var processed = Interlocked.Increment(ref processedCount);
                if (processed % ProgressInterval == 0 || processed == characters.Count)
                {
                    Console.Write($"\rItem scan progress: {processed}/{characters.Count}");
                }
            });

        if (characters.Count > 0)
        {
            Console.WriteLine();
        }

        var orderedResults = results
            .OrderBy(result => result.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(result => result.Realm, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var orderedFailures = failures
            .OrderBy(message => message, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var remainingCharacters = unresolvedCharacters
            .OrderBy(character => character.DisplayRealm, StringComparer.OrdinalIgnoreCase)
            .ThenBy(character => character.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var report = new MissingItemFinderReport(
            DateTimeOffset.UtcNow,
            options.DescribeScope(),
            options.ItemIds,
            characters.Count,
            orderedFailures.Count,
            orderedFailures,
            orderedResults);

        var outputPath = ResolveOutputPath(options);
        var missingOutputPath = ResolveMissingOutputPath(options);

        await WriteReportAsync(outputPath, report, cancellationToken);
        await WriteMissingCharactersAsync(missingOutputPath, remainingCharacters, cancellationToken);

        return new MissingItemFinderResult(
            characters.Count,
            characters.Count - remainingCharacters.Count,
            orderedResults.Count,
            remainingCharacters.Count,
            orderedResults.Sum(result => result.Items.Count),
            outputPath,
            missingOutputPath);
    }

    private List<CharacterToScan> LoadCharacters(
        MissingItemFinderOptions options,
        CancellationToken cancellationToken)
    {
        if (options.HasSpecificCharacter)
        {
            return [ResolveSingleCharacter(options.CharacterName!, options.Realm!)];
        }

        return options.HasNamesFile
            ? LoadCharactersFromNamesFile(options, cancellationToken)
            : LoadDefaultCharacters(cancellationToken);
    }

    private static CharacterToScan ResolveSingleCharacter(string name, string realm)
    {
        if (!CharacterHelpers.TryResolveRealm(realm, out var apiRealm, out var displayRealm))
        {
            throw new InvalidOperationException(
                $"Unknown realm '{realm}'. Use Tauri, Evermoon, WoD, or a full API realm name.");
        }

        return new CharacterToScan(name.Trim(), apiRealm, displayRealm);
    }

    private List<CharacterToScan> LoadDefaultCharacters(CancellationToken cancellationToken)
    {
        var allCharacters = new List<(string Name, string ApiRealm, string DisplayRealm)>();
        CharacterHelpers.LoadDefaultCharacterSources(
            _achievementLadderProjectRoot,
            allCharacters,
            ignoreMissingGuildCharacters: true);

        return ToDistinctCharacters(allCharacters, cancellationToken);
    }

    private List<CharacterToScan> LoadCharactersFromNamesFile(
        MissingItemFinderOptions options,
        CancellationToken cancellationToken)
    {
        var resolvedPath = Path.GetFullPath(options.NamesFilePath!, _projectRoot);
        if (!File.Exists(resolvedPath))
        {
            throw new FileNotFoundException($"Could not find names file: {resolvedPath}", resolvedPath);
        }

        var characters = new HashSet<CharacterToScan>(new CharacterToScanComparer());
        var hasFallbackRealm = CharacterHelpers.TryResolveRealm(
            options.Realm,
            out var fallbackApiRealm,
            out var fallbackDisplayRealm);

        foreach (var rawLine in File.ReadLines(resolvedPath))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (CharacterHelpers.TryExtractCharacterWithRealm(
                    rawLine,
                    out var nameWithRealm,
                    out var apiRealm,
                    out var displayRealm))
            {
                characters.Add(new CharacterToScan(nameWithRealm, apiRealm, displayRealm));
                continue;
            }

            if (!CharacterHelpers.TryExtractCharacterName(rawLine, out var name))
            {
                continue;
            }

            if (!hasFallbackRealm)
            {
                throw new InvalidOperationException(
                    $"The names file contains entries without realm information. Provide --realm or use Name-Realm lines. File: {resolvedPath}");
            }

            characters.Add(new CharacterToScan(name, fallbackApiRealm, fallbackDisplayRealm));
        }

        return characters
            .OrderBy(character => character.DisplayRealm, StringComparer.OrdinalIgnoreCase)
            .ThenBy(character => character.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<CharacterToScan> ToDistinctCharacters(
        IEnumerable<(string Name, string ApiRealm, string DisplayRealm)> sourceCharacters,
        CancellationToken cancellationToken)
    {
        var result = new HashSet<CharacterToScan>(new CharacterToScanComparer());

        foreach (var character in sourceCharacters)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(character.Name) ||
                string.IsNullOrWhiteSpace(character.ApiRealm) ||
                string.IsNullOrWhiteSpace(character.DisplayRealm) ||
                character.Name.Contains('#', StringComparison.Ordinal))
            {
                continue;
            }

            result.Add(new CharacterToScan(
                character.Name.Trim(),
                character.ApiRealm.Trim(),
                character.DisplayRealm.Trim()));
        }

        return result
            .OrderBy(character => character.DisplayRealm, StringComparer.OrdinalIgnoreCase)
            .ThenBy(character => character.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static async Task<ItemFetchResult> FetchOwnedMatchingItemsAsync(
        HttpClient client,
        string apiUrl,
        string secret,
        CharacterToScan character,
        IReadOnlyDictionary<int, string> targetItems,
        CancellationToken cancellationToken)
    {
        var body = new
        {
            secret,
            url = "character-itemappearances",
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
                return ItemFetchResult.Fail(
                    $"API returned {(int)response.StatusCode} {response.ReasonPhrase}");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            if (!document.RootElement.TryGetProperty("response", out var responseElement))
            {
                return ItemFetchResult.Fail("Missing response payload.");
            }

            if (!responseElement.TryGetProperty("itemappearances", out var itemAppearancesElement) ||
                !itemAppearancesElement.TryGetProperty("owned", out var ownedElement) ||
                ownedElement.ValueKind != JsonValueKind.Array)
            {
                return ItemFetchResult.Fail("Missing item appearance data.");
            }

            var matches = new Dictionary<int, ItemMatchResult>();

            foreach (var categoryArray in ownedElement.EnumerateArray())
            {
                if (categoryArray.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var itemElement in categoryArray.EnumerateArray())
                {
                    if (itemElement.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    var itemId = ReadInt(itemElement, "itemid");
                    if (itemId <= 0 || !targetItems.TryGetValue(itemId, out var itemName))
                    {
                        continue;
                    }

                    matches[itemId] = new ItemMatchResult(itemId, itemName);
                }
            }

            return ItemFetchResult.Ok(matches.Values
                .OrderBy(item => item.ItemId)
                .ToList());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ItemFetchResult.Fail(ex.Message);
        }
    }

    private string ResolveOutputPath(MissingItemFinderOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.OutputPath))
        {
            return Path.GetFullPath(options.OutputPath!, _projectRoot);
        }

        return Path.Combine(_projectRoot, "Output", "item-scan-results.json");
    }

    private string ResolveMissingOutputPath(MissingItemFinderOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.MissingOutputPath))
        {
            return Path.GetFullPath(options.MissingOutputPath!, _projectRoot);
        }

        return Path.Combine(_solutionRoot, "MissingItemCharactersToScan.txt");
    }

    private static async Task WriteReportAsync(
        string outputPath,
        MissingItemFinderReport report,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = outputPath + ".tmp";
        var json = JsonSerializer.Serialize(report, ReportJsonOptions);
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        await File.WriteAllTextAsync(tempPath, json, encoding, cancellationToken);
        File.Move(tempPath, outputPath, overwrite: true);
    }

    private static async Task WriteMissingCharactersAsync(
        string outputPath,
        IReadOnlyList<CharacterToScan> characters,
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
            foreach (var character in characters)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await writer.WriteLineAsync($"{character.Name}-{character.DisplayRealm}");
            }
        }

        File.Move(tempPath, outputPath, overwrite: true);
    }

    private static int ReadInt(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var property))
        {
            return 0;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var intValue))
        {
            return intValue;
        }

        return property.ValueKind == JsonValueKind.String &&
               int.TryParse(property.GetString(), out intValue)
            ? intValue
            : 0;
    }

    private static string FormatItemMatch(ItemMatchResult item) =>
        string.Equals(item.Name, $"Item {item.ItemId}", StringComparison.OrdinalIgnoreCase)
            ? item.Name
            : $"{item.Name} ({item.ItemId})";

    private static string BuildApiUrl(string baseUrl, string apiKey)
    {
        var separator = baseUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return $"{baseUrl}{separator}apikey={Uri.EscapeDataString(apiKey)}";
    }

    private sealed record CharacterToScan(string Name, string ApiRealm, string DisplayRealm);

    private sealed record ItemFetchResult(
        bool Success,
        string? FailureMessage,
        IReadOnlyList<ItemMatchResult> Items)
    {
        public static ItemFetchResult Ok(IReadOnlyList<ItemMatchResult> items) =>
            new(true, null, items);

        public static ItemFetchResult Fail(string message) =>
            new(false, message, Array.Empty<ItemMatchResult>());
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
