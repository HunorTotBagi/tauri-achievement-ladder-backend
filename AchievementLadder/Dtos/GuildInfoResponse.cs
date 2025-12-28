using AchievementLadder.Helpers;

namespace AchievementLadder.Dtos;

public class GuildInfoResponse
{
    public bool success { get; set; }
    public int errorcode { get; set; }
    public string errorstring { get; set; }
    public GuildInfoInner response { get; set; }
}
