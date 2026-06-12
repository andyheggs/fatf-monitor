using Microsoft.Extensions.Options;

namespace FatfMonitor.Api.Compliance;

public sealed class FatfMonitorService(
    HttpClient httpClient,
    FatfJurisdictionParser parser,
    IFatfLlmVerifier llmVerifier,
    IFatfSnapshotStore snapshotStore,
    IOptions<FatfMonitorOptions> options)
{
    public async Task<FatfSnapshot> FetchCurrentAsync(CancellationToken cancellationToken = default)
    {
        var sources = GetSources();
        var jurisdictions = new List<FatfJurisdiction>();
        var excerpts = new Dictionary<FatfListCategory, string>();

        foreach (var source in sources)
        {
            var html = await httpClient.GetStringAsync(source.Url, cancellationToken);
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

    private IReadOnlyCollection<FatfSource> GetSources()
    {
        var monitorOptions = options.Value;
        return
        [
            new(
                FatfListCategory.IncreasedMonitoring,
                "Jurisdictions under Increased Monitoring",
                new Uri(monitorOptions.IncreasedMonitoringUrl)),
            new(
                FatfListCategory.CallForAction,
                "High-Risk Jurisdictions subject to a Call for Action",
                new Uri(monitorOptions.CallForActionUrl))
        ];
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
