namespace AchievementLadder.Models
{
    public class Player
    {
        public int Id { get; set; }
        public string Name { get; set; } = null!;
        public string Realm { get; set; } = null!;
        public int Points { get; set; }
        public DateTime SnapshotDate { get; set; }
    }
}