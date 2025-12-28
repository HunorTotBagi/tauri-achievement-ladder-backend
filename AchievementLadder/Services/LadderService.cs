using System.Text.Json;
using AchievementLadder.Models;
using AchievementLadder.Repositories;

namespace AchievementLadder.Services
{
    public class LadderService
    {
        private readonly ILadderRepository _ladderRepository;
        private readonly IWebHostEnvironment _env;

        public LadderService(ILadderRepository ladderRepository, IWebHostEnvironment env)
        {
            _ladderRepository = ladderRepository;
            _env = env;
        }

        public async Task SaveSnapshotAsync(Dictionary<(string Name, string Realm), int> results)
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
                var filePath = Path.Combine(projectRoot, "Data", "CharacterCollection", fileName);
                if (!File.Exists(filePath))
                    return;

                var content = File.ReadAllText(filePath);
                using var doc = JsonDocument.Parse(content);
                var array = doc.RootElement.EnumerateObject().First().Value;

                foreach (var item in array.EnumerateArray())
                {
                    if (item.TryGetProperty("name", out var nameProp))
                    {
                        var name = nameProp.GetString() ?? string.Empty;
                        allCharacters.Add((name, apiRealm, displayRealm));
                    }
                }
            }

            LoadCharacters("evermoon-achi.txt", "[EN] Evermoon", "Evermoon");
            LoadCharacters("evermoon-hk.txt", "[EN] Evermoon", "Evermoon");
            LoadCharacters("evermoon-playTime.txt", "[EN] Evermoon", "Evermoon");

            LoadCharacters("tauri-achi.txt", "[HU] Tauri WoW Server", "Tauri");
            LoadCharacters("tauri-hk.txt", "[HU] Tauri WoW Server", "Tauri");
            LoadCharacters("tauri-playTime.txt", "[HU] Tauri WoW Server", "Tauri");

            LoadCharacters("wod-achi.txt", "[HU] Warriors of Darkness", "WoD");
            LoadCharacters("wod-hk.txt", "[HU] Warriors of Darkness", "WoD");
            LoadCharacters("wod-playTime.txt", "[HU] Warriors of Darkness", "WoD");

            var distinctCharacters = allCharacters.Distinct().ToList();

            using var client = new HttpClient();

            var players = new List<Player>();
            var today = DateTime.UtcNow;

            foreach (var (name, apiRealm, displayRealm) in distinctCharacters)
            {
                var body = new
                {
                    secret = secret,
                    url = "character-sheet",
                    @params = new
                    {
                        r = apiRealm,
                        n = name
                    }
                };

                var jsonBody = JsonSerializer.Serialize(body);
                var content = new StringContent(jsonBody, System.Text.Encoding.UTF8, "application/json");

                try
                {
                    var response = await client.PostAsync(apiUrl, content);
                    var responseString = await response.Content.ReadAsStringAsync();

                    int pts = 0;
                    string guild = string.Empty;

                    if (!string.IsNullOrWhiteSpace(responseString))
                    {
                        using var doc = JsonDocument.Parse(responseString);
                        var root = doc.RootElement;
                        if (root.TryGetProperty("response", out var resp) && resp.ValueKind == JsonValueKind.Object)
                        {
                            if (resp.TryGetProperty("pts", out var ptsProp) && ptsProp.ValueKind == JsonValueKind.Number)
                                pts = ptsProp.GetInt32();

                            if (resp.TryGetProperty("guild", out var guildProp) && guildProp.ValueKind == JsonValueKind.String)
                                guild = guildProp.GetString() ?? string.Empty;
                        }
                    }

                    players.Add(new Player
                    {
                        Name = name,
                        Realm = displayRealm,
                        AchievementPoints = pts,
                        Guild = guild ?? string.Empty,
                        Race = 0,
                        Gender = 0,
                        Class = 0,
                        HonorableKills = 0,
                        LastUpdated = today
                    });
                }
                catch
                {
                    // on any error skip this character
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
                p.Realm,
                p.AchievementPoints,
                p.Guild ?? string.Empty,
                p.LastUpdated
            )).ToList();
        }
    }
}
