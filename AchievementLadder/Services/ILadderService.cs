namespace AchievementLadder.Services
{
    public interface ILadderService
    {
        Task SaveSnapshotAsync(Dictionary<(string Name, string Realm), int> results);
    }
}