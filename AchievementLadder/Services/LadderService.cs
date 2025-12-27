using AchievementLadder.Models;
using AchievementLadder.Repositories;

namespace AchievementLadder.Services
{
    public class LadderService : ILadderService
    {
        private readonly ILadderRepository _ladderRepository;

        public LadderService(ILadderRepository ladderRepository)
        {
            _ladderRepository = ladderRepository;
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

        public async Task ImportCharactersFromFileAsync(string filePath, string apiRealm, string displayRealm)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException(filePath);

            var content = await File.ReadAllTextAsync(filePath);
            using var doc = System.Text.Json.JsonDocument.Parse(content);
            var root = doc.RootElement.EnumerateObject().First().Value;

            var today = DateTime.UtcNow;
            var players = new List<Player>();

            foreach (var item in root.EnumerateArray())
            {
                var name = item.GetProperty("name").GetString();
                var guild = item.TryGetProperty("guildName", out var g) ? g.GetString() ?? string.Empty : string.Empty;
                var points = item.TryGetProperty("points", out var p) && int.TryParse(p.GetString(), out var pts) ? pts : 0;
                var race = item.TryGetProperty("race", out var r) && int.TryParse(r.GetString(), out var rr) ? rr : 0;
                var gender = item.TryGetProperty("gender", out var ge) && int.TryParse(ge.GetString(), out var gg) ? gg : 0;
                var @class = item.TryGetProperty("class", out var c) && int.TryParse(c.GetString(), out var cc) ? cc : 0;
                var hk = item.TryGetProperty("totalKills", out var hkElem) && int.TryParse(hkElem.GetString(), out var hkVal) ? hkVal : 0;

                players.Add(new Player
                {
                    Name = name,
                    Realm = displayRealm,
                    AchievementPoints = points,
                    Guild = guild,
                    Race = race,
                    Gender = gender,
                    Class = @class,
                    HonorableKills = hk,
                    LastUpdated = today
                });
            }

            await _ladderRepository.UpsertPlayersAsync(players);
        }
    }
}
