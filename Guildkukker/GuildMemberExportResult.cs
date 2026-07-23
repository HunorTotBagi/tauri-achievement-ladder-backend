namespace Guildkukker;

public sealed record GuildMemberExportResult(
    int ScannedPlayerCount,
    int PlayerCount,
    int ReputationCount,
    int MissingReputationCount,
    string OutputPath
);
