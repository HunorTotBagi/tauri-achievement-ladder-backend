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
            new { GuildName = "Competence Optional", RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "Skill Issue",         RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "Despair",             RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "Mythic",              RealmApi = "[HU] Warriors of Darkness", RealmDisplay = "WoD" },
            new { GuildName = "Cara Máxima",         RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "Outlaws",             RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Guild of Multiverse", RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Defiance",            RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "Thunder",             RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "Army of Divergent",   RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "Infernum",            RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Vistustan",           RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "Yin Yang",            RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "Impact",              RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "Shadow Hunters",      RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "Fekete Hold",         RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "Last Whisper",        RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Punishers",           RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "Искатели легенд",     RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },  

            // Random guilds
            new { GuildName = "Wipes on Tresh",      RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "beloved",             RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "BOOSTED",             RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Insane",              RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Killepitsch",         RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Lighting Darkness",   RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Logic",               RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "Ace Of Spades",       RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "Ace Of Spadez",       RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "Solo Leveling",       RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "Last Try",            RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "Rycerze Ortalionu",   RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "Storm",               RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "Endless",             RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "CannabisCountryClub", RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "Calamity",            RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "Unicorns",            RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "Crows at Midnight",   RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "The World Eaters",    RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "Redemption",          RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "Aurora",              RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "Rosszcsontok",        RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "The Dark Exile",      RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "Army of Divergent",   RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "Nexxus",              RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "Ex Nihilo",           RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },

            // Random Random Guilds
            new { GuildName = "HiveFive",              RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "Siege",                 RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "OID MORTALES",          RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "Loktar Ogar",           RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "Wanderers of Azeroth",  RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },

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
            LastUpdated = todayUtc
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
}
