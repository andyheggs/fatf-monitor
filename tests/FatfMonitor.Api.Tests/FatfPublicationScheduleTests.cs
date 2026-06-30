using FatfMonitor.Api.Compliance;
using Xunit;

namespace FatfMonitor.Api.Tests;

public sealed class FatfPublicationScheduleTests
{
    [Theory]
    [InlineData(2026, 1, 15, 2025, 10, 1)]
    [InlineData(2026, 2, 27, 2025, 10, 1)]
    [InlineData(2026, 2, 28, 2026, 2, 1)]
    [InlineData(2026, 6, 27, 2026, 2, 1)]
    [InlineData(2026, 6, 28, 2026, 6, 1)]
    [InlineData(2026, 10, 27, 2026, 6, 1)]
    [InlineData(2026, 10, 28, 2026, 10, 1)]
    public void MinimumExpectedPublicationDate_UsesLatestCompletedCycle(
        int year,
        int month,
        int day,
        int expectedYear,
        int expectedMonth,
        int expectedDay)
    {
        var result = FatfPublicationSchedule.MinimumExpectedPublicationDate(new DateOnly(year, month, day));

        Assert.Equal(new DateOnly(expectedYear, expectedMonth, expectedDay), result);
    }
}
