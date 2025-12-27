using AchievementLadder.Models;
using AchievementLadder.Services;
using Microsoft.AspNetCore.Mvc;

namespace AchievementLadder.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class LadderController : ControllerBase
    {
        private readonly ILadderService _service;

        public LadderController(ILadderService service)
        {
            _service = service;
        }

        [HttpPost("snapshot")]
        public async Task<IActionResult> Snapshot([FromBody] Dictionary<string, int> payload)
        {
            var mapped = payload.Select(kvp =>
            {
                var parts = kvp.Key.Split('-', 2);
                var name = parts.Length > 0 ? parts[0] : string.Empty;
                var realm = parts.Length > 1 ? parts[1] : string.Empty;
                return (Name: name, Realm: realm, Points: kvp.Value);
            }).ToDictionary(x => (x.Name, x.Realm), x => x.Points);

            await _service.SaveSnapshotAsync(mapped);
            return Accepted();
        }
    }
}
