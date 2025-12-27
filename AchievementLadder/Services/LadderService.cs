using AchievementLadder.Models;
using AchievementLadder.Repositories;

namespace AchievementLadder.Services
{
    public class LadderService(ILadderRepository ladderRepository) : ILadderService
    {
        public async Task SaveSnapshotAsync(Dictionary<(string Name, string Realm), int> results)
        {
            var today = DateTime.Today;
            var players = results.Select(kvp => new Player
            {
                Name = kvp.Key.Name,
                AchievementPoints = kvp.Value,
                LastUpdated = today
            });

            await ladderRepository.AddSnapshotAsync(players);
        }
    }
}
