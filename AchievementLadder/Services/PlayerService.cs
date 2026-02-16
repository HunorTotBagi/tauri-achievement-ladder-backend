using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using AchievementLadder.Dtos;
using AchievementLadder.Helpers;
using AchievementLadder.Models;
using AchievementLadder.Repositories;

namespace AchievementLadder.Services;

public class PlayerService(IPlayerRepository playerRepository, IWebHostEnvironment webHostEnvironment, PlayerCsvStore csvStore) : IPlayerService
{
    public async Task SyncData(CancellationToken cancellationToken)
    {
        var projectRoot = webHostEnvironment.ContentRootPath;
        using var client = new HttpClient();

        var config = new ConfigurationBuilder()
            .SetBasePath(projectRoot)
            .AddJsonFile("appsettings.Development.json", optional: false, reloadOnChange: true)
            .Build();

        string baseUrl = config["TauriApi:BaseUrl"] ?? string.Empty;
        string apiKey = config["TauriApi:ApiKey"] ?? string.Empty;
        string secret = config["TauriApi:Secret"] ?? string.Empty;

        string apiUrl = $"{baseUrl}?apikey={apiKey}";

        var allCharacters = new List<(string Name, string ApiRealm, string DisplayRealm)>();

        CharacterHelpers.LoadCharacters(projectRoot, "evermoon-achi.txt", "[EN] Evermoon", "Evermoon", allCharacters);
        CharacterHelpers.LoadCharacters(projectRoot, "evermoon-hk.txt", "[EN] Evermoon", "Evermoon", allCharacters);
        CharacterHelpers.LoadCharacters(projectRoot, "evermoon-playTime.txt", "[EN] Evermoon", "Evermoon", allCharacters);

        CharacterHelpers.LoadCharacters(projectRoot, "tauri-achi.txt", "[HU] Tauri WoW Server", "Tauri", allCharacters);
        CharacterHelpers.LoadCharacters(projectRoot, "tauri-hk.txt", "[HU] Tauri WoW Server", "Tauri", allCharacters);
        CharacterHelpers.LoadCharacters(projectRoot, "tauri-playTime.txt", "[HU] Tauri WoW Server", "Tauri", allCharacters);

        CharacterHelpers.LoadCharacters(projectRoot, "wod-achi.txt", "[HU] Warriors of Darkness", "WoD", allCharacters);
        CharacterHelpers.LoadCharacters(projectRoot, "wod-hk.txt", "[HU] Warriors of Darkness", "WoD", allCharacters);
        CharacterHelpers.LoadCharacters(projectRoot, "wod-playTime.txt", "[HU] Warriors of Darkness", "WoD", allCharacters);

        var targetGuilds = new[]
        {
            // LOAD GUILDS
            new { GuildName = "The Invictus",          RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
        };

        foreach (var g in targetGuilds)
        {
            await CharacterHelpers.LoadGuildMembersLevel90Async(
                g.GuildName,
                g.RealmApi,
                g.RealmDisplay,
                apiUrl,
                secret,
                allCharacters,
                client);
        }

        var distinctCharacters = allCharacters
            .Where(x => !string.IsNullOrWhiteSpace(x.Name) && !x.Name.Contains('#'))
            .Distinct()
            .ToList();

        var cetZone = TimeZoneInfo.FindSystemTimeZoneById(
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? "Central European Standard Time" 
                : "Europe/Belgrade"
        );

        var today = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, cetZone);

        var players = new List<Player>(distinctCharacters.Count);

        const int maxConcurrency = 20;
        using var throttler = new SemaphoreSlim(maxConcurrency);

        int done = 0;

        var tasks = distinctCharacters.Select(async ch =>
        {
            await throttler.WaitAsync(cancellationToken);
            try
            {
                var p = await FetchPlayerAsync(
                    client, apiUrl, secret,
                    ch.Name, ch.ApiRealm, ch.DisplayRealm,
                    today,
                    cancellationToken);

                if (p is not null)
                {
                    lock (players) players.Add(p);
                }

                var current = Interlocked.Increment(ref done);
                if (current % 250 == 0)
                {
                    Console.WriteLine($"Fetched {current}/{distinctCharacters.Count}");
                }
            }
            finally
            {
                throttler.Release();
            }
        }).ToArray();

        await Task.WhenAll(tasks);

        await csvStore.WriteAsync(players, "Players.csv", cancellationToken);
        await csvStore.WriteLastUpdatedAsync(today, cancellationToken);
    }

