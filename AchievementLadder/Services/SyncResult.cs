namespace AchievementLadder.Services;

public sealed record SyncResult(
    int PlayerCount,
    int RetryCharacterCount,
    string PlayersCsvPath,
    string RareAchievementsPath,
    string LastUpdatedPath,
    string RetryOutputPath);
