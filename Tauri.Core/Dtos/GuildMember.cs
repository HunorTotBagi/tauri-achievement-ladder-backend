namespace Tauri.Core.Dtos;

public class GuildMember
{
    public string name { get; set; } = string.Empty;
    public int level { get; set; }
    public string realm { get; set; } = string.Empty;
    public int rank { get; set; }
    public string rank_name { get; set; } = string.Empty;
}
