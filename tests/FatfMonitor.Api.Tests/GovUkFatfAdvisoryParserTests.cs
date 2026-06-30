using FatfMonitor.Api.Compliance;
using Xunit;

namespace FatfMonitor.Api.Tests;

public sealed class GovUkFatfAdvisoryParserTests
{
    [Fact]
    public void TryParse_ReturnsCurrentListsFromStructuredAdvisory()
    {
        const string html = """
            <script type="application/ld+json">
            {
              "mainEntity": [{
                "name": "FATF public statement",
                "acceptedAnswer": {
                  "text": "<p>On 19 June 2026, the FATF published the most recent update.</p><h4 id=\"jurisdictions-under-increased-monitoring\">Jurisdictions under Increased Monitoring</h4><ul><li>Angola</li><li>Bolivia</li><li>Bosnia and Herzegovina</li><li>British Virgin Islands</li><li>Bulgaria</li><li>Cameroon</li><li>Côte d’Ivoire</li><li>Haiti</li><li>Iraq</li><li>Kenya</li></ul><h4 id=\"high-risk-jurisdictions-subject-to-a-call-for-action\">High-Risk Jurisdictions subject to a Call for Action</h4><ul><li>Democratic People’s Republic of Korea</li><li>Iran</li><li>Myanmar</li></ul>"
                }
              }]
            }
            </script>
            """;
        var parser = new GovUkFatfAdvisoryParser();
        var sourceUrl = new Uri("https://www.gov.uk/example");

        var result = parser.TryParse(
            html,
            sourceUrl,
            new DateOnly(2026, 6, 30),
            new DateOnly(2026, 6, 1));

        Assert.NotNull(result);
        Assert.All(result.Sources, source => Assert.Equal(new DateOnly(2026, 6, 19), source.PublicationDate));
        Assert.Contains(result.Jurisdictions, item => item.Name == "Bosnia and Herzegovina");
        Assert.Contains(result.Jurisdictions, item => item.Name == "Democratic People's Republic of Korea");
        Assert.Equal(3, result.Jurisdictions.Count(item => item.Category == FatfListCategory.CallForAction));
    }

    [Fact]
    public void TryParse_RejectsStaleAdvisory()
    {
        const string html = """
            <script type="application/ld+json">
            {
              "mainEntity": [{
                "name": "FATF public statement",
                "acceptedAnswer": {
                  "text": "<p>On 13 February 2026, the FATF published the most recent update.</p>"
                }
              }]
            }
            </script>
            """;

        var result = new GovUkFatfAdvisoryParser().TryParse(
            html,
            new Uri("https://www.gov.uk/example"),
            new DateOnly(2026, 6, 30),
            new DateOnly(2026, 6, 1));

        Assert.Null(result);
    }
}
