using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using AchievementLadder.Configuration;
using AchievementLadder.Shared;

namespace BattlegroundExtractor;

public sealed class BattlegroundExtractorService(string solutionRoot, TauriApiOptions apiOptions)
{
    private const int MaxDegreeOfParallelism = 10;

    private static readonly object ConsoleWriteLock = new();

    // Maps a realm name (as it appears in the match members) to its guild list file.
    // Matching is done by substring so both display and full API realm names resolve.
    private static readonly (string Token, string FileName)[] GuildFileByRealm =
    [
        ("Evermoon", "evermoon-guilds.txt"),
        ("Tauri", "tauri-guilds.txt"),
        ("Warriors of Darkness", "wod-guilds.txt")
    ];

    private readonly string _guildsDirectory = Path.Combine(solutionRoot, "AchievementLadder", "Data", "Guilds");
    private readonly TauriApiOptions _apiOptions = apiOptions;

    public async Task ExecuteAsync(ExtractorOptions options, CancellationToken cancellationToken)
    {
        using var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(_apiOptions.RequestTimeoutSeconds)
        };
        var apiUrl = BuildApiUrl(_apiOptions.BaseUrl, _apiOptions.ApiKey);

        Console.WriteLine($"Fetching match(es) {options.DescribeRange} on {options.DisplayRealm}...");

        var allMembers = await FetchRangeMembersAsync(client, apiUrl, _apiOptions.Secret, options, cancellationToken);
        if (allMembers.Count == 0)
        {
            Console.WriteLine("No members found across the requested match(es).");
            return;
        }

        // The same player can appear in many matches across a range — scan each only once.
        var distinctMembers = allMembers
            .DistinctBy(member => $"{member.CharName}|{member.RealmName}".ToLowerInvariant())
            .OrderBy(member => member.CharName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Console.WriteLine($"Collected {distinctMembers.Count} unique member(s).");
        Console.WriteLine();

        await ScanRareAchievementsAsync(client, apiUrl, _apiOptions.Secret, distinctMembers, cancellationToken);

        Console.WriteLine();
        CollectUnknownGuilds(distinctMembers);
    }

    private async Task<List<MatchMember>> FetchRangeMembersAsync(
        HttpClient client,
        string apiUrl,
        string secret,
        ExtractorOptions options,
        CancellationToken cancellationToken)
    {
        var allMembers = new ConcurrentBag<MatchMember>();
        var total = options.MatchCount;
        var processed = 0;
        var matchesWithData = 0;

        await Parallel.ForEachAsync(
            options.MatchIds,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = MaxDegreeOfParallelism,
                CancellationToken = cancellationToken
            },
            async (matchId, ct) =>
            {
                try
                {
                    var members = await FetchMatchMembersAsync(client, apiUrl, secret, options.ApiRealm, matchId, ct);
                    if (members.Count > 0)
                    {
                        Interlocked.Increment(ref matchesWithData);
                        foreach (var member in members)
                        {
                            allMembers.Add(member);
                        }
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"  Skipping match {matchId}: {ex.Message}");
                }
                finally
                {
                    var current = Interlocked.Increment(ref processed);
                    if (options.IsRange && (current % 100 == 0 || current == total))
                    {
                        Console.WriteLine(
                            $"  Fetched {current}/{total} match(es) — {Volatile.Read(ref matchesWithData)} with data.");
                    }
                }
            });

        if (options.IsRange)
        {
            Console.WriteLine($"Matches with data: {matchesWithData}/{total}.");
        }

