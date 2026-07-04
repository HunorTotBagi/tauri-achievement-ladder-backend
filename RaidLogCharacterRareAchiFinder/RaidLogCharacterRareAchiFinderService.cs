using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using AchievementLadder.Configuration;
using AchievementLadder.Helpers;
using RareAchiAndItemScan;

namespace RaidLogCharacterRareAchiFinder;

public sealed class RaidLogCharacterRareAchiFinderService(
    string projectRoot,
    string solutionRoot,
    TauriApiOptions apiOptions)
{
    private static readonly object ConsoleWriteLock = new();
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private static readonly (string Token, string FileName)[] GuildFileByRealm =
    [
        ("Evermoon", "evermoon-guilds.txt"),
        ("Tauri", "tauri-guilds.txt"),
        ("Warriors of Darkness", "wod-guilds.txt"),
        ("WoD", "wod-guilds.txt")
    ];

    private readonly string _projectRoot = Path.GetFullPath(projectRoot);
    private readonly string _guildsDirectory = Path.Combine(solutionRoot, "AchievementLadder", "Data", "Guilds");
    private readonly TauriApiOptions _apiOptions = apiOptions;
    private readonly CharacterKeyComparer _characterKeyComparer = new();
    private readonly HashSet<CharacterKey> _scannedCharacters = new(new CharacterKeyComparer());
    private readonly Dictionary<string, HashSet<string>> _knownGuildsByFile = new(StringComparer.OrdinalIgnoreCase);

    public async Task<RaidLogFinderResult> ExecuteAsync(
        RaidLogFinderOptions options,
        CancellationToken cancellationToken)
    {
        var statePath = ResolveStatePath(options.StatePath);
        var startLogId = options.StartLogId ?? await LoadStateAsync(statePath, cancellationToken) ?? 1;
        var apiUrl = BuildApiUrl(_apiOptions.BaseUrl, _apiOptions.ApiKey);
        var currentLogId = startLogId;
        var scannedLogs = 0;
        var memberCount = 0;
        var scannedCharacterCount = 0;
        var matchedCharacterCount = 0;
        var achievementMatchCount = 0;
        var itemMatchCount = 0;
        var mountMatchCount = 0;
        var newGuildCount = 0;
        var stopReason = string.Empty;

        using var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(_apiOptions.RequestTimeoutSeconds)
        };

        Console.WriteLine($"Scanning raid logs on {options.DisplayRealm} from id {startLogId}...");
        Console.WriteLine($"Checkpoint: {statePath}");
        Console.WriteLine();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (options.MaxLogs is { } maxLogs && scannedLogs >= maxLogs)
            {
                stopReason = $"Reached --max-logs limit ({maxLogs}).";
                await WriteStateAsync(statePath, currentLogId, cancellationToken);
                break;
            }

            var fetchResult = await FetchRaidLogAsync(
                client,
                apiUrl,
                _apiOptions.Secret,
                options.ApiRealm,
                options.DisplayRealm,
                currentLogId,
                cancellationToken);

            if (fetchResult.ShouldSkip)
            {
                Console.WriteLine($"[{currentLogId}] raid log not found - skipping.");
                currentLogId++;
                await WriteStateAsync(statePath, currentLogId, cancellationToken);
                continue;
            }

            if (fetchResult.RaidLog is null)
            {
                stopReason = fetchResult.StopReason ?? "No raid-log response.";
                Console.WriteLine($"Stopping at raid log id {currentLogId}: {stopReason}");
                await WriteStateAsync(statePath, currentLogId, cancellationToken);
                break;
            }

            var raidLog = fetchResult.RaidLog;
            scannedLogs++;
            memberCount += raidLog.Members.Count;

            Console.WriteLine(
                $"[{raidLog.LogId}] {raidLog.MapName} / {raidLog.EncounterName}: {raidLog.Members.Count} member(s)");

            newGuildCount += CollectUnknownGuilds(raidLog.Members);

            var scanResult = await ScanMembersAsync(
                client,
                apiUrl,
                _apiOptions.Secret,
                raidLog.Members,
                options.ScanParallelism,
                cancellationToken);

            scannedCharacterCount += scanResult.ScannedCharacterCount;
            matchedCharacterCount += scanResult.MatchedCharacterCount;
            achievementMatchCount += scanResult.AchievementMatchCount;
            itemMatchCount += scanResult.ItemMatchCount;
            mountMatchCount += scanResult.MountMatchCount;

            currentLogId++;
            await WriteStateAsync(statePath, currentLogId, cancellationToken);
        }

        return new RaidLogFinderResult(
            startLogId,
            currentLogId,
            scannedLogs,
            memberCount,
            scannedCharacterCount,
            matchedCharacterCount,
            achievementMatchCount,
            itemMatchCount,
            mountMatchCount,
            newGuildCount,
            statePath,
            stopReason);
    }

    private async Task<RaidMemberScanSummary> ScanMembersAsync(
        HttpClient client,
        string apiUrl,
        string secret,
        IReadOnlyList<RaidLogMember> members,
        int scanParallelism,
        CancellationToken cancellationToken)
    {
        var distinctMembers = members
            .DistinctBy(member => new CharacterKey(member.Name, member.DisplayRealm), _characterKeyComparer)
            .Where(member => _scannedCharacters.Add(new CharacterKey(member.Name, member.DisplayRealm)))
            .ToList();

        if (distinctMembers.Count == 0)
        {
            return RaidMemberScanSummary.Empty;
        }

        var matchedCharacterCount = 0;
        var achievementMatchCount = 0;
        var itemMatchCount = 0;
        var mountMatchCount = 0;

        await Parallel.ForEachAsync(
            distinctMembers,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = scanParallelism,
                CancellationToken = cancellationToken
            },
            async (member, ct) =>
            {
                try
                {
                    var result = await ScanMemberAsync(client, apiUrl, secret, member, ct);
                    if (!result.HasMatches)
                    {
                        return;
                    }

                    Interlocked.Increment(ref matchedCharacterCount);
                    Interlocked.Add(ref achievementMatchCount, result.Achievements.Count);
                    Interlocked.Add(ref itemMatchCount, result.Items.Count);
                    Interlocked.Add(ref mountMatchCount, result.Mounts.Count);
                    WriteMatchSummary(member, result);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(
                        $"  Skipping {member.Name}-{member.DisplayRealm}: {ex.Message}");
                }
            });

        return new RaidMemberScanSummary(
            distinctMembers.Count,
            matchedCharacterCount,
            achievementMatchCount,
            itemMatchCount,
            mountMatchCount);
    }

    private static async Task<RaidMemberScanResult> ScanMemberAsync(
        HttpClient client,
        string apiUrl,
        string secret,
        RaidLogMember member,
        CancellationToken cancellationToken)
    {
        var achievementsTask = FetchMatchingAchievementsAsync(client, apiUrl, secret, member, cancellationToken);
        var itemsTask = FetchOwnedMatchingItemsAsync(client, apiUrl, secret, member, cancellationToken);
        var mountsTask = FetchMatchingMountsAsync(client, apiUrl, secret, member, cancellationToken);

        await Task.WhenAll(achievementsTask, itemsTask, mountsTask);

        return new RaidMemberScanResult(
            achievementsTask.Result,
            itemsTask.Result,
            mountsTask.Result);
    }

    private static async Task<RaidLogFetchResult> FetchRaidLogAsync(
        HttpClient client,
        string apiUrl,
        string secret,
        string apiRealm,
        string displayRealm,
        int logId,
        CancellationToken cancellationToken)
    {
        var body = new
        {
            secret,
            url = "raid-log",
            @params = new
            {
                r = apiRealm,
                id = logId
            }
        };

        using var content = new StringContent(
            JsonSerializer.Serialize(body),
            Encoding.UTF8,
            "application/json");

        using var response = await client.PostAsync(apiUrl, content, cancellationToken);
        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            return RaidLogFetchResult.Stop(
                $"API returned {(int)response.StatusCode} {response.ReasonPhrase}");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Raid log {logId} failed: API returned {(int)response.StatusCode} {response.ReasonPhrase}.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;

        if (root.TryGetProperty("success", out var successElement) && IsExplicitFalse(successElement))
        {
            var errorCode = ReadInt(root, "errorcode");
            var errorString = ReadString(root, "errorstring");

            if (errorCode == 20 &&
                string.Equals(errorString, "raid log not found", StringComparison.OrdinalIgnoreCase))
            {
                return RaidLogFetchResult.Skip();
            }

            return RaidLogFetchResult.Stop(
                string.IsNullOrWhiteSpace(errorString)
                    ? $"API returned success=false ({errorCode})."
                    : $"API returned success=false ({errorCode}: {errorString}).");
        }

        if (!root.TryGetProperty("response", out var responseElement) ||
            responseElement.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined or JsonValueKind.False)
        {
            return RaidLogFetchResult.Stop("Missing response payload.");
        }

        if (responseElement.ValueKind != JsonValueKind.Object)
        {
            return RaidLogFetchResult.Stop($"Response payload was {responseElement.ValueKind}, not an object.");
        }

        var members = ReadMembers(responseElement, apiRealm, displayRealm);
        var raidLog = new RaidLogRecord(
            ReadInt(responseElement, "log_id", logId),
            ReadMapName(responseElement),
            ReadEncounterName(responseElement),
            members);

        return RaidLogFetchResult.Found(raidLog);
    }

    private static async Task<IReadOnlyList<RareAchievementMatch>> FetchMatchingAchievementsAsync(
        HttpClient client,
        string apiUrl,
        string secret,
        RaidLogMember member,
        CancellationToken cancellationToken)
    {
        var response = await FetchResponseElementAsync(
            client,
            apiUrl,
            secret,
            "character-achievements",
            new
            {
                r = member.ApiRealm,
                n = member.Name
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
        RaidLogMember member,
        CancellationToken cancellationToken)
    {
        var response = await FetchResponseElementAsync(
            client,
            apiUrl,
            secret,
            "character-itemappearances",
            new
            {
                r = member.ApiRealm,
                n = member.Name
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
                    !RareScanCatalog.TargetItems.TryGetValue(itemId, out var itemName))
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
        RaidLogMember member,
        CancellationToken cancellationToken)
    {
        var response = await FetchResponseElementAsync(
            client,
            apiUrl,
            secret,
            "character-mounts",
            new
            {
                r = member.ApiRealm,
                n = member.Name
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

            var mountName = ReadString(mountElement, "spellname");
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

    private int CollectUnknownGuilds(IReadOnlyList<RaidLogMember> members)
    {
        Directory.CreateDirectory(_guildsDirectory);

        var addedCount = 0;
        foreach (var member in members
                     .Where(member => !string.IsNullOrWhiteSpace(member.GuildName))
                     .DistinctBy(member => $"{member.GuildName}|{member.DisplayRealm}".ToLowerInvariant()))
        {
            var fileName = ResolveGuildFileName(member.DisplayRealm);
            if (fileName is null)
            {
                Console.WriteLine($"  No guild file for realm '{member.DisplayRealm}' - skipping guild '{member.GuildName}'.");
                continue;
            }

            var filePath = Path.Combine(_guildsDirectory, fileName);
            if (!_knownGuildsByFile.TryGetValue(fileName, out var knownGuilds))
            {
                knownGuilds = LoadGuildSet(filePath);
                _knownGuildsByFile[fileName] = knownGuilds;
            }

            if (!knownGuilds.Add(member.GuildName))
            {
                continue;
            }

            AppendGuildName(filePath, member.GuildName);
            addedCount++;
            Console.WriteLine($"  + Added guild '{member.GuildName}' to {fileName}");
        }

        return addedCount;
    }

    private static List<RaidLogMember> ReadMembers(
        JsonElement responseElement,
        string fallbackApiRealm,
        string fallbackDisplayRealm)
    {
        if (!responseElement.TryGetProperty("members", out var membersElement) ||
            membersElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var members = new List<RaidLogMember>();
        foreach (var memberElement in membersElement.EnumerateArray())
        {
            if (memberElement.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (memberElement.TryGetProperty("valid_player", out var validPlayerElement) &&
                IsExplicitFalse(validPlayerElement))
            {
                continue;
            }

            var name = ReadString(memberElement, "name");
            if (string.IsNullOrWhiteSpace(name) || name.Contains('#', StringComparison.Ordinal))
            {
                continue;
            }

            var link = ReadString(memberElement, "link");
            var (apiRealm, displayRealm) = ResolveMemberRealm(link, fallbackApiRealm, fallbackDisplayRealm);

            members.Add(new RaidLogMember(
                name,
                apiRealm,
                displayRealm,
                ReadInt(memberElement, "class"),
                ReadString(memberElement, "guildName")));
        }

        return members
            .DistinctBy(member => new CharacterKey(member.Name, member.DisplayRealm), new CharacterKeyComparer())
            .OrderBy(member => member.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(member => member.DisplayRealm, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static (string ApiRealm, string DisplayRealm) ResolveMemberRealm(
        string link,
        string fallbackApiRealm,
        string fallbackDisplayRealm)
    {
        var rawRealm = ExtractQueryValue(link, "r");
        if (string.IsNullOrWhiteSpace(rawRealm))
        {
            return (fallbackApiRealm, fallbackDisplayRealm);
        }

        if (CharacterHelpers.TryResolveRealm(rawRealm, out var apiRealm, out var displayRealm))
        {
            return (apiRealm, displayRealm);
        }

        return (rawRealm.Trim(), rawRealm.Trim());
    }

    private static string ExtractQueryValue(string link, string key)
    {
        if (string.IsNullOrWhiteSpace(link))
        {
            return string.Empty;
        }

        var decoded = WebUtility.HtmlDecode(link);
        foreach (var token in decoded.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separatorIndex = token.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var tokenKey = token[..separatorIndex].Trim();
            if (!string.Equals(tokenKey, key, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = token[(separatorIndex + 1)..].Trim();
            return Uri.UnescapeDataString(value.Replace("+", " ", StringComparison.Ordinal));
        }

        return string.Empty;
    }

    private string ResolveStatePath(string? statePath)
    {
        if (!string.IsNullOrWhiteSpace(statePath))
        {
            return Path.GetFullPath(statePath, _projectRoot);
        }

        return Path.Combine(_projectRoot, "raid-log-next-id.txt");
    }

    private static async Task<int?> LoadStateAsync(string statePath, CancellationToken cancellationToken)
    {
        if (!File.Exists(statePath))
        {
            return null;
        }

        var content = (await File.ReadAllTextAsync(statePath, cancellationToken)).Trim();
        return int.TryParse(content, NumberStyles.Integer, CultureInfo.InvariantCulture, out var logId) && logId > 0
            ? logId
            : null;
    }

    private static async Task WriteStateAsync(
        string statePath,
        int nextLogId,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(statePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = statePath + ".tmp";
        await File.WriteAllTextAsync(
            tempPath,
            nextLogId.ToString(CultureInfo.InvariantCulture),
            Utf8NoBom,
            cancellationToken);
        File.Move(tempPath, statePath, overwrite: true);
    }

    private static string? ResolveGuildFileName(string realmName)
    {
        if (string.IsNullOrWhiteSpace(realmName))
        {
            return null;
        }

        foreach (var (token, fileName) in GuildFileByRealm)
        {
            if (realmName.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                return fileName;
            }
        }

        return null;
    }

    private static HashSet<string> LoadGuildSet(string filePath)
    {
        var guilds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(filePath))
        {
            return guilds;
        }

        foreach (var line in File.ReadLines(filePath))
        {
            var trimmed = line.Trim().TrimStart('\uFEFF');
            if (!string.IsNullOrWhiteSpace(trimmed))
            {
                guilds.Add(trimmed);
            }
        }

        return guilds;
    }

    private static void AppendGuildName(string filePath, string guildName)
    {
        var needsLeadingNewline = File.Exists(filePath) && !EndsWithNewline(filePath);
        var textToAppend = (needsLeadingNewline ? Environment.NewLine : string.Empty) + guildName + Environment.NewLine;
        File.AppendAllText(filePath, textToAppend, Utf8NoBom);
    }

    private static bool EndsWithNewline(string filePath)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
        if (stream.Length == 0)
        {
            return true;
        }

        stream.Seek(-1, SeekOrigin.End);
        var lastByte = stream.ReadByte();
        return lastByte is '\n' or '\r';
    }

    private static string ReadMapName(JsonElement responseElement)
    {
        if (responseElement.TryGetProperty("mapentry", out var mapEntryElement) &&
            mapEntryElement.ValueKind == JsonValueKind.Object)
        {
            var mapName = ReadString(mapEntryElement, "name");
            if (!string.IsNullOrWhiteSpace(mapName))
            {
                return mapName;
            }
        }

        var mapId = ReadInt(responseElement, "map_id");
        return mapId > 0 ? $"Map {mapId}" : "Unknown Map";
    }

    private static string ReadEncounterName(JsonElement responseElement)
    {
        if (responseElement.TryGetProperty("encounter_data", out var encounterElement) &&
            encounterElement.ValueKind == JsonValueKind.Object)
        {
            var encounterName = ReadString(encounterElement, "encounter_name");
            if (!string.IsNullOrWhiteSpace(encounterName))
            {
                return encounterName;
            }
        }

        var encounterId = ReadInt(responseElement, "encounter_id");
        return encounterId > 0 ? $"Encounter {encounterId}" : "Unknown Encounter";
    }

    private static void WriteMatchSummary(RaidLogMember member, RaidMemberScanResult result)
    {
        var fragments = new List<string>();

        if (result.Achievements.Count > 0)
        {
            fragments.Add($"achievements: {string.Join(", ", result.Achievements.Select(match => match.Name))}");
        }

        if (result.Items.Count > 0)
        {
            fragments.Add($"items: {string.Join(", ", result.Items.Select(FormatItemMatch))}");
        }

        if (result.Mounts.Count > 0)
        {
            fragments.Add($"mounts: {string.Join(", ", result.Mounts.Select(match => match.Name))}");
        }

        lock (ConsoleWriteLock)
        {
            var previousColor = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write($"  ! {member.Name} - {RareScanCatalog.ClassNameFromId(member.ClassId)} - {member.DisplayRealm}: ");
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
                if (int.TryParse(property.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out var achievementId))
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

    private static bool IsExplicitFalse(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.False)
        {
            return true;
        }

        return element.ValueKind == JsonValueKind.String &&
               bool.TryParse(element.GetString(), out var value) &&
               !value;
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
               int.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out intValue)
            ? intValue
            : 0;
    }

    private static int ReadInt(JsonElement parent, string propertyName, int fallback)
    {
        var value = ReadInt(parent, propertyName);
        return value == 0 ? fallback : value;
    }

    private static string ReadString(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return string.Empty;
        }

        return property.GetString()?.Trim() ?? string.Empty;
    }

    private static string BuildApiUrl(string baseUrl, string apiKey)
    {
        var separator = baseUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return $"{baseUrl}{separator}apikey={Uri.EscapeDataString(apiKey)}";
    }

    private sealed record RaidLogRecord(
        int LogId,
        string MapName,
        string EncounterName,
        IReadOnlyList<RaidLogMember> Members);

    private sealed record RaidLogFetchResult(RaidLogRecord? RaidLog, string? StopReason, bool ShouldSkip)
    {
        public static RaidLogFetchResult Found(RaidLogRecord raidLog) =>
            new(raidLog, null, false);

        public static RaidLogFetchResult Skip() =>
            new(null, null, true);

        public static RaidLogFetchResult Stop(string reason) =>
            new(null, reason, false);
    }

    private sealed record RaidLogMember(
        string Name,
        string ApiRealm,
        string DisplayRealm,
        int ClassId,
        string GuildName);

    private sealed record RaidMemberScanResult(
        IReadOnlyList<RareAchievementMatch> Achievements,
        IReadOnlyList<RareItemMatch> Items,
        IReadOnlyList<RareMountMatch> Mounts)
    {
        public bool HasMatches => Achievements.Count > 0 || Items.Count > 0 || Mounts.Count > 0;
    }

    private sealed record RaidMemberScanSummary(
        int ScannedCharacterCount,
        int MatchedCharacterCount,
        int AchievementMatchCount,
        int ItemMatchCount,
        int MountMatchCount)
    {
        public static RaidMemberScanSummary Empty { get; } = new(0, 0, 0, 0, 0);
    }

    private readonly record struct CharacterKey(string Name, string Realm);

    private sealed class CharacterKeyComparer : IEqualityComparer<CharacterKey>
    {
        public bool Equals(CharacterKey x, CharacterKey y) =>
            string.Equals(x.Name, y.Name, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.Realm, y.Realm, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode(CharacterKey obj)
        {
            return HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Name),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Realm));
        }
    }
}

public sealed record RaidLogFinderResult(
    int StartLogId,
    int NextLogId,
    int RaidLogCount,
    int MemberCount,
    int ScannedCharacterCount,
    int MatchedCharacterCount,
    int AchievementMatchCount,
    int ItemMatchCount,
    int MountMatchCount,
    int NewGuildCount,
    string StatePath,
    string StopReason);

public sealed record RareAchievementMatch(int AchievementId, string Name);

public sealed record RareItemMatch(int ItemId, string Name);

public sealed record RareMountMatch(int SpellId, string Name);
