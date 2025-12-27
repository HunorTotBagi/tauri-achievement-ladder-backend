using AchievementLadder.Data;
using AchievementLadder.Models;

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
    }
}
