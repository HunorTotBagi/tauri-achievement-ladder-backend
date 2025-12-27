namespace AchievementLadder.Models
{
    public class Player
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public int Race { get; set; }
        public int Gender { get; set; }
        public int Class { get; set; }
        public string Realm { get; set; }
        public string Guild { get; set; }
        public int AchievementPoints { get; set; }
        public int HonorableKills { get; set; }
        public DateTime LastUpdated { get; set; }
    }
}