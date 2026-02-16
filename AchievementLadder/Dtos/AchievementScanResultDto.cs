namespace AchievementLadder.Dtos;

public sealed record AchievementScanResultDto(
    string Name,
    string Realm,
    int ClassId,
    string ClassName,
    IReadOnlyList<AchievementScanMatchDto> Achievements
);

public sealed record AchievementScanMatchDto(
    int AchievementId,
    string Name
);
