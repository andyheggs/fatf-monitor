using Microsoft.Extensions.Options;

namespace FatfMonitor.Api.Compliance;

public sealed class FatfMonitorService(
    HttpClient httpClient,
    FatfJurisdictionParser parser,
    IFatfLlmVerifier llmVerifier,
    IFatfWebSearchProvider webSearchProvider,
    IFatfSnapshotStore snapshotStore,
    IOptions<FatfMonitorOptions> options)
{
    public async Task<FatfSnapshot> FetchCurrentAsync(CancellationToken cancellationToken = default)
    {
        var webSearchSnapshot = await webSearchProvider.TryFetchLatestAsync(cancellationToken);
        if (webSearchSnapshot is not null)
        {
            return webSearchSnapshot;
        }

        var sources = await ResolveSourcesAsync(cancellationToken);
        var jurisdictions = new List<FatfJurisdiction>();
        var excerpts = new Dictionary<FatfListCategory, string>();

        foreach (var source in sources)
        {
            var html = await FetchHtmlAsync(source.Url, new Uri(options.Value.HomePageUrl), cancellationToken);
            excerpts[source.Category] = parser.ExtractReviewExcerpt(html);
            jurisdictions.AddRange(parser
                .ParseJurisdictionNames(html)
                .Select(name => new FatfJurisdiction(name, source.Category, source.Url.ToString())));
        }

        var snapshot = new FatfSnapshot(
            DateTimeOffset.UtcNow,
            jurisdictions.OrderBy(jurisdiction => jurisdiction.Category).ThenBy(jurisdiction => jurisdiction.Name).ToArray(),
            sources,
            null);

        var review = await llmVerifier.ReviewAsync(snapshot, excerpts, cancellationToken);
        return snapshot with { LlmReview = review };
    }

    public async Task<FatfMonitorResult> CheckAndPersistAsync(CancellationToken cancellationToken = default)
    {
        var previous = await snapshotStore.ReadLatestAsync(cancellationToken);
        var current = await FetchCurrentAsync(cancellationToken);
        var changes = previous is null ? FatfChangeSet.Empty : Compare(previous, current);

        await snapshotStore.SaveLatestAsync(current, cancellationToken);

        return new FatfMonitorResult(current, previous, changes);
    }

    private async Task<IReadOnlyCollection<FatfSource>> ResolveSourcesAsync(CancellationToken cancellationToken)
    {
        var monitorOptions = options.Value;
        var homePageUri = new Uri(monitorOptions.HomePageUrl);
        var homePageHtml = await FetchHtmlAsync(homePageUri, null, cancellationToken);
        var discoveredLinks = parser.ExtractPublicationLinks(homePageHtml, homePageUri);

        var increasedMonitoring = PickDiscoveredSource(
            discoveredLinks,
            FatfListCategory.IncreasedMonitoring,
            monitorOptions.IncreasedMonitoringUrl,
            "Jurisdictions under Increased Monitoring");
        var callForAction = PickDiscoveredSource(
            discoveredLinks,
            FatfListCategory.CallForAction,
            monitorOptions.CallForActionUrl,
            "High-Risk Jurisdictions subject to a Call for Action");

        return [increasedMonitoring, callForAction];
    }

    private static FatfSource PickDiscoveredSource(
        IReadOnlyCollection<FatfPublicationLink> discoveredLinks,
        FatfListCategory category,
        string? fallbackUrl,
        string fallbackName)
    {
        var discovered = discoveredLinks.FirstOrDefault(link => link.Category == category);
        if (discovered is not null)
        {
            return new FatfSource(category, discovered.Title, discovered.Url);
        }

        if (!string.IsNullOrWhiteSpace(fallbackUrl))
        {
            return new FatfSource(category, fallbackName, new Uri(fallbackUrl));
        }

        throw new InvalidOperationException($"Could not discover FATF source link for {fallbackName} from the FATF homepage.");
    }

    private async Task<string> FetchHtmlAsync(Uri url, Uri? referer, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (referer is not null)
        {
            request.Headers.Referrer = referer;
        }

        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private static FatfChangeSet Compare(FatfSnapshot previous, FatfSnapshot current)
    {
        var previousKeys = previous.Jurisdictions
            .Select(ToKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var currentKeys = current.Jurisdictions
            .Select(ToKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var added = current.Jurisdictions
            .Where(jurisdiction => !previousKeys.Contains(ToKey(jurisdiction)))
            .ToArray();
        var removed = previous.Jurisdictions
            .Where(jurisdiction => !currentKeys.Contains(ToKey(jurisdiction)))
            .ToArray();

        return new FatfChangeSet(added, removed, added.Length > 0 || removed.Length > 0);
    }

    private static string ToKey(FatfJurisdiction jurisdiction) =>
        $"{jurisdiction.Category}:{jurisdiction.Name}";
}
