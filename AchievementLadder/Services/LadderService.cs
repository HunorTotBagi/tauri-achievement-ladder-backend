using System.Text;
using System.Text.Json;
using AchievementLadder.Models;
using AchievementLadder.Repositories;

namespace AchievementLadder.Services
{
    public class LadderService
    {
        private readonly ILadderRepository _ladderRepository;
        private readonly IWebHostEnvironment _env;

        public LadderService(
            ILadderRepository ladderRepository,
            IWebHostEnvironment env)
        {
            _ladderRepository = ladderRepository;
            _env = env;
        }

        public async Task SaveSnapshotAsync(
            Dictionary<(string Name, string Realm), int> results)
        {
            var today = DateTime.UtcNow;

            var players = results.Select(kvp => new Player
            {
                Name = kvp.Key.Name,
                AchievementPoints = kvp.Value,
                LastUpdated = today
            });

            await _ladderRepository.AddSnapshotAsync(players);
        }

        public async Task ImportCharactersFromFileAsync()
        {
            var projectRoot = _env.ContentRootPath;
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

            void LoadCharacters(string fileName, string apiRealm, string displayRealm)
            {
                var filePath = Path.Combine(
                    projectRoot,
                    "Data",
                    "CharacterCollection",
                    fileName);

                if (!File.Exists(filePath))
                    return;

                var content = File.ReadAllText(filePath);
                using var doc = JsonDocument.Parse(content);

                var array = doc.RootElement
                    .EnumerateObject()
                    .First()
                    .Value;

                foreach (var item in array.EnumerateArray())
                {
                    if (item.TryGetProperty("name", out var nameProp))
                    {
                        var name = nameProp.GetString();
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            allCharacters.Add((name, apiRealm, displayRealm));
                        }
                    }
                }
            }

            // Evermoon
            LoadCharacters("evermoon-achi.txt", "[EN] Evermoon", "Evermoon");
            LoadCharacters("evermoon-hk.txt", "[EN] Evermoon", "Evermoon");
            LoadCharacters("evermoon-playTime.txt", "[EN] Evermoon", "Evermoon");

            // Tauri
            LoadCharacters("tauri-achi.txt", "[HU] Tauri WoW Server", "Tauri");
            LoadCharacters("tauri-hk.txt", "[HU] Tauri WoW Server", "Tauri");
            LoadCharacters("tauri-playTime.txt", "[HU] Tauri WoW Server", "Tauri");

            // WoD
            LoadCharacters("wod-achi.txt", "[HU] Warriors of Darkness", "WoD");
            LoadCharacters("wod-hk.txt", "[HU] Warriors of Darkness", "WoD");
            LoadCharacters("wod-playTime.txt", "[HU] Warriors of Darkness", "WoD");

