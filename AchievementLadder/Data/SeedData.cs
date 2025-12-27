using AchievementLadder.Models;

namespace AchievementLadder.Data
{
    public static class SeedData
    {
        public static IEnumerable<Player> Sample()
        {
            return new[]
            {
                new Player { Name = "Test1", Realm = "Evermoon", Points = 100, SnapshotDate = DateTime.Today },
                new Player { Name = "Test2", Realm = "Tauri", Points = 80, SnapshotDate = DateTime.Today }
            };
        }
    }
}