    private static async Task<Player?> FetchPlayerAsync(
    HttpClient client,
    string apiUrl,
    string secret,
    string name,
    string apiRealm,
    string displayRealm,
    DateTime todayUtc,
    CancellationToken ct)
    {
        var body = new
        {
            secret,
            url = "character-sheet",
            @params = new { r = apiRealm, n = name }
        };

        using var content = new StringContent(
            JsonSerializer.Serialize(body),
            Encoding.UTF8,
            "application/json");

        using var response = await client.PostAsync(apiUrl, content, ct);
        if (!response.IsSuccessStatusCode) return null;

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        var root = doc.RootElement;
        if (!root.TryGetProperty("response", out var resp)) return null;

        int race = resp.TryGetProperty("race", out var v) ? v.GetInt32() : 0;
        int gender = resp.TryGetProperty("gender", out v) ? v.GetInt32() : 0;
        int @class = resp.TryGetProperty("class", out v) ? v.GetInt32() : 0;
        int pts = resp.TryGetProperty("pts", out v) ? v.GetInt32() : 0;
        int hk = resp.TryGetProperty("playerHonorKills", out v) ? v.GetInt32() : 0;
        string faction = resp.TryGetProperty("faction_string_class", out v) ? (v.GetString() ?? "") : "";
        string guild = resp.TryGetProperty("guildName", out v) ? (v.GetString() ?? "") : "";
        int mountCount = await FetchMountCountAsync(client, apiUrl, secret, name, apiRealm, ct);

        return new Player
        {
            Name = name,
            Race = race,
            Gender = gender,
            Class = @class,
            Realm = displayRealm,
            Guild = guild,
            AchievementPoints = pts,
            HonorableKills = hk,
            Faction = faction,
            LastUpdated = todayUtc,
            MountCount = mountCount
        };
    }

    public async Task<IReadOnlyList<LadderEntryDto>> GetSortedByAchievements(int pageNumber, int pageSize, string? realm = null, string? faction = null, int? playerClass = null, CancellationToken cancellationToken = default)
    {
        pageNumber = pageNumber < 1 ? 1 : pageNumber;
        pageSize = pageSize is < 1 or > 500 ? 100 : pageSize;

        var skip = (pageNumber - 1) * pageSize;

        var players = await playerRepository.GetSortedByAchievements(pageSize, skip, realm, faction, playerClass, cancellationToken);

        return players.Select(p => new LadderEntryDto(
            p.Name,
            p.Race,
            p.Gender,
            p.Class,
            p.Realm,
            p.Guild ?? string.Empty,
            p.AchievementPoints,
            p.HonorableKills,
            p.Faction ?? string.Empty
        )).ToList();
    }

    public async Task<IReadOnlyList<LadderEntryDto>> GetSortedByHonorableKills(int pageNumber, int pageSize, string? realm = null, string? faction = null, int? playerClass = null, CancellationToken cancellationToken = default)
    {
        pageNumber = pageNumber < 1 ? 1 : pageNumber;
        pageSize = pageSize is < 1 or > 500 ? 100 : pageSize;

        var skip = (pageNumber - 1) * pageSize;

        var players = await playerRepository.GetSortedByHonorableKills(pageSize, skip, realm, faction, playerClass, cancellationToken);

        return players.Select(p => new LadderEntryDto(
            p.Name,
            p.Race,
            p.Gender,
            p.Class,
            p.Realm,
            p.Guild ?? string.Empty,
            p.AchievementPoints,
            p.HonorableKills,
            p.Faction ?? string.Empty
        )).ToList();
    }

    private static async Task<int> FetchMountCountAsync(
        HttpClient client,
        string apiUrl,
        string secret,
        string name,
        string apiRealm,
        CancellationToken ct)
    {
        var body = new
        {
            secret,
            url = "character-mounts",
            @params = new { r = apiRealm, n = name }
        };

        using var content = new StringContent(
            JsonSerializer.Serialize(body),
            Encoding.UTF8,
            "application/json");

        using var response = await client.PostAsync(apiUrl, content, ct);
        if (!response.IsSuccessStatusCode) return 0;

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        if (!doc.RootElement.TryGetProperty("response", out var resp)) return 0;

        // mounts is at response.mounts
        if (!resp.TryGetProperty("mounts", out var mountsEl)) return 0;
        if (mountsEl.ValueKind != JsonValueKind.Array) return 0;

        return mountsEl.GetArrayLength();
    }

}
