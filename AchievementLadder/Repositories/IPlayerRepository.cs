using AchievementLadder.Models;

namespace AchievementLadder.Repositories;

public interface IPlayerRepository
{
    Task UpsertPlayersAsync(IEnumerable<Player> players);
    Task<IReadOnlyList<Player>> GetSortedByAchievements(int take, int skip, string? realm = null, string? faction = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Player>> GetSortedByHonorableKills(int take, int skip, string? realm = null, string? faction = null, CancellationToken cancellationToken = default);
}
