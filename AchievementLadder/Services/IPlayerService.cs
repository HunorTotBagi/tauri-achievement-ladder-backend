using AchievementLadder.Dtos;

namespace AchievementLadder.Services;

public interface IPlayerService
{
    Task SyncData(CancellationToken cancellationToken);
    Task<IReadOnlyList<LadderEntryDto>> GetSortedByAchievements(int page, int pageSize, CancellationToken cancellationToken);
    Task<IReadOnlyList<LadderEntryDto>> GetSortedByHonorableKills(int page, int pageSize, CancellationToken cancellationToken);
}
