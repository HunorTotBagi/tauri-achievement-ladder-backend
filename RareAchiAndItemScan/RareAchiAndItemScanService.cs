using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using AchievementLadder.Configuration;
using AchievementLadder.Dtos;
using AchievementLadder.Helpers;

namespace RareAchiAndItemScan;

public sealed class RareAchiAndItemScanService(
    string projectRoot,
    string achievementLadderProjectRoot,
    TauriApiOptions apiOptions)
{
    private static readonly object ConsoleWriteLock = new();
    private static readonly JsonSerializerOptions ReportJsonOptions = new()
    {
        WriteIndented = true
    };

    private static readonly RealmSource[] RealmSources =
    [
        new("evermoon-achi.txt", "[EN] Evermoon", "Evermoon"),
        new("evermoon-hk.txt", "[EN] Evermoon", "Evermoon"),
        new("evermoon-playTime.txt", "[EN] Evermoon", "Evermoon"),
        new("tauri-achi.txt", "[HU] Tauri WoW Server", "Tauri"),
        new("tauri-hk.txt", "[HU] Tauri WoW Server", "Tauri"),
        new("tauri-playTime.txt", "[HU] Tauri WoW Server", "Tauri"),
        new("wod-achi.txt", "[HU] Warriors of Darkness", "WoD"),
        new("wod-hk.txt", "[HU] Warriors of Darkness", "WoD"),
        new("wod-playTime.txt", "[HU] Warriors of Darkness", "WoD")
    ];

    private const int MaxDegreeOfParallelism = 10;
    private const int ProgressInterval = 100;

    private readonly string _projectRoot = Path.GetFullPath(projectRoot);
    private readonly string _achievementLadderProjectRoot = Path.GetFullPath(achievementLadderProjectRoot);
    private readonly TauriApiOptions _apiOptions = apiOptions;

    public async Task<RareScanRunResult> ExecuteAsync(ScanOptions options, CancellationToken cancellationToken)
    {
        using var client = new HttpClient();
        var apiUrl = BuildApiUrl(_apiOptions.BaseUrl, _apiOptions.ApiKey);
        var targetItems = ResolveTargetItems(options);
        var characters = await ResolveCharactersAsync(options, client, apiUrl, _apiOptions.Secret, cancellationToken);
        var scanResults = new ConcurrentBag<RareCharacterScanResult>();
        var failures = new ConcurrentBag<string>();
        var processedCount = 0;

        await Parallel.ForEachAsync(
            characters,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = MaxDegreeOfParallelism,
                CancellationToken = cancellationToken
            },
            async (character, ct) =>
            {
                try
                {
                    var result = await ScanCharacterAsync(
                        client,
                        apiUrl,
                        _apiOptions.Secret,
                        character,
                        options.Targets,
                        targetItems,
                        ct);
                    if (result is not null)
                    {
                        scanResults.Add(result);
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    failures.Add($"{character.Name}-{character.DisplayRealm}: {ex.Message}");
                    Console.Error.WriteLine(
                        $"Skipping {character.Name}-{character.DisplayRealm}: {ex.Message}");
                }
                finally
                {
                    var processed = Interlocked.Increment(ref processedCount);
                    if (processed % ProgressInterval == 0 || processed == characters.Count)
                    {
                        Console.WriteLine($"Progress: {processed}/{characters.Count}");
                    }
                }
            });

        var orderedResults = scanResults
            .OrderBy(result => result.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(result => result.Realm, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var orderedFailures = failures
            .OrderBy(message => message, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (characters.Count > 0 && orderedFailures.Count == characters.Count)
        {
            throw new InvalidOperationException(
                $"All character scans failed. First error: {orderedFailures[0]}");
        }

        var report = new RareScanReport(
            DateTimeOffset.UtcNow,
            options.DescribeScope(),
            options.DescribeTargets(),
            characters.Count,
            orderedFailures.Count,
            orderedFailures,
            orderedResults);

        var outputPath = ResolveOutputPath(options);
        await WriteReportAsync(outputPath, report, cancellationToken);

        return new RareScanRunResult(
            characters.Count,
            orderedResults.Count,
            orderedFailures.Count,
            orderedResults.Sum(result => result.Achievements.Count),
            orderedResults.Sum(result => result.Items.Count),
            orderedResults.Sum(result => result.Mounts.Count),
            outputPath);
    }

    private static IReadOnlyDictionary<int, string> ResolveTargetItems(ScanOptions options)
    {
        if (!options.Targets.HasFlag(ScanTarget.Items))
        {
            return new Dictionary<int, string>();
        }

        if (!options.HasCustomItemIds)
        {
            return RareScanCatalog.TargetItems;
        }

        return options.ItemIds.ToDictionary(
            itemId => itemId,
            RareScanCatalog.ItemNameFromId);
    }

    private async Task<List<CharacterToScan>> ResolveCharactersAsync(
        ScanOptions options,
        HttpClient client,
        string apiUrl,
        string secret,
        CancellationToken cancellationToken)
    {
        if (options.HasSpecificCharacter)
        {
            return [ResolveSingleCharacter(options.CharacterName!, options.Realm!)];
        }

        if (options.HasSpecificGuild)
        {
            return await LoadGuildMembersAsync(
                client,
                apiUrl,
                secret,
                options.GuildName!,
                options.Realm!,
                cancellationToken);
        }

        if (options.HasNamesFile)
        {
            return LoadCharactersFromNamesFile(options.NamesFilePath!, options.Realm!, cancellationToken);
        }

        return LoadSourceCharacters(cancellationToken);
    }

    private List<CharacterToScan> LoadSourceCharacters(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_achievementLadderProjectRoot))
        {
            throw new DirectoryNotFoundException(
                $"Could not find AchievementLadder project folder: {_achievementLadderProjectRoot}");
        }

        var allCharacters = new List<(string Name, string ApiRealm, string DisplayRealm)>();
        CharacterHelpers.LoadDefaultCharacterSources(
            _achievementLadderProjectRoot,
            allCharacters,
            ignoreMissingGuildCharacters: true);

        var characters = new HashSet<CharacterToScan>(new CharacterToScanComparer());

        foreach (var character in allCharacters)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(character.Name) ||
                string.IsNullOrWhiteSpace(character.ApiRealm) ||
                string.IsNullOrWhiteSpace(character.DisplayRealm) ||
                character.Name.Contains('#', StringComparison.Ordinal))
            {
                continue;
            }

            characters.Add(new CharacterToScan(
                character.Name.Trim(),
                character.ApiRealm.Trim(),
                character.DisplayRealm.Trim()));
        }

        return characters
            .OrderBy(character => character.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(character => character.DisplayRealm, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static CharacterToScan ResolveSingleCharacter(string name, string realm)
    {
        var source = ResolveRealmSource(realm);
        return new CharacterToScan(name.Trim(), source.ApiRealm, source.DisplayRealm);
    }

    private List<CharacterToScan> LoadCharactersFromNamesFile(
        string namesFilePath,
        string realm,
        CancellationToken cancellationToken)
    {
        var realmSource = ResolveRealmSource(realm);
        var resolvedPath = Path.GetFullPath(namesFilePath, _projectRoot);

        if (!File.Exists(resolvedPath))
        {
            throw new FileNotFoundException(
                $"Could not find names file: {resolvedPath}",
                resolvedPath);
        }

        var characters = new HashSet<CharacterToScan>(new CharacterToScanComparer());

        foreach (var rawLine in File.ReadLines(resolvedPath))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!CharacterHelpers.TryExtractCharacterName(rawLine, out var name))
            {
                continue;
            }

            characters.Add(new CharacterToScan(name, realmSource.ApiRealm, realmSource.DisplayRealm));
        }

        return characters
            .OrderBy(character => character.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static RealmSource ResolveRealmSource(string realm)
    {
        foreach (var source in RealmSources)
        {
            if (string.Equals(realm, source.DisplayRealm, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(realm, source.ApiRealm, StringComparison.OrdinalIgnoreCase))
            {
                return source;
            }
        }

        throw new InvalidOperationException(
            $"Unknown realm '{realm}'. Use Tauri, Evermoon, WoD, or a full API realm name.");
    }

    private static async Task<List<CharacterToScan>> LoadGuildMembersAsync(
        HttpClient client,
        string apiUrl,
        string secret,
        string guildName,
        string realm,
        CancellationToken cancellationToken)
    {
        var realmSource = ResolveRealmSource(realm);
        var body = new
        {
            secret,
            url = "guild-info",
            @params = new
            {
                r = realmSource.ApiRealm,
                gn = guildName.Trim()
            }
        };

        using var content = new StringContent(
            JsonSerializer.Serialize(body),
            Encoding.UTF8,
            "application/json");

        using var response = await client.PostAsync(apiUrl, content, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Guild lookup failed for '{guildName}' on {realmSource.DisplayRealm}: API returned {(int)response.StatusCode} {response.ReasonPhrase}.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var guildInfo = await JsonSerializer.DeserializeAsync<GuildInfoResponse>(
            stream,
            cancellationToken: cancellationToken);

        if (guildInfo?.response?.guildList is null || guildInfo.response.guildList.Count == 0)
        {
            throw new InvalidOperationException(
                $"Guild '{guildName}' on {realmSource.DisplayRealm} was not found or returned no members.");
        }

        return guildInfo.response.guildList.Values
            .Select(member => member.name?.Trim())
            .Where(name => !string.IsNullOrWhiteSpace(name) && !name.Contains('#', StringComparison.Ordinal))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(name => new CharacterToScan(name!, realmSource.ApiRealm, realmSource.DisplayRealm))
            .OrderBy(character => character.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static async Task<RareCharacterScanResult?> ScanCharacterAsync(
        HttpClient client,
        string apiUrl,
        string secret,
        CharacterToScan character,
        ScanTarget targets,
        IReadOnlyDictionary<int, string> targetItems,
        CancellationToken cancellationToken)
    {
        var achievementsTask = targets.HasFlag(ScanTarget.Achievements)
            ? FetchMatchingAchievementsAsync(client, apiUrl, secret, character, cancellationToken)
            : Task.FromResult<IReadOnlyList<RareAchievementMatch>>(Array.Empty<RareAchievementMatch>());

        var itemsTask = targets.HasFlag(ScanTarget.Items)
            ? FetchOwnedMatchingItemsAsync(client, apiUrl, secret, character, targetItems, cancellationToken)
            : Task.FromResult<IReadOnlyList<RareItemMatch>>(Array.Empty<RareItemMatch>());

        var mountsTask = targets.HasFlag(ScanTarget.Mounts)
            ? FetchMatchingMountsAsync(client, apiUrl, secret, character, cancellationToken)
            : Task.FromResult<IReadOnlyList<RareMountMatch>>(Array.Empty<RareMountMatch>());

        await Task.WhenAll(achievementsTask, itemsTask, mountsTask);

        var achievements = achievementsTask.Result;
        var items = itemsTask.Result;
        var mounts = mountsTask.Result;

        if (achievements.Count == 0 && items.Count == 0 && mounts.Count == 0)
        {
            return null;
        }

        var classId = await FetchCharacterClassAsync(client, apiUrl, secret, character, cancellationToken);
        var className = RareScanCatalog.ClassNameFromId(classId);

        WriteMatchSummary(character, className, achievements, items, mounts);

        return new RareCharacterScanResult(
            character.Name,
            character.DisplayRealm,
            classId,
            className,
            achievements,
            items,
            mounts);
    }

    private static void WriteMatchSummary(
        CharacterToScan character,
        string className,
        IReadOnlyList<RareAchievementMatch> achievements,
        IReadOnlyList<RareItemMatch> items,
        IReadOnlyList<RareMountMatch> mounts)
    {
        var fragments = new List<string>();

        if (achievements.Count > 0)
        {
            fragments.Add($"achievements: {string.Join(", ", achievements.Select(match => match.Name))}");
        }

        if (items.Count > 0)
        {
            fragments.Add($"items: {string.Join(", ", items.Select(FormatItemMatch))}");
        }

        if (mounts.Count > 0)
        {
            fragments.Add($"mounts: {string.Join(", ", mounts.Select(match => match.Name))}");
        }

        lock (ConsoleWriteLock)
        {
            var previousColor = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write($"{character.Name} - {className} - {character.DisplayRealm}: ");
            Console.ForegroundColor = previousColor;
            Console.WriteLine(string.Join(" | ", fragments));
        }
    }

    private static string FormatItemMatch(RareItemMatch match)
    {
        var fallbackName = $"Item {match.ItemId}";
        return string.Equals(match.Name, fallbackName, StringComparison.OrdinalIgnoreCase)
            ? fallbackName
            : $"{match.Name} ({match.ItemId})";
    }

    private static async Task<IReadOnlyList<RareAchievementMatch>> FetchMatchingAchievementsAsync(
        HttpClient client,
        string apiUrl,
        string secret,
        CharacterToScan character,
        CancellationToken cancellationToken)
    {
        var response = await FetchResponseElementAsync(
            client,
            apiUrl,
            secret,
            "character-achievements",
            new
            {
                r = character.ApiRealm,
                n = character.Name
            },
            cancellationToken);

        if (response is null)
        {
            return Array.Empty<RareAchievementMatch>();
        }

        var achievedIds = ExtractAchievementIds(response.Value);

        return RareScanCatalog.RareAchievementNames
            .Where(kvp => achievedIds.Contains(kvp.Key))
            .Select(kvp => new RareAchievementMatch(kvp.Key, kvp.Value))
            .OrderBy(match => match.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static async Task<IReadOnlyList<RareItemMatch>> FetchOwnedMatchingItemsAsync(
        HttpClient client,
        string apiUrl,
        string secret,
        CharacterToScan character,
        IReadOnlyDictionary<int, string> targetItems,
        CancellationToken cancellationToken)
    {
        if (targetItems.Count == 0)
        {
            return Array.Empty<RareItemMatch>();
        }

        var response = await FetchResponseElementAsync(
            client,
            apiUrl,
            secret,
            "character-itemappearances",
            new
            {
                r = character.ApiRealm,
                n = character.Name
            },
            cancellationToken);

        if (response is null ||
            !response.Value.TryGetProperty("itemappearances", out var itemAppearancesElement) ||
            !itemAppearancesElement.TryGetProperty("owned", out var ownedElement) ||
            ownedElement.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<RareItemMatch>();
        }

        var matches = new Dictionary<int, RareItemMatch>();

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
                if (itemId <= 0 ||
                    !targetItems.TryGetValue(itemId, out var itemName))
                {
                    continue;
                }

                matches[itemId] = new RareItemMatch(itemId, itemName);
            }
        }

        return matches.Values
            .OrderBy(match => match.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static async Task<IReadOnlyList<RareMountMatch>> FetchMatchingMountsAsync(
        HttpClient client,
        string apiUrl,
        string secret,
        CharacterToScan character,
        CancellationToken cancellationToken)
    {
        var response = await FetchResponseElementAsync(
            client,
            apiUrl,
            secret,
            "character-mounts",
            new
            {
                r = character.ApiRealm,
                n = character.Name
            },
            cancellationToken);

        if (response is null ||
            !response.Value.TryGetProperty("mounts", out var mountsElement) ||
            mountsElement.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<RareMountMatch>();
        }

        var matches = new Dictionary<string, RareMountMatch>(StringComparer.OrdinalIgnoreCase);

        foreach (var mountElement in mountsElement.EnumerateArray())
        {
            if (mountElement.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var mountName = ReadString(mountElement, "spellname").Trim();
            if (string.IsNullOrWhiteSpace(mountName) ||
                !RareScanCatalog.GladiatorMountNames.Contains(mountName))
            {
                continue;
            }

            matches[mountName] = new RareMountMatch(ReadInt(mountElement, "spellid"), mountName);
        }

        return matches.Values
            .OrderBy(match => match.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static async Task<int> FetchCharacterClassAsync(
        HttpClient client,
        string apiUrl,
        string secret,
        CharacterToScan character,
        CancellationToken cancellationToken)
    {
        var response = await FetchResponseElementAsync(
            client,
            apiUrl,
            secret,
            "character-sheet",
            new
            {
                r = character.ApiRealm,
                n = character.Name
            },
            cancellationToken);

        return response is null ? 0 : ReadInt(response.Value, "class");
    }

    private static async Task<JsonElement?> FetchResponseElementAsync(
        HttpClient client,
        string apiUrl,
        string secret,
        string endpoint,
        object parameters,
        CancellationToken cancellationToken)
    {
        var body = new
        {
            secret,
            url = endpoint,
            @params = parameters
        };

        using var content = new StringContent(
            JsonSerializer.Serialize(body),
            Encoding.UTF8,
            "application/json");

        using var response = await client.PostAsync(apiUrl, content, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (!document.RootElement.TryGetProperty("response", out var responseElement))
        {
            return null;
        }

        return responseElement.Clone();
    }

    private string ResolveOutputPath(ScanOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.OutputPath))
        {
            return Path.GetFullPath(options.OutputPath!, _projectRoot);
        }

        var outputDirectory = Path.Combine(_projectRoot, "Output");
        var fileName = BuildDefaultFileName(options);
        return Path.Combine(outputDirectory, fileName);
    }

    private static string BuildDefaultFileName(ScanOptions options)
    {
        var fileName = $"{options.GetTargetFileSegment()}-rare-scan-results";

        if (options.HasSpecificCharacter)
        {
            fileName +=
                $"-{SanitizeFileSegment(options.CharacterName!)}-{SanitizeFileSegment(options.Realm!)}";
        }
        else if (options.HasSpecificGuild)
        {
            fileName +=
                $"-guild-{SanitizeFileSegment(options.GuildName!)}-{SanitizeFileSegment(options.Realm!)}";
        }
        else if (options.HasNamesFile)
        {
            fileName +=
                $"-batch-{SanitizeFileSegment(Path.GetFileNameWithoutExtension(options.NamesFilePath!))}-{SanitizeFileSegment(options.Realm!)}";
        }

        return $"{fileName}.json";
    }

    private static string SanitizeFileSegment(string value)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);

        foreach (var character in value.Trim())
        {
            builder.Append(invalidCharacters.Contains(character) ? '_' : character);
        }

        return builder.ToString().Replace(' ', '-');
    }

    private static async Task WriteReportAsync(
        string outputPath,
        RareScanReport report,
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

    private static HashSet<int> ExtractAchievementIds(JsonElement responseElement)
    {
        if (!responseElement.TryGetProperty("Achievements", out var achievementsElement))
        {
            return [];
        }

        var achievementIds = new HashSet<int>();

        if (achievementsElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in achievementsElement.EnumerateObject())
            {
                if (int.TryParse(property.Name, out var achievementId))
                {
                    achievementIds.Add(achievementId);
                }
            }
        }
        else if (achievementsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in achievementsElement.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out var achievementId))
                {
                    achievementIds.Add(achievementId);
                }
            }
        }

        return achievementIds;
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

    private static string ReadString(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return string.Empty;
        }

        return property.GetString() ?? string.Empty;
    }

    private static string BuildApiUrl(string baseUrl, string apiKey)
    {
        var separator = baseUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return $"{baseUrl}{separator}apikey={Uri.EscapeDataString(apiKey)}";
    }

    private sealed record RealmSource(string FileName, string ApiRealm, string DisplayRealm);

    private sealed record CharacterToScan(string Name, string ApiRealm, string DisplayRealm);

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
