using AchievementLadder.Services;
using Microsoft.AspNetCore.Mvc;

namespace AchievementLadder.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class LadderController : ControllerBase
    {
        private readonly LadderService _ladderService;

        public LadderController(LadderService ladderService)
        {
            _ladderService = ladderService;
        }

        [HttpPost("import/evermoon")]
        public async Task<IActionResult> ImportEvermoon()
        {
            await _ladderService.ImportCharactersFromFileAsync();
            return Accepted();
        }

        [HttpGet("sorted/achievements")]
        public async Task<IActionResult> GetLadder([FromQuery] string? realm, [FromQuery] int page = 1, [FromQuery] int pageSize = 100, CancellationToken ct = default)
        {
            var result = await _ladderService.GetLadderAsync(realm, page, pageSize, ct);
            return Ok(result);
        }

        [HttpGet("sorted/honorableKills")]
        public async Task<IActionResult> GetByHonorableKills([FromQuery] string? realm, [FromQuery] int page = 1, [FromQuery] int pageSize = 100, CancellationToken ct = default)
        {
            var result = await _ladderService.GetLadderByHonorableKillsAsync(realm, page, pageSize, ct);
            return Ok(result);
        }
    }
}
