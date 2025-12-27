using AchievementLadder.Models;

namespace AchievementLadder.Repositories
{
    public interface ILadderRepository
    {
        Task AddSnapshotAsync(IEnumerable<Player> players);
        Task UpsertPlayersAsync(IEnumerable<Player> players);
    }
}