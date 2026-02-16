using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using AchievementLadder.Dtos;
using AchievementLadder.Helpers;
using AchievementLadder.Models;
using AchievementLadder.Repositories;

namespace AchievementLadder.Services;

public class PlayerService(
    IPlayerRepository playerRepository,
    IWebHostEnvironment webHostEnvironment,
    PlayerCsvStore csvStore,
    IConfiguration configuration) : IPlayerService
{
    private static readonly Dictionary<int, string> TargetItems = new()
    {
        { 22818, "The Plague Bearer" },
        { 23075, "Death's Bargain" },

        { 85046, "Death Knight Malevolent Elite Head" },
        { 85086, "Death Knight Malevolent Elite Shoulders" },
        { 84993, "Death Knight Malevolent Elite Chest" },
        { 73740, "Death Knight Cataclysmic Elite Head" },

        { 45983, "Furious Gladiator's Tabard" },
        { 49086, "Relentless Gladiator's Tabard" },
        { 51534, "Wrathful Gladiator's Tabard" },
        { 98162, "Tyrannical Gladiator's Tabard" },

        { 22701, "Polar Leggings" },
        { 22662, "Polar Gloves" },

        { 22691, "Corrupted Ashbringer" },
        { 18608, "Benediction" },
        { 18609, "Anathema" },
        { 18713, "Rhok'delar, Longbow of the Ancient Keepers" },
        { 18715, "Lok'delar, Stave of the Ancient Keepers" },

        { 73680, "Cataclysmic Gladiator's Leather Helm" },
        { 73679, "Cataclysmic Gladiator's Leather Legguards" },
        { 73678, "Cataclysmic Gladiator's Leather Spaulders" },
        { 73681, "Cataclysmic Gladiator's Leather Gloves" },
        { 73682, "Cataclysmic Gladiator's Leather Tunic" },
        { 65522, "Vicious Chest" },
        { 65520, "Vicious Head" },
        { 65518, "Vicious Shoulders" },
        { 65519, "Vicious Legs" },
        { 65605, "Vicious Boots" },
        { 65521, "Vicious Gloves" },
    };

    private static readonly HashSet<int> TargetItemIds = TargetItems.Keys.ToHashSet();

    private static readonly IReadOnlyDictionary<int, string> ClassNames = new Dictionary<int, string>
    {
        { 1, "Warrior" },
        { 2, "Paladin" },
        { 3, "Hunter" },
        { 4, "Rogue" },
        { 5, "Priest" },
        { 6, "Death Knight" },
        { 7, "Shaman" },
        { 8, "Mage" },
        { 9, "Warlock" },
        { 10, "Monk" },
        { 11, "Druid" },
        { 12, "Demon Hunter" }
    };

    private static readonly Dictionary<int, string> RareAchievementNames = new()
    {
        { 416,  "Scarab Lord" },
        { 418,  "Merciless R1" },
        { 419,  "Vengeful R1" },
        { 420,  "Brutal R1" },
        { 425,  "Atiesh" },
        { 886,  "Swift Nether Drake" },
        { 887,  "Merciless Nether Drake" },
        { 888,  "Vengeful Nether Drake" },
        { 2316, "Brutal Nether Drake" },
        { 3336, "Deadly R1" },
        { 3436, "Furious R1" },
        { 3758, "Relentless R1" },
        { 4599, "Wrathful R1" },
        { 6002, "Vicious R1" },
        { 6124, "Ruthless R1" },
        { 6938, "Cataclysmic R1" },
        { 8214, "Malevolent R1" },
        { 8216, "Malevolent Gladiator's Serpent" },
        { 8643, "Grievous R1" },
        { 8678, "Tyrannical Gladiator" },
        { 8666, "Prideful R1" },
        { 6939, "Hero of the Alliance: Cataclysmic" },
        { 8654, "Hero of the Alliance: Grievous" },
        { 8243, "Hero of the Alliance: Malevolent" },
        { 8658, "Hero of the Alliance: Prideful" },
        { 6316, "Hero of the Alliance: Ruthless" },
        { 8652, "Hero of the Alliance: Tyrannical" },
        { 5344, "Hero of the Alliance: Vicious" },
        { 6940, "Hero of the Horde: Cataclysmic" },
        { 8657, "Hero of the Horde: Grievous" },
        { 8244, "Hero of the Horde: Malevolent" },
        { 8659, "Hero of the Horde: Prideful" },
        { 8653, "Hero of the Horde: Tyrannical" },
        { 6317, "Hero of the Horde: Ruthless" },
        { 5358, "Hero of the Horde: Vicious" },
        { 3096, "Deadly Gladiator's Drake" },
        { 3756, "Furious Gladiator's Drake" },
        { 3757, "Relentless Gladiator's Drake" },
        { 4600, "Wrathful Gladiator's Drake" },
        { 6003, "Vicious Gladiator's Drake" },
        { 6322, "Ruthless Gladiator's Drake" },
        { 6741, "Cataclysmic Gladiator's Drake" },
        { 8707, "Prideful Gladiator's Drake" },
        { 432,  "Champion of the Naaru" },
        { 5329, "300 RBG Wins" },
        { 6942, "Hero of the Alliance" },
        { 6941, "Hero of the Horde" },
        { 8791, "Tyrannical R1" },
        { 3117, "Realm First! Death's Demise" },
        { 3259, "Realm First! Celestial Defender" },
        { 6433, "Realm First! Challenge Conqueror: Gold" },
        { 1402, "Realm First! Conqueror of Naxxramas" },
        { 4576, "Realm First! Fall of the Lich King" },
        { 4078, "Realm First! Grand Crusader" },
        { 1400, "Realm First! Magic Seeker" },
        { 1463, "Realm First! Northrend Vanguard" },
        { 456,  "Realm First! Obsidian Slayer" },
        { 1415, "RF Alchemy 450" },
        { 1420, "RF Fishing 450" },
        { 5395, "RF Archeology 450" },
        { 1414, "RF Blacksmithing 450" },
        { 1416, "RF Cooking 450" },
        { 1417, "RF Enchanting 450" },
        { 1418, "RF Engineer 450" },
        { 1421, "RF Herbalism 450" },
        { 1423, "RF Jewelcrafter 450" },
        { 1424, "RF Leatherworking 450" },
        { 1419, "RF First Aid 450" },
        { 1425, "RF Mining 450" },
        { 1422, "RF Inscription 450" },
        { 1426, "RF Skinning 450" },
        { 1427, "RF Tailor 450" },
        { 457,  "RF Level 80" },
        { 1405, "RF Blood Elf 80" },
        { 461,  "RF DK 80" },
        { 1406, "RF Draenei 80" },
        { 466,  "RF Druid 80" },
        { 1407, "RF Dwarf 80" },
        { 1413, "RF Undead 80" },
        { 1404, "RF Gnome 80" },
        { 1408, "RF Human 80" },
        { 462,  "RF Hunter 80" },
        { 460,  "RF Mage 80" },
        { 1409, "RF Night Elf 80" },
        { 1410, "RF Orc 80" },
        { 465,  "RF Paladin 80" },
        { 464,  "RF Priest 80" },
        { 458,  "RF Rogue 80" },
        { 467,  "RF Shaman 80" },
        { 1411, "RF Tauren 80" },
        { 1412, "RF Troll 80" },
        { 463,  "RF Warlock 80" },
        { 459,  "RF Warrior 80" },
        { 5381, "RF Alchemy 525" },
        { 5387, "RF Fishing 525" },
        { 5396, "RF Archeology 525" },
        { 5382, "RF Blacksmithing 525" },
        { 5383, "RF Cooking 525" },
        { 5384, "RF Enchanting 525" },
        { 5385, "RF Engineer 525" },
        { 5388, "RF Herbalism 525" },
        { 5390, "RF Jewelcrafter 525" },
        { 5391, "RF Leatherworking 525" },
        { 5386, "RF First Aid 525" },
        { 5392, "RF Mining 525" },
        { 5389, "RF Inscription 525" },
        { 5393, "RF Skinning 525" },
        { 5394, "RF Tailor 525" },
        { 4999, "RF Level 85" },
        { 5005, "RF DK 85" },
        { 5000, "RF Druid 85" },
        { 5004, "RF Hunter 85" },
        { 5006, "RF Mage 85" },
        { 5001, "RF Paladin 85" },
        { 5002, "RF Priest 85" },
        { 5008, "RF Rogue 85" },
        { 4998, "RF Shaman 85" },
        { 5003, "RF Warlock 85" },
        { 5007, "RF Warrior 85" },
        { 6829, "Realm First! Pandaren Ambassador" },
        { 6859, "RF Alchemy 600" },
        { 6865, "RF Fishing 600" },
        { 6873, "RF Archeology 600" },
        { 6860, "RF Blacksmithing 600" },
        { 6861, "RF Cooking 600" },
        { 6862, "RF Enchanting 600" },
        { 6863, "RF Engineer 600" },
        { 6866, "RF Herbalism 600" },
        { 6868, "RF Jewelcrafter 600" },
        { 6869, "RF Leatherworking 600" },
        { 6864, "RF First Aid 600" },
        { 6870, "RF Mining 600" },
        { 6867, "RF Inscription 600" },
        { 6871, "RF Skinning 600" },
        { 6872, "RF Tailor 600" },
        { 6524, "RF Level 90" },
        { 6748, "RF DK 90" },
        { 6743, "RF Druid 90" },
        { 6747, "RF Hunter 90" },
        { 6749, "RF Mage 90" },
        { 6752, "RF Monk 90" },
        { 6744, "RF Paladin 90" },
        { 6745, "RF Priest 90" },
        { 6751, "RF Rogue 90" },
        { 6523, "RF Shaman 90" },
        { 6746, "RF Warlock 90" },
        { 6750, "RF Warrior 90" }
    };

    private static readonly object ConsoleWriteLock = new();

    private static void WriteColoredPrefixLine(string prefix, string content)
    {
        lock (ConsoleWriteLock)
        {
            var previousColor = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write(prefix);
            Console.ForegroundColor = previousColor;
            Console.WriteLine(content);
        }
    }

    public async Task SyncData(CancellationToken cancellationToken)
    {
        var (apiUrl, secret) = GetTauriApiConfig();
        var projectRoot = webHostEnvironment.ContentRootPath;
        using var client = new HttpClient();

        var distinctCharacters = await LoadDistinctCharactersAsync(
            projectRoot,
            apiUrl,
            secret,
            client,
            cancellationToken);

        var cetZone = TimeZoneInfo.FindSystemTimeZoneById(
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? "Central European Standard Time"
                : "Europe/Belgrade"
        );

        var today = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, cetZone);
        var players = new List<Player>(distinctCharacters.Count);

        const int maxConcurrency = 20;
        using var throttler = new SemaphoreSlim(maxConcurrency);
        var done = 0;

        var tasks = distinctCharacters.Select(async ch =>
        {
            await throttler.WaitAsync(cancellationToken);
            try
            {
                var p = await FetchPlayerAsync(
                    client,
                    apiUrl,
                    secret,
                    ch.Name,
                    ch.ApiRealm,
                    ch.DisplayRealm,
                    today,
                    cancellationToken);

                if (p is not null)
                {
                    lock (players)
                    {
                        players.Add(p);
                    }
                }
            }
            finally
            {
                var current = Interlocked.Increment(ref done);
                if (current % 250 == 0)
                {
                    Console.WriteLine($"Fetched {current}/{distinctCharacters.Count}");
                }

                throttler.Release();
            }
        }).ToArray();

        await Task.WhenAll(tasks);

        await csvStore.WriteAsync(players, "Players.csv", cancellationToken);
        await csvStore.WriteLastUpdatedAsync(today, cancellationToken);
    }

    public async Task<IReadOnlyList<ItemScanResultDto>> ScanItems(CancellationToken cancellationToken = default)
    {
        var (apiUrl, secret) = GetTauriApiConfig();
        var projectRoot = webHostEnvironment.ContentRootPath;
        using var client = new HttpClient();

        var classByCharacter = new Dictionary<string, int>(StringComparer.Ordinal);
        var distinctCharacters = await LoadDistinctCharactersAsync(
            projectRoot,
            apiUrl,
            secret,
            client,
            cancellationToken,
            classByCharacter);

        var results = new ConcurrentBag<ItemScanResultDto>();
        const int maxConcurrency = 20;
        using var throttler = new SemaphoreSlim(maxConcurrency);
        var done = 0;

        var tasks = distinctCharacters.Select(async ch =>
        {
            await throttler.WaitAsync(cancellationToken);
            try
            {
                var matches = await FetchOwnedMatchingItemsAsync(
                    client,
                    apiUrl,
                    secret,
                    ch.Name,
                    ch.ApiRealm,
                    cancellationToken);

                if (matches.Count == 0)
                    return;

                var classId = await ResolveClassAsync(
                    client,
                    apiUrl,
                    secret,
                    ch.Name,
                    ch.ApiRealm,
                    ch.DisplayRealm,
                    classByCharacter,
                    cancellationToken);

                var className = ClassNameFromId(classId);
                var matchedItems = matches
                    .Select(m => new ItemScanMatchDto(
                        m.ItemId,
                        TargetItems.TryGetValue(m.ItemId, out var label) ? label : m.Name))
                    .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var scanPrefix = $"{ch.Name} - {className} - {ch.DisplayRealm}: ";
                var scanItems = string.Join(", ", matchedItems.Select(x => x.Name));
                WriteColoredPrefixLine(scanPrefix, scanItems);

                results.Add(new ItemScanResultDto(
                    ch.Name,
                    ch.DisplayRealm,
                    classId,
                    className,
                    matchedItems));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Item scan error for {ch.Name}-{ch.DisplayRealm}: {ex.Message}");
            }
            finally
            {
                var current = Interlocked.Increment(ref done);
                if (current % 250 == 0)
                {
                    Console.WriteLine($" ");
                    Console.WriteLine($"Item scan progress {current}/{distinctCharacters.Count}");
                    Console.WriteLine($" ");
                }

                throttler.Release();
            }
        }).ToArray();

        await Task.WhenAll(tasks);

        return results
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Realm, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<IReadOnlyList<AchievementScanResultDto>> ScanAchievements(CancellationToken cancellationToken = default)
    {
        var (apiUrl, secret) = GetTauriApiConfig();
        var projectRoot = webHostEnvironment.ContentRootPath;
        using var client = new HttpClient();

        var classByCharacter = new Dictionary<string, int>(StringComparer.Ordinal);
        var distinctCharacters = await LoadDistinctCharactersAsync(
            projectRoot,
            apiUrl,
            secret,
            client,
            cancellationToken,
            classByCharacter);

        var results = new ConcurrentBag<AchievementScanResultDto>();
        const int maxConcurrency = 20;
        using var throttler = new SemaphoreSlim(maxConcurrency);
        var done = 0;

        var tasks = distinctCharacters.Select(async ch =>
        {
            await throttler.WaitAsync(cancellationToken);
            try
            {
                var matches = await FetchMatchingAchievementsAsync(
                    client,
                    apiUrl,
                    secret,
                    ch.Name,
                    ch.ApiRealm,
                    cancellationToken);

                if (matches.Count == 0)
                    return;

                var classId = await ResolveClassAsync(
                    client,
                    apiUrl,
                    secret,
                    ch.Name,
                    ch.ApiRealm,
                    ch.DisplayRealm,
                    classByCharacter,
                    cancellationToken);

                var className = ClassNameFromId(classId);
                var matchedAchievements = matches
                    .Select(m => new AchievementScanMatchDto(m.AchievementId, m.Name))
                    .ToList();

                var scanPrefix = $"{ch.Name} - {className} - {ch.DisplayRealm}: ";
                var scanAchievements = string.Join(", ", matchedAchievements.Select(x => x.Name));
                WriteColoredPrefixLine(scanPrefix, scanAchievements);

                results.Add(new AchievementScanResultDto(
                    ch.Name,
                    ch.DisplayRealm,
                    classId,
                    className,
                    matchedAchievements));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Achievement scan error for {ch.Name}-{ch.DisplayRealm}: {ex.Message}");
            }
            finally
            {
                var current = Interlocked.Increment(ref done);
                if (current % 250 == 0)
                { 
                    Console.WriteLine($" ");
                    Console.WriteLine($"Achievement scan progress {current}/{distinctCharacters.Count}");
                    Console.WriteLine($" ");
                }

                throttler.Release();
            }
        }).ToArray();

        await Task.WhenAll(tasks);

        return results
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Realm, StringComparer.OrdinalIgnoreCase)
            .ToList();
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

    private async Task<List<(string Name, string ApiRealm, string DisplayRealm)>> LoadDistinctCharactersAsync(
        string projectRoot,
        string apiUrl,
        string secret,
        HttpClient client,
        CancellationToken ct,
        IDictionary<string, int>? classByCharacter = null)
    {
        var allCharacters = new List<(string Name, string ApiRealm, string DisplayRealm)>();

        CharacterHelpers.LoadCharacters(projectRoot, "evermoon-achi.txt", "[EN] Evermoon", "Evermoon", allCharacters, classByCharacter);
        CharacterHelpers.LoadCharacters(projectRoot, "evermoon-hk.txt", "[EN] Evermoon", "Evermoon", allCharacters, classByCharacter);
        CharacterHelpers.LoadCharacters(projectRoot, "evermoon-playTime.txt", "[EN] Evermoon", "Evermoon", allCharacters, classByCharacter);

        CharacterHelpers.LoadCharacters(projectRoot, "tauri-achi.txt", "[HU] Tauri WoW Server", "Tauri", allCharacters, classByCharacter);
        CharacterHelpers.LoadCharacters(projectRoot, "tauri-hk.txt", "[HU] Tauri WoW Server", "Tauri", allCharacters, classByCharacter);
        CharacterHelpers.LoadCharacters(projectRoot, "tauri-playTime.txt", "[HU] Tauri WoW Server", "Tauri", allCharacters, classByCharacter);

        CharacterHelpers.LoadCharacters(projectRoot, "wod-achi.txt", "[HU] Warriors of Darkness", "WoD", allCharacters, classByCharacter);
        CharacterHelpers.LoadCharacters(projectRoot, "wod-hk.txt", "[HU] Warriors of Darkness", "WoD", allCharacters, classByCharacter);
        CharacterHelpers.LoadCharacters(projectRoot, "wod-playTime.txt", "[HU] Warriors of Darkness", "WoD", allCharacters, classByCharacter);

        await GuildHelpers.LoadGuildMembersFromFilesAsync(
            projectRoot,
            apiUrl,
            secret,
            client,
            allCharacters,
            ct,
            maxConcurrency: 4,
            classByCharacter: classByCharacter);

        return allCharacters
            .Where(x => !string.IsNullOrWhiteSpace(x.Name) && !x.Name.Contains('#'))
            .Distinct()
            .ToList();
    }

    private (string ApiUrl, string Secret) GetTauriApiConfig()
    {
        var baseUrl = configuration["TauriApi:BaseUrl"] ?? string.Empty;
        var apiKey = configuration["TauriApi:ApiKey"] ?? string.Empty;
        var secret = configuration["TauriApi:Secret"] ?? string.Empty;

        if (string.IsNullOrWhiteSpace(baseUrl) ||
            string.IsNullOrWhiteSpace(apiKey) ||
            string.IsNullOrWhiteSpace(secret))
        {
            throw new InvalidOperationException("Missing TauriApi configuration. Configure TauriApi:BaseUrl, TauriApi:ApiKey, and TauriApi:Secret.");
        }

        return ($"{baseUrl}?apikey={apiKey}", secret);
    }

    private static string ClassNameFromId(int classId)
    {
        if (ClassNames.TryGetValue(classId, out var value))
            return value;

        return classId > 0 ? $"Class {classId}" : "Unknown";
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
        if (!response.IsSuccessStatusCode)
            return null;

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        var root = doc.RootElement;
        if (!root.TryGetProperty("response", out var resp))
            return null;

        var race = ReadInt(resp, "race");
        var gender = ReadInt(resp, "gender");
        var @class = ReadInt(resp, "class");
        var pts = ReadInt(resp, "pts");
        var hk = ReadInt(resp, "playerHonorKills");
        var faction = ReadString(resp, "faction_string_class");
        var guild = ReadString(resp, "guildName");
        var mountCount = await FetchMountCountAsync(client, apiUrl, secret, name, apiRealm, ct);

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
        if (!response.IsSuccessStatusCode)
            return 0;

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        if (!doc.RootElement.TryGetProperty("response", out var resp))
            return 0;

        if (!resp.TryGetProperty("mounts", out var mountsEl))
            return 0;

        return mountsEl.ValueKind == JsonValueKind.Array
            ? mountsEl.GetArrayLength()
            : 0;
    }

    private static async Task<List<(int ItemId, string Name)>> FetchOwnedMatchingItemsAsync(
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
            url = "character-itemappearances",
            @params = new { r = apiRealm, n = name }
        };

        using var content = new StringContent(
            JsonSerializer.Serialize(body),
            Encoding.UTF8,
            "application/json");

        using var response = await client.PostAsync(apiUrl, content, ct);
        if (!response.IsSuccessStatusCode)
            return new List<(int ItemId, string Name)>();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        if (!doc.RootElement.TryGetProperty("response", out var responseEl))
            return new List<(int ItemId, string Name)>();

        return ExtractOwnedMatchingItems(responseEl, TargetItemIds);
    }

    private static async Task<List<(int AchievementId, string Name)>> FetchMatchingAchievementsAsync(
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
            url = "character-achievements",
            @params = new { r = apiRealm, n = name }
        };

        using var content = new StringContent(
            JsonSerializer.Serialize(body),
            Encoding.UTF8,
            "application/json");

        using var response = await client.PostAsync(apiUrl, content, ct);
        if (!response.IsSuccessStatusCode)
            return new List<(int AchievementId, string Name)>();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        if (!doc.RootElement.TryGetProperty("response", out var responseEl))
            return new List<(int AchievementId, string Name)>();

        var achievedIds = ExtractAchievementIds(responseEl);

        return RareAchievementNames
            .Where(kvp => achievedIds.Contains(kvp.Key))
            .Select(kvp => (kvp.Key, kvp.Value))
            .ToList();
    }

    private static async Task<int> FetchCharacterClassAsync(
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
            url = "character-sheet",
            @params = new { r = apiRealm, n = name }
        };

        using var content = new StringContent(
            JsonSerializer.Serialize(body),
            Encoding.UTF8,
            "application/json");

        using var response = await client.PostAsync(apiUrl, content, ct);
        if (!response.IsSuccessStatusCode)
            return 0;

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        if (!doc.RootElement.TryGetProperty("response", out var responseEl))
            return 0;

        return ReadInt(responseEl, "class");
    }

    private static async Task<int> ResolveClassAsync(
        HttpClient client,
        string apiUrl,
        string secret,
        string name,
        string apiRealm,
        string displayRealm,
        IDictionary<string, int> classByCharacter,
        CancellationToken ct)
    {
        var key = CharacterHelpers.CreateCharacterKey(name, displayRealm);

        lock (classByCharacter)
        {
            if (classByCharacter.TryGetValue(key, out var cachedClass) && cachedClass > 0)
                return cachedClass;
        }

        var classId = await FetchCharacterClassAsync(client, apiUrl, secret, name, apiRealm, ct);

        lock (classByCharacter)
        {
            if (!classByCharacter.ContainsKey(key))
                classByCharacter[key] = classId;
        }

        return classId;
    }

    private static int ReadInt(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var property))
            return 0;

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var intValue))
            return intValue;

        if (property.ValueKind == JsonValueKind.String &&
            int.TryParse(property.GetString(), out intValue))
        {
            return intValue;
        }

        return 0;
    }

    private static string ReadString(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var property))
            return string.Empty;

        return property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : string.Empty;
    }

    private static HashSet<int> ExtractAchievementIds(JsonElement responseElement)
    {
        if (!responseElement.TryGetProperty("Achievements", out var achievementsEl))
            return new HashSet<int>();

        var ids = new HashSet<int>();

        if (achievementsEl.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in achievementsEl.EnumerateObject())
            {
                if (int.TryParse(prop.Name, out var id))
                    ids.Add(id);
            }
        }
        else if (achievementsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in achievementsEl.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out var id))
                    ids.Add(id);
            }
        }

        return ids;
    }

    private static List<(int ItemId, string Name)> ExtractOwnedMatchingItems(JsonElement responseEl, HashSet<int> targetItemIds)
    {
        if (!responseEl.TryGetProperty("itemappearances", out var itemAppEl))
            return new List<(int, string)>();

        if (!itemAppEl.TryGetProperty("owned", out var ownedEl))
            return new List<(int, string)>();

        if (ownedEl.ValueKind != JsonValueKind.Array)
            return new List<(int, string)>();

        var found = new Dictionary<int, string>();

        foreach (var categoryArray in ownedEl.EnumerateArray())
        {
            if (categoryArray.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var itemEl in categoryArray.EnumerateArray())
            {
                if (itemEl.ValueKind != JsonValueKind.Object)
                    continue;

                if (!itemEl.TryGetProperty("itemid", out var itemIdEl))
                    continue;

                if (!itemIdEl.TryGetInt32(out var itemId))
                    continue;

                if (!targetItemIds.Contains(itemId))
                    continue;

                var name = itemEl.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String
                    ? nameEl.GetString() ?? $"itemid={itemId}"
                    : $"itemid={itemId}";

                found[itemId] = name;
            }
        }

        return found
            .Select(kvp => (kvp.Key, kvp.Value))
            .ToList();
    }

    public static class GuildHelpers
    {
        public static IEnumerable<(string GuildName, string ApiRealm, string DisplayRealm)> LoadGuilds(
            string projectRoot,
            string fileName,
            string apiRealm,
            string displayRealm)
        {
            var path = Path.Combine(projectRoot, "Data", "Guilds", fileName);
            if (!File.Exists(path))
                yield break;

            foreach (var raw in File.ReadLines(path))
            {
                var line = raw?.Trim();
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                yield return (line, apiRealm, displayRealm);
            }
        }

        public static async Task LoadGuildMembersFromFilesAsync(
            string projectRoot,
            string apiUrl,
            string secret,
            HttpClient client,
            List<(string Name, string ApiRealm, string DisplayRealm)> allCharacters,
            CancellationToken ct,
            int maxConcurrency = 4,
            IDictionary<string, int>? classByCharacter = null)
        {
            var guilds = new List<(string GuildName, string ApiRealm, string DisplayRealm)>();

            guilds.AddRange(LoadGuilds(projectRoot, "evermoon-guilds.txt", "[EN] Evermoon", "Evermoon"));
            guilds.AddRange(LoadGuilds(projectRoot, "tauri-guilds.txt", "[HU] Tauri WoW Server", "Tauri"));
            guilds.AddRange(LoadGuilds(projectRoot, "wod-guilds.txt", "[HU] Warriors of Darkness", "WoD"));

            var distinctGuilds = guilds
                .DistinctBy(g => (g.GuildName.ToLowerInvariant(), g.ApiRealm))
                .ToList();

            using var throttler = new SemaphoreSlim(maxConcurrency);
            var done = 0;

            var tasks = distinctGuilds.Select(async g =>
            {
                await throttler.WaitAsync(ct);
                try
                {
                    await CharacterHelpers.LoadGuildMembersLevel90Async(
                        g.GuildName,
                        g.ApiRealm,
                        g.DisplayRealm,
                        apiUrl,
                        secret,
                        allCharacters,
                        client,
                        classByCharacter);
                }
                finally
                {
                    var current = Interlocked.Increment(ref done);
                    if (current % 25 == 0)
                        Console.WriteLine($"Loaded guilds {current}/{distinctGuilds.Count}");

                    throttler.Release();
                }
            });

            await Task.WhenAll(tasks);
        }
    }
}
