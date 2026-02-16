namespace AchievementLadder.Dtos;

public sealed record ItemScanResultDto(
    string Name,
    string Realm,
    int ClassId,
    string ClassName,
    IReadOnlyList<ItemScanMatchDto> Items
);

public sealed record ItemScanMatchDto(
    int ItemId,
    string Name
);
