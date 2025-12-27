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
            try
            {
                await ladderService.ImportCharactersFromFileAsync();
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
