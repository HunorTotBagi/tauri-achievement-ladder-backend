namespace AchievementLadder.Dtos;

public class GuildInfoInner
{
    public Dictionary<string, GuildMember> guildList { get; set; } = new();
}