        return allMembers.ToList();
    }

    private static async Task<List<MatchMember>> FetchMatchMembersAsync(
        HttpClient client,
        string apiUrl,
        string secret,
        string apiRealm,
        int matchId,
        CancellationToken cancellationToken)
    {
        var body = new
        {
            secret,
            url = "pvp-match",
            @params = new
            {
                r = apiRealm,
                matchid = matchId.ToString()
            }
        };

        using var content = new StringContent(
            JsonSerializer.Serialize(body),
            Encoding.UTF8,
            "application/json");

        using var response = await client.PostAsync(apiUrl, content, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            // A non-existent match id is expected within a range — skip it quietly.
            return [];
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (document.RootElement.TryGetProperty("success", out var successElement) &&
            successElement.ValueKind == JsonValueKind.False)
        {
            return [];
        }

        if (!document.RootElement.TryGetProperty("response", out var responseElement) ||
            !responseElement.TryGetProperty("members", out var membersElement) ||
            membersElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var members = new List<MatchMember>();
        foreach (var memberElement in membersElement.EnumerateArray())
        {
            if (memberElement.ValueKind != JsonValueKind.Object ||
                !memberElement.TryGetProperty("character-minimal-data", out var minimal) ||
                minimal.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var charName = ReadString(minimal, "charname").Trim();
            if (string.IsNullOrWhiteSpace(charName))
            {
                continue;
            }

            members.Add(new MatchMember(
                charName,
                ReadInt(minimal, "class"),
                ReadString(minimal, "guildname").Trim(),
                ReadString(memberElement, "realmName").Trim()));
        }

        return members;
    }

    private static async Task ScanRareAchievementsAsync(
        HttpClient client,
        string apiUrl,
        string secret,
        IReadOnlyList<MatchMember> members,
        CancellationToken cancellationToken)
    {
        Console.WriteLine("=== Rare achievement scan ===");

        var matchCount = 0;

        await Parallel.ForEachAsync(
            members,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = MaxDegreeOfParallelism,
                CancellationToken = cancellationToken
            },
            async (member, ct) =>
            {
                try
                {
                    var achievements = await FetchMatchingAchievementsAsync(client, apiUrl, secret, member, ct);
                    if (achievements.Count == 0)
                    {
                        return;
                    }

                    Interlocked.Increment(ref matchCount);
                    WriteAchievementSummary(member, achievements);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"  Skipping {member.CharName} ({member.RealmName}): {ex.Message}");
                }
            });

        if (matchCount == 0)
        {
            Console.WriteLine("No rare achievements found among match members.");
        }
    }

    private static void WriteAchievementSummary(MatchMember member, IReadOnlyList<string> achievements)
    {
        var className = RareScanCatalog.ClassNameFromId(member.ClassId);

        lock (ConsoleWriteLock)
        {
            var previousColor = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write($"{member.CharName} - {className} - {member.RealmName}: ");
            Console.ForegroundColor = previousColor;
            Console.WriteLine(string.Join(", ", achievements));
        }
    }

    private static async Task<IReadOnlyList<string>> FetchMatchingAchievementsAsync(
        HttpClient client,
        string apiUrl,
        string secret,
        MatchMember member,
        CancellationToken cancellationToken)
    {
        var body = new
        {
            secret,
            url = "character-achievements",
            @params = new
            {
                r = member.RealmName,
                n = member.CharName
            }
        };

        using var content = new StringContent(
            JsonSerializer.Serialize(body),
            Encoding.UTF8,
            "application/json");

        using var response = await client.PostAsync(apiUrl, content, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return Array.Empty<string>();
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (!document.RootElement.TryGetProperty("response", out var responseElement))
        {
            return Array.Empty<string>();
        }

        var achievedIds = ExtractAchievementIds(responseElement);

        return RareScanCatalog.RareAchievementNames
            .Where(kvp => achievedIds.Contains(kvp.Key))
            .Select(kvp => kvp.Value)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void CollectUnknownGuilds(IReadOnlyList<MatchMember> members)
    {
        Console.WriteLine("=== Guild collection ===");

        Directory.CreateDirectory(_guildsDirectory);

        // Cache of guild names already known per file, keyed by file name.
        var knownGuildsByFile = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var addedCount = 0;
        var skippedUnknownRealm = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var member in members)
        {
            if (string.IsNullOrWhiteSpace(member.GuildName))
            {
                continue;
            }

            var fileName = ResolveGuildFileName(member.RealmName);
            if (fileName is null)
            {
                if (skippedUnknownRealm.Add(member.RealmName))
                {
                    Console.WriteLine($"  No guild file for realm '{member.RealmName}' — skipping its guilds.");
                }

                continue;
            }

            var filePath = Path.Combine(_guildsDirectory, fileName);
            if (!knownGuildsByFile.TryGetValue(fileName, out var knownGuilds))
            {
                knownGuilds = LoadGuildSet(filePath);
                knownGuildsByFile[fileName] = knownGuilds;
            }

            if (!knownGuilds.Add(member.GuildName))
            {
                continue;
            }

            AppendGuildName(filePath, member.GuildName);
            addedCount++;
            Console.WriteLine($"  + Added '{member.GuildName}' to {fileName}");
        }

        Console.WriteLine(addedCount == 0
            ? "No new guilds — all guilds already known."
            : $"Added {addedCount} new guild name(s).");
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
            var trimmed = line.Trim().TrimStart('﻿');
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
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        var textToAppend = (needsLeadingNewline ? Environment.NewLine : string.Empty) + guildName + Environment.NewLine;
        File.AppendAllText(filePath, textToAppend, encoding);
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

    private sealed record MatchMember(string CharName, int ClassId, string GuildName, string RealmName);
}
