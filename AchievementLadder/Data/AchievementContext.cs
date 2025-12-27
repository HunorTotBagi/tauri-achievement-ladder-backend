using AchievementLadder.Models;
using Microsoft.EntityFrameworkCore;

namespace AchievementLadder.Data
{
    public class AchievementContext : DbContext
    {
        public AchievementContext(DbContextOptions<AchievementContext> options) : base(options)
        {
        }

        public DbSet<Player> Players { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Player>()
                .HasIndex(p => new { 
                    p.Name,
                    p.Race,
                    p.Gender,
                    p.Class,
                    p.Realm,
                    p.Guild,
                    p.AchievementPoints,
                    p.HonorableKills,
                    p.LastUpdated});
        }
    }
}
