using AchievementLadder.Models;

namespace AchievementLadder.Services
{
    public interface ILadderService
    {
        Task SaveSnapshotAsync(Dictionary<(string Name, string Realm), int> results);
        Task<List<Player>> GetLadderAsync(int limit = 100);
    }
}