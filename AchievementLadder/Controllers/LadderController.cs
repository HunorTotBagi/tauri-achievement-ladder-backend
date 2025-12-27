using AchievementLadder.Services;
using Microsoft.AspNetCore.Mvc;

namespace AchievementLadder.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class LadderController(ILadderService ladderService) : ControllerBase
    {
        [HttpPost("import/evermoon")]
        public async Task<IActionResult> ImportEvermoon()
        {
            var baseDir = AppContext.BaseDirectory;
            var filePath = Path.Combine(baseDir, "..", "..", "..", "Data", "CharacterCollection", "evermoon-achi.txt");

            try
            {
                await ladderService.ImportCharactersFromFileAsync(filePath, "[EN] Evermoon", "Evermoon");
                return Accepted();
            }
            catch (FileNotFoundException)
            {
                return NotFound("evermoon-achi.txt not found");
            }
            catch (Exception ex)
            {
                return Problem(ex.Message);
            }
        }
    }
}
