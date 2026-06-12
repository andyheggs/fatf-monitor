using FatfMonitor.Api.Compliance;
using Xunit;

namespace FatfMonitor.Api.Tests;

public sealed class FatfMonitorUnavailableExceptionTests
{
    [Fact]
    public void Constructor_CapturesBlockedSourceUrl()
    {
        var sourceUrl = new Uri("https://www.fatf-gafi.org/");

        var exception = new FatfMonitorUnavailableException("FATF returned 403.", sourceUrl);

        Assert.Equal(sourceUrl, exception.SourceUrl);
        Assert.Contains("403", exception.Message);
    }
}