            var targetGuilds = new[]
            {
            new { GuildName = "Competence Optional", RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },  // 01
            new { GuildName = "Skill Issue",         RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },  // 02
            new { GuildName = "Despair",             RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },  // 03
            new { GuildName = "Mythic",              RealmApi = "[HU] Warriors of Darkness", RealmDisplay = "WoD" },       // 04
            new { GuildName = "Cara Máxima",         RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },  // 05
            new { GuildName = "Guild of Multiverse", RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },     // 06
            new { GuildName = "Defiance",            RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },  // 07
            new { GuildName = "Vistustan",           RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },  // 08
            new { GuildName = "Yin Yang",            RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },  // 09
            new { GuildName = "Shadow Hunters",      RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },  // 10
            new { GuildName = "Infernum",            RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },     // 11
            new { GuildName = "Thunder",             RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },  // 12
            new { GuildName = "Army of Divergent",   RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },  // 13
            new { GuildName = "Last Whisper",        RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },  // 14
            new { GuildName = "Punishers",           RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },  // 15
            new { GuildName = "Искатели легенд",     RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },  // 16 

            // Random guilds
            new { GuildName = "Wipes on Tresh",      RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "beloved",             RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "Outlaws",             RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "BOOSTED",             RealmApi = "[HU] Tauri WoW Server",     RealmDisplay = "Tauri" },
            new { GuildName = "Fekete Hold",         RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "Logic",               RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "Ace Of Spades",       RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "Ace Of Spadez",       RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "Solo Leveling",       RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "Last Try",            RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "Rycerze Ortalionu",   RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "Storm",               RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" },
            new { GuildName = "Endless",             RealmApi = "[EN] Evermoon",             RealmDisplay = "Evermoon" }
            };

            foreach (var g in targetGuilds)
            {
                await LoadGuildMembersLevel100Async(
                    g.GuildName,
                    g.RealmApi,
                    g.RealmDisplay,
                    apiUrl,
                    secret,
                    allCharacters,
                    client);
            }

            var distinctCharacters = allCharacters
                .Distinct()
                .ToList();

            var players = new List<Player>();
            var today = DateTime.UtcNow;

            foreach (var (name, apiRealm, displayRealm) in distinctCharacters)
            {
                var body = new
                {
                    secret,
                    url = "character-sheet",
                    @params = new { r = apiRealm, n = name }
                };

                var jsonBody = JsonSerializer.Serialize(body);
                var content = new StringContent(
                    jsonBody,
                    Encoding.UTF8,
                    "application/json");

                try
                {
                    var response = await client.PostAsync(apiUrl, content);
                    var responseString = await response.Content.ReadAsStringAsync();

                    int race = 0;
                    int gender = 0;
                    int @class = 0;
                    int pts = 0;
                    int honorableKills = 0;
                    string faction = string.Empty;
                    string guild = string.Empty;

                    if (!string.IsNullOrWhiteSpace(responseString))
                    {
                        using var doc = JsonDocument.Parse(responseString);
                        var root = doc.RootElement;

                        if (root.TryGetProperty("response", out var resp))
                        {
                            if (resp.TryGetProperty("race", out var v)) race = v.GetInt32();
                            if (resp.TryGetProperty("gender", out v)) gender = v.GetInt32();
                            if (resp.TryGetProperty("class", out v)) @class = v.GetInt32();
                            if (resp.TryGetProperty("pts", out v)) pts = v.GetInt32();
                            if (resp.TryGetProperty("playerHonorKills", out v)) honorableKills = v.GetInt32();
                            if (resp.TryGetProperty("faction_string_class", out v)) faction = v.GetString() ?? "";
                            if (resp.TryGetProperty("guildName", out v)) guild = v.GetString() ?? "";
                        }
                    }

                    players.Add(new Player
                    {
                        Name = name,
                        Race = race,
                        Gender = gender,
                        Class = @class,
                        Realm = displayRealm,
                        Guild = guild,
                        AchievementPoints = pts,
                        HonorableKills = honorableKills,
                        Faction = faction,
                        LastUpdated = today
                    });
                }
                catch
                {
                    // swallow individual character failures
                }
            }

            await _ladderRepository.UpsertPlayersAsync(players);
        }

        public async Task<IReadOnlyList<LadderEntryDto>> GetLadderAsync(
            string? realm,
            int page,
            int pageSize,
            CancellationToken ct = default
)
        {
            page = page < 1 ? 1 : page;
            pageSize = pageSize is < 1 or > 500 ? 100 : pageSize;

            var skip = (page - 1) * pageSize;

            var players = await _ladderRepository.GetPlayersSortedByAchievementPointsAsync(
                realm,
                take: pageSize,
                skip: skip,
                ct: ct
            );

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

        public async Task<IReadOnlyList<LadderEntryDto>> GetLadderByHonorableKillsAsync(
            string? realm,
            int page,
            int pageSize,
            CancellationToken ct = default)
        {
            page = page < 1 ? 1 : page;
            pageSize = pageSize is < 1 or > 500 ? 100 : pageSize;

            var skip = (page - 1) * pageSize;

            var players = await _ladderRepository.GetPlayersSortedByHonorableKillsAsync(
                realm,
                take: pageSize,
                skip: skip,
                ct: ct);

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

        private static async Task LoadGuildMembersLevel100Async(
            string guildName,
            string apiRealm,
            string displayRealm,
            string apiUrl,
            string secret,
            List<(string, string, string)> output,
            HttpClient client)
        {
            var body = new
            {
                secret,
                url = "guild-info",
                @params = new { r = apiRealm, gn = guildName }
            };

            var jsonBody = JsonSerializer.Serialize(body);
            var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

            try
            {
                var response = await client.PostAsync(apiUrl, content);
                var responseString = await response.Content.ReadAsStringAsync();

                var guildInfo = JsonSerializer.Deserialize<GuildInfoResponse>(responseString);
                if (guildInfo?.response?.guildList == null)
                    return;

                foreach (var member in guildInfo.response.guildList.Values)
                {
                    if (member.level == 100)
                        output.Add((member.name, apiRealm, displayRealm));
                }
            }
            catch { }
        }
    }

    public class GuildInfoResponse
    {
        public bool success { get; set; }
        public int errorcode { get; set; }
        public string errorstring { get; set; }
        public GuildInfoInner response { get; set; }
    }

    public class GuildInfoInner
    {
        public Dictionary<string, GuildMember> guildList { get; set; }
    }

    public class GuildMember
    {
        public string name { get; set; }
        public int level { get; set; }
        public string realm { get; set; }
    }
}
