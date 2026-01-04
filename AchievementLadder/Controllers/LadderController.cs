using AchievementLadder.Services;
using Microsoft.AspNetCore.Mvc;

namespace AchievementLadder.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class LadderController(IPlayerService playerService) : ControllerBase
    {
        [HttpPost("syncData")]
        public async Task<IActionResult> SyncData(CancellationToken cancellationToken = default)
        {
            await playerService.SyncData(cancellationToken);
            return Accepted();
        }

        [HttpGet("sorted/achievements")]
        public async Task<IActionResult> GetSortedByAchievements([FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 500, [FromQuery] string? realm = null, [FromQuery] string? faction = null, [FromQuery] int? playerClass = null, CancellationToken cancellationToken = default)
        {
            var result = await playerService.GetSortedByAchievements(pageNumber, pageSize, realm, faction, playerClass, cancellationToken);
            return Ok(result);
        }

        [HttpGet("sorted/honorableKills")]
        public async Task<IActionResult> GetSortedByHonorableKills([FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 500, [FromQuery] string? realm = null, [FromQuery] string? faction = null, [FromQuery] int? playerClass = null, CancellationToken cancellationToken = default)
        {
            var result = await playerService.GetSortedByHonorableKills(pageNumber, pageSize, realm, faction, playerClass, cancellationToken);
            return Ok(result);
        }
    }
}
