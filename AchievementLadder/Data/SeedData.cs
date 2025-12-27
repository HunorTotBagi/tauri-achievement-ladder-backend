using AchievementLadder.Models;

namespace AchievementLadder.Data
{
    public static class SeedData
    {
        public static IEnumerable<Player> Sample()
        {
            return
            [
                new Player { 
                    Name = "Xyhop",
                    Race = 8,
                    Gender = 1,
                    Class = 5,
                    Realm = "Evermoon",
                    Guild = "beloved",
                    AchievementPoints = 18580,
                    HonorableKills = 57680,
                    LastUpdated = DateTime.Now },

                new Player {
                    Name = "Langston",
                    Race = 2,
                    Gender = 1,
                    Class = 3,
                    Realm = "Evermoon",
                    Guild = "Искатели легенд",
                    AchievementPoints = 18760,
                    HonorableKills = 48010,
                    LastUpdated = DateTime.Now },
            ];
        }
    }
}
