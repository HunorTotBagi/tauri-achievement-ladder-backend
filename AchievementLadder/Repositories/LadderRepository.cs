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

        public async Task UpsertPlayersAsync(IEnumerable<Player> players)
        {
            foreach (var p in players)
            {
                var existing = await _db.Players
                    .FirstOrDefaultAsync(x => x.Name == p.Name && x.Realm == p.Realm);

                if (existing == null)
                {
                    await _db.Players.AddAsync(p);
                }
                else
                {
                    existing.AchievementPoints = p.AchievementPoints;
                    existing.Guild = p.Guild;
                    existing.HonorableKills = p.HonorableKills;
                    existing.LastUpdated = p.LastUpdated;
                    existing.Race = p.Race;
                    existing.Gender = p.Gender;
                    existing.Class = p.Class;
                }
            }

            await _db.SaveChangesAsync();
        }

        public async Task<IReadOnlyList<Player>> GetPlayersSortedByAchievementPointsAsync(
            string? realm,
            int take,
            int skip,
            CancellationToken ct = default
)
        {
            var query = _db.Players.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(realm))
                query = query.Where(p => p.Realm == realm);

            return await query
                .OrderByDescending(p => p.AchievementPoints)
                .ThenBy(p => p.Name)
                .Skip(skip)
                .Take(take)
                .ToListAsync(ct);
        }
    }
}
