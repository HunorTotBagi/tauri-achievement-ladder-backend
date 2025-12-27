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
    }
}
