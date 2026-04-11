namespace AchievementLadder.Services;

public sealed record SyncResult(
    int PlayerCount,
    string PlayersCsvPath,
    string RareAchievementsPath,
    string LastUpdatedPath);
