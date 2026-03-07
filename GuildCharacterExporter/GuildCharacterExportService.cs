using System.Text;
using System.Text.Json;
using AchievementLadder.Configuration;
using AchievementLadder.Dtos;

namespace GuildCharacterExporter;

public sealed class GuildCharacterExportService(string solutionRoot, TauriApiOptions apiOptions)
{
    private static readonly RealmSource[] RealmSources =
    [
        new("evermoon-guilds.txt", "[EN] Evermoon", "Evermoon"),
        new("tauri-guilds.txt", "[HU] Tauri WoW Server", "Tauri"),
        new("wod-guilds.txt", "[HU] Warriors of Darkness", "WoD")
    ];

    private readonly string _solutionRoot = Path.GetFullPath(solutionRoot);

    public async Task<GuildCharacterExportResult> ExportAsync(CancellationToken cancellationToken)
    {
        var guildDataDirectory = Path.Combine(_solutionRoot, "AchievementLadder", "Data", "Guilds");
        if (!Directory.Exists(guildDataDirectory))
        {
            throw new DirectoryNotFoundException($"Could not find guild data folder: {guildDataDirectory}");
        }

        using var client = new HttpClient();
        string apiUrl = BuildApiUrl(apiOptions.BaseUrl, apiOptions.ApiKey);

        var guilds = LoadGuilds(guildDataDirectory)
            .DistinctBy(guild => (guild.GuildName.ToLowerInvariant(), guild.ApiRealm))
            .ToList();

        var characterLines = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var processedGuildCount = 0;

        foreach (var guild in guilds)
        {
            var members = await LoadGuildMembersAsync(client, apiUrl, apiOptions.Secret, guild, cancellationToken);
            foreach (var memberName in members)
            {
                characterLines.Add($"{memberName}-{guild.DisplayRealm}");
            }

            processedGuildCount++;
            if (processedGuildCount % 25 == 0)
            {
                Console.WriteLine($"Loaded guilds {processedGuildCount}/{guilds.Count}");
            }
        }

        var orderedLines = characterLines
            .OrderBy(line => line, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var outputPath = Path.Combine(
            _solutionRoot,
            "AchievementLadder",
            "Data",
            "GuildCharacters",
            "GuildCharacters.txt");
        await WriteLinesAsync(outputPath, orderedLines, cancellationToken);

        return new GuildCharacterExportResult(orderedLines.Count, outputPath);
    }

    private static IEnumerable<GuildSource> LoadGuilds(string guildDataDirectory)
    {
        foreach (var source in RealmSources)
        {
            var path = Path.Combine(guildDataDirectory, source.FileName);
            if (!File.Exists(path))
            {
                continue;
            }

            foreach (var rawLine in File.ReadLines(path))
            {
                var guildName = rawLine?.Trim();
                if (string.IsNullOrWhiteSpace(guildName))
                {
                    continue;
                }

                yield return new GuildSource(guildName, source.ApiRealm, source.DisplayRealm);
            }
        }
    }

    private static async Task<IReadOnlyList<string>> LoadGuildMembersAsync(
        HttpClient client,
        string apiUrl,
        string secret,
        GuildSource guild,
        CancellationToken cancellationToken)
    {
        var body = new
        {
            secret,
            url = "guild-info",
            @params = new
            {
                r = guild.ApiRealm,
                gn = guild.GuildName
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
                Console.Error.WriteLine(
                    $"Skipping guild '{guild.GuildName}' on {guild.DisplayRealm}: API returned {(int)response.StatusCode} {response.ReasonPhrase}.");
                return [];
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var guildInfo = await JsonSerializer.DeserializeAsync<GuildInfoResponse>(
                stream,
                cancellationToken: cancellationToken);

            if (guildInfo?.response?.guildList is null)
            {
                return [];
            }

            return guildInfo.response.guildList.Values
                .Where(member => member.level >= 90)
                .Select(member => member.name?.Trim())
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Cast<string>()
                .ToList();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"Skipping guild '{guild.GuildName}' on {guild.DisplayRealm}: {ex.Message}");
            return [];
        }
    }

    private static async Task WriteLinesAsync(string path, IReadOnlyList<string> lines, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = path + ".tmp";
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        await using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true))
        await using (var writer = new StreamWriter(stream, encoding))
        {
            foreach (var line in lines)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await writer.WriteLineAsync(line);
            }
        }

        File.Move(tempPath, path, overwrite: true);
    }

    private static string BuildApiUrl(string baseUrl, string apiKey)
    {
        var separator = baseUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return $"{baseUrl}{separator}apikey={Uri.EscapeDataString(apiKey)}";
    }

    private sealed record RealmSource(string FileName, string ApiRealm, string DisplayRealm);

    private sealed record GuildSource(string GuildName, string ApiRealm, string DisplayRealm);
}
