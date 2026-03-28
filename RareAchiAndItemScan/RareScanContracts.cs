namespace RareAchiAndItemScan;

public sealed record RareScanRunResult(
    int ScannedCharacterCount,
    int MatchedCharacterCount,
    int FailedCharacterCount,
    int AchievementMatchCount,
    int ItemMatchCount,
    int MountMatchCount,
    string OutputPath);

public sealed record RareScanReport(
    DateTimeOffset GeneratedAtUtc,
    string Scope,
    string Targets,
    int ScannedCharacterCount,
    int FailedCharacterCount,
    IReadOnlyList<string> Failures,
    IReadOnlyList<RareCharacterScanResult> Characters);

public sealed record RareCharacterScanResult(
    string Name,
    string Realm,
    int ClassId,
    string ClassName,
    IReadOnlyList<RareAchievementMatch> Achievements,
    IReadOnlyList<RareItemMatch> Items,
    IReadOnlyList<RareMountMatch> Mounts)
{
    public int TotalMatchCount => Achievements.Count + Items.Count + Mounts.Count;
}

public sealed record RareAchievementMatch(int AchievementId, string Name);

public sealed record RareItemMatch(int ItemId, string Name);

public sealed record RareMountMatch(int SpellId, string Name);
