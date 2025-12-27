using AchievementLadder.Models;

namespace AchievementLadder.Repositories
{
    public interface ILadderRepository
    {
        Task AddSnapshotAsync(IEnumerable<Player> players);
        Task<List<Player>> GetLatestLadderAsync(int limit = 100);
    }
}