using Tauri.Core.Infrastructure;

namespace Tauri.Core.Tests;

public sealed class CharacterAgeCalculatorTests
{
    [Fact]
    public void Format_MissingAchievementDate_ReturnsEmptyString()
    {
        var result = CharacterAgeCalculator.Format(null, DateTimeOffset.UtcNow);

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void Format_FutureAchievementDate_ReturnsZeroAge()
    {
        var result = CharacterAgeCalculator.Format(
            new DateTimeOffset(2026, 8, 12, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 11, 0, 0, 0, TimeSpan.Zero)
        );

        Assert.Equal("0 years 0 months 0 days", result);
    }

    [Fact]
    public void Format_CalendarDifference_BorrowsDaysAndMonths()
    {
        var result = CharacterAgeCalculator.Format(
            new DateTimeOffset(2020, 10, 20, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2022, 2, 15, 0, 0, 0, TimeSpan.Zero)
        );

        Assert.Equal("1 years 3 months 26 days", result);
    }

    [Fact]
    public void Format_TimestampsWithOffsets_UsesUtcCalendarDates()
    {
        var result = CharacterAgeCalculator.Format(
            new DateTimeOffset(2020, 1, 2, 1, 0, 0, TimeSpan.FromHours(2)),
            new DateTimeOffset(2021, 1, 1, 23, 0, 0, TimeSpan.Zero)
        );

        Assert.Equal("1 years 0 months 0 days", result);
    }
}
