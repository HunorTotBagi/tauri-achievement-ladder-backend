using AchievementLadder.Models;

namespace AchievementLadder.Repositories
{
    public interface ILadderRepository
    {
        Task AddSnapshotAsync(IEnumerable<Player> players);
        Task UpsertPlayersAsync(IEnumerable<Player> players);
        Task<IReadOnlyList<Player>> GetPlayersSortedByAchievementPointsAsync(string? realm, int take, int skip, CancellationToken ct = default);
        Task<IReadOnlyList<Player>> GetPlayersSortedByHonorableKillsAsync(string? realm, int take, int skip, CancellationToken ct = default);
    }
}