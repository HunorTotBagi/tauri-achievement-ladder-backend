using AchievementLadder.Models;

namespace AchievementLadder.Repositories;

public interface IPlayerRepository
{
    Task UpsertPlayersAsync(IEnumerable<Player> players);
    Task<IReadOnlyList<Player>> GetSortedByAchievements(int take, int skip, CancellationToken cancellationToken);
    Task<IReadOnlyList<Player>> GetSortedByHonorableKills(int take, int skip, CancellationToken cancellationToken);
}