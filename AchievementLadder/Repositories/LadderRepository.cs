using AchievementLadder.Data;
using AchievementLadder.Models;
using Microsoft.EntityFrameworkCore;

namespace AchievementLadder.Repositories
{
    public class LadderRepository : ILadderRepository
    {
        private readonly AchievementContext _db;

        public LadderRepository(AchievementContext db)
        {
            _db = db;
        }

        public async Task AddSnapshotAsync(IEnumerable<Player> players)
        {
            await _db.Players.AddRangeAsync(players);
            await _db.SaveChangesAsync();
        }

        public async Task<List<Player>> GetLatestLadderAsync(int limit = 100)
        {
            var latestDate = await _db.Players
                .MaxAsync(p => (DateTime?)p.SnapshotDate) ?? DateTime.Today;

            return await _db.Players
                .Where(p => p.SnapshotDate == latestDate)
                .OrderByDescending(p => p.Points)
                .Take(limit)
                .ToListAsync();
        }
    }
}
