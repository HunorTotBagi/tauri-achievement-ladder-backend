using AchievementLadder.Models;
using AchievementLadder.Repositories;

namespace AchievementLadder.Services
{
    public class LadderService : ILadderService
    {
        private readonly ILadderRepository _repo;

        public LadderService(ILadderRepository repo)
        {
            _repo = repo;
        }

        public async Task SaveSnapshotAsync(Dictionary<(string Name, string Realm), int> results)
        {
            var today = DateTime.Today;
            var players = results.Select(kvp => new Player
            {
                Name = kvp.Key.Name,
                Realm = kvp.Key.Realm,
                Points = kvp.Value,
                SnapshotDate = today
            });

            await _repo.AddSnapshotAsync(players);
        }

        public async Task<List<Player>> GetLadderAsync(int limit = 100)
        {
            return await _repo.GetLatestLadderAsync(limit);
        }
    }
}
