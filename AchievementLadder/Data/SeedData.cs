using AchievementLadder.Models;

namespace AchievementLadder.Data
{
    public static class SeedData
    {
        public static IEnumerable<Player> Sample()
        {
            return new List<Player>
            {
                new Player {
                    Name = "RandomPlayer1",
                    Race = 8,
                    Gender = 1,
                    Class = 5,
                    Realm = "Evermoon",
                    Guild = "beloved",
                    AchievementPoints = 10,
                    HonorableKills = 20,
                    Faction = "Horde",
                    LastUpdated = DateTime.UtcNow },

                new Player {
                    Name = "RandomPlayer2",
                    Race = 2,
                    Gender = 1,
                    Class = 3,
                    Realm = "Evermoon",
                    Guild = "Искатели легенд",
                    AchievementPoints = 20,
                    HonorableKills = 30,
                    Faction = "Alliance",
                    LastUpdated = DateTime.UtcNow },
            };
        }
    }
}
