using AchievementLadder.Data;
using AchievementLadder.Models;
using Microsoft.EntityFrameworkCore;

namespace AchievementLadder.Repositories
{
    public class PlayerRepository : IPlayerRepository
    {
        private readonly AchievementContext _db;

        public PlayerRepository(AchievementContext db)
        {
            _db = db;
        }

        public async Task UpsertPlayersAsync(IEnumerable<Player> players)
        {
            foreach (var p in players)
            {
                var existing = await _db.Players.FirstOrDefaultAsync(x => x.Name == p.Name && x.Realm == p.Realm);

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
                    existing.Faction = p.Faction;
                }
            }

            await _db.SaveChangesAsync();
        }

        public async Task<IReadOnlyList<Player>> GetSortedByAchievements(int take, int skip, string? realm = null, string? faction = null, CancellationToken cancellationToken = default)
        {
            var query = _db.Players.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(realm))
                query = query.Where(p => p.Realm == realm);

            if (!string.IsNullOrWhiteSpace(faction))
                query = query.Where(p => p.Faction == faction);

            return await query
                .OrderByDescending(p => p.AchievementPoints)
                .Skip(skip)
                .Take(take)
                .ToListAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<Player>> GetSortedByHonorableKills(int take, int skip, string? realm = null, string? faction = null, CancellationToken cancellationToken = default)
        {
            var query = _db.Players.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(realm))
                query = query.Where(p => p.Realm == realm);

            if (!string.IsNullOrWhiteSpace(faction))
                query = query.Where(p => p.Faction == faction);

            return await query
                .OrderByDescending(p => p.HonorableKills)
                .Skip(skip)
                .Take(take)
                .ToListAsync(cancellationToken);
        }
    }
}
