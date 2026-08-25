namespace AchievementLadder.Services;

public sealed record SyncResult(
    int PlayerCount,
    int RetryCharacterCount,
    string PlayersCsvPath,
    string RareAchievementsPath,
    string RareItemsPath,
    string LastUpdatedPath,
    string RetryOutputPath
);
