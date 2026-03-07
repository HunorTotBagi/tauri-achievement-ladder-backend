namespace AchievementLadder.Dtos;

public class GuildInfoResponse
{
    public bool success { get; set; }
    public int errorcode { get; set; }
    public string errorstring { get; set; } = string.Empty;
    public GuildInfoInner response { get; set; } = new();
}
