namespace AchievementLadder.Models;

public sealed record LadderEntryDto(
    string Name,
    int Race,
    int Gender,
    int Class,
    string Realm,
    string Guild,
    int AchievementPoints,
    int HonorableKills,
    string Faction
);
