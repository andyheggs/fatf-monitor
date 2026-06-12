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
            <a>Algeria</a>, <a>Côte d'Ivoire</a>, <a>Virgin Islands (UK)</a>
            <h6>Topic</h6>
            <a>High-risk and other monitored jurisdictions</a>
            """;

        var parser = new FatfJurisdictionParser();

        var names = parser.ParseJurisdictionNames(html);

        Assert.Equal(["Algeria", "Côte d'Ivoire", "Virgin Islands (UK)"], names);
    }

    [Fact]
    public void ParseJurisdictionNames_FallsBackToMarkdownCitationText()
    {
        const string text = """
            ###### Country
            cite89†Democratic Republic of Korea
            cite90†Iran
            cite91†Myanmar
            ###### Topic
            """;

        var parser = new FatfJurisdictionParser();

        var names = parser.ParseJurisdictionNames(text);

        Assert.Equal(["Democratic Republic of Korea", "Iran", "Myanmar"], names);
    }
}
