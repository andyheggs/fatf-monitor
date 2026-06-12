using FatfMonitor.Api.Compliance;
using Xunit;

namespace FatfMonitor.Api.Tests;

public sealed class FatfJurisdictionParserTests
{
    [Fact]
    public void ParseJurisdictionNames_ReadsPublicationCountryLinks()
    {
        const string html = """
            <h4>Publication details</h4>
            <h6>Language</h6>
            English
            <h6>Country</h6>
            <a>Algeria</a>, <a>Angola</a>, <a>Virgin Islands (UK)</a>
            <h6>Topic</h6>
            <a>High-risk and other monitored jurisdictions</a>
            """;

        var parser = new FatfJurisdictionParser();

        var names = parser.ParseJurisdictionNames(html);

        Assert.Equal(["Algeria", "Angola", "Virgin Islands (UK)"], names);
    }

    [Fact]
    public void ParseJurisdictionNames_FallsBackToMarkdownCitationText()
    {
        const string text =
            "###### Country\n" +
            "\ue000cite\ue00289\u2020Democratic Republic of Korea\ue201\n" +
            "\ue000cite\ue00290\u2020Iran\ue201\n" +
            "\ue000cite\ue00291\u2020Myanmar\ue201\n" +
            "###### Topic";

        var parser = new FatfJurisdictionParser();

        var names = parser.ParseJurisdictionNames(text);

        Assert.Equal(["Democratic Republic of Korea", "Iran", "Myanmar"], names);
    }

    [Fact]
    public void ExtractPublicationLinks_ReadsLatestJurisdictionLinksFromHomePage()
    {
        const string html = """
            <section>
              <h2>High-Risk and Other Monitored Jurisdictions</h2>
              <a href="/en/publications/High-risk-and-other-monitored-jurisdictions/increased-monitoring-june-2026.html">Jurisdictions under Increased Monitoring - 19 June 2026</a>
              <a href="/en/publications/High-risk-and-other-monitored-jurisdictions/Call-for-action-june-2026.html">High-Risk Jurisdictions subject to a Call for Action - 19 June 2026</a>
            </section>
            """;

        var parser = new FatfJurisdictionParser();

        var links = parser.ExtractPublicationLinks(html, new Uri("https://www.fatf-gafi.org/"));

        Assert.Contains(links, link =>
            link.Category == FatfListCategory.IncreasedMonitoring &&
            link.Url.ToString() == "https://www.fatf-gafi.org/en/publications/High-risk-and-other-monitored-jurisdictions/increased-monitoring-june-2026.html");
        Assert.Contains(links, link =>
            link.Category == FatfListCategory.CallForAction &&
            link.Url.ToString() == "https://www.fatf-gafi.org/en/publications/High-risk-and-other-monitored-jurisdictions/Call-for-action-june-2026.html");
    }
}
