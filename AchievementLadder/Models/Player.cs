namespace AchievementLadder.Models
{
    public class Player
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int Race { get; set; }
        public int Gender { get; set; }
        public int Class { get; set; }
        public string Realm { get; set; } = string.Empty;
        public string Guild { get; set; } = string.Empty;
        public int AchievementPoints { get; set; }
        public int HonorableKills { get; set; }
        public string Faction { get; set; } = string.Empty;
    }
}
