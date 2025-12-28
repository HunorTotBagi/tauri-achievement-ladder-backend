using AchievementLadder.Services;
using Microsoft.AspNetCore.Mvc;

namespace AchievementLadder.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class LadderController(LadderService ladderService) : ControllerBase
    {

        [HttpPost("import/evermoon")]
        public async Task<IActionResult> ImportEvermoon()
        {
            await ladderService.ImportCharactersFromFileAsync();
            return Accepted();
        }

        [HttpGet("sorted/achievements")]
        public async Task<IActionResult> GetLadder([FromQuery] string? realm, [FromQuery] int page = 1, [FromQuery] int pageSize = 100, CancellationToken ct = default)
        {
            var result = await ladderService.GetLadderAsync(realm, page, pageSize, ct);
            return Ok(result);
        }
    }
}
