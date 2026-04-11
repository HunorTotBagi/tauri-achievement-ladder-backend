namespace AchievementLadder.Models;

public sealed record RareAchievementExport(
    DateTimeOffset GeneratedAt,
    IReadOnlyList<RareAchievementDefinition> Achievements,
    IReadOnlyList<CharacterRareAchievementEntry> Characters);

public sealed record RareAchievementDefinition(
    int Id,
    string Name);

public sealed record CharacterRareAchievementEntry(
    string Name,
    string Realm,
    IReadOnlyList<int> AchievementIds);
