using FatfMonitor.Api.Compliance;
using Xunit;

namespace FatfMonitor.Api.Tests;

public sealed class FatfJurisdictionListResponseTests
{
    [Fact]
    public void FromSnapshot_GroupsJurisdictionsByFatfList()
    {
        var snapshot = new FatfSnapshot(
            DateTimeOffset.Parse("2026-06-12T12:00:00Z"),
            [
                new FatfJurisdiction("Myanmar", FatfListCategory.CallForAction, "https://example.test/call"),
                new FatfJurisdiction("Algeria", FatfListCategory.IncreasedMonitoring, "https://example.test/increased"),
                new FatfJurisdiction("Iran", FatfListCategory.CallForAction, "https://example.test/call"),
                new FatfJurisdiction("Angola", FatfListCategory.IncreasedMonitoring, "https://example.test/increased")
            ],
            [
                new FatfSource(
                    FatfListCategory.IncreasedMonitoring,
                    "Jurisdictions under Increased Monitoring - June 2026",
                    new Uri("https://example.test/increased"),
                    new DateOnly(2026, 6, 19)),
                new FatfSource(
                    FatfListCategory.CallForAction,
                    "High-Risk Jurisdictions subject to a Call for Action - June 2026",
                    new Uri("https://example.test/call"),
                    new DateOnly(2026, 6, 19))
            ],
            null);

        var response = FatfJurisdictionListResponse.FromSnapshot(snapshot);

        Assert.Equal(4, response.TotalJurisdictions);
        Assert.Equal(2, response.IncreasedMonitoring.Count);
        Assert.Equal(["Algeria", "Angola"], response.IncreasedMonitoring.Jurisdictions);
        Assert.Equal("https://example.test/increased", response.IncreasedMonitoring.SourceUrl);
        Assert.Equal(new DateOnly(2026, 6, 19), response.IncreasedMonitoring.PublicationDate);
        Assert.Equal(2, response.CallForAction.Count);
        Assert.Equal(["Iran", "Myanmar"], response.CallForAction.Jurisdictions);
        Assert.Equal("https://example.test/call", response.CallForAction.SourceUrl);
        Assert.Equal(new DateOnly(2026, 6, 19), response.CallForAction.PublicationDate);
    }
}
