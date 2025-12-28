namespace AchievementLadder.Models;

public sealed record LadderEntryDto(
    string Name,
    string Realm,
    int AchievementPoints,
    string Guild,
    DateTime LastUpdated
);
