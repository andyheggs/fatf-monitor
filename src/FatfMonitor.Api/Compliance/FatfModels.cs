namespace FatfMonitor.Api.Compliance;

public enum FatfListCategory
{
    IncreasedMonitoring,
    CallForAction
}

public sealed record FatfSource(
    FatfListCategory Category,
    string Name,
    Uri Url);

public sealed record FatfPublicationLink(
    FatfListCategory Category,
    string Title,
    Uri Url);

public sealed record FatfJurisdiction(
    string Name,
    FatfListCategory Category,
    string SourceUrl);

public sealed record FatfSnapshot(
    DateTimeOffset CheckedAt,
    IReadOnlyCollection<FatfJurisdiction> Jurisdictions,
    IReadOnlyCollection<FatfSource> Sources,
    FatfLlmReview? LlmReview);

public sealed record FatfChangeSet(
    IReadOnlyCollection<FatfJurisdiction> Added,
    IReadOnlyCollection<FatfJurisdiction> Removed,
    bool HasChanges)
{
    public static FatfChangeSet Empty { get; } = new([], [], false);
}

public sealed record FatfMonitorResult(
    FatfSnapshot Current,
    FatfSnapshot? Previous,
    FatfChangeSet Changes);

public sealed record FatfJurisdictionListResponse(
    DateTimeOffset CheckedAt,
    FatfJurisdictionList IncreasedMonitoring,
    FatfJurisdictionList CallForAction,
    FatfLlmReview? LlmReview)
{
    public int TotalJurisdictions => IncreasedMonitoring.Count + CallForAction.Count;

    public static FatfJurisdictionListResponse FromSnapshot(FatfSnapshot snapshot)
    {
        return new FatfJurisdictionListResponse(
            snapshot.CheckedAt,
            FatfJurisdictionList.FromSnapshot(snapshot, FatfListCategory.IncreasedMonitoring, "Jurisdictions under Increased Monitoring"),
            FatfJurisdictionList.FromSnapshot(snapshot, FatfListCategory.CallForAction, "High-Risk Jurisdictions subject to a Call for Action"),
            snapshot.LlmReview);
    }
}

public sealed record FatfJurisdictionList(
    FatfListCategory Category,
    string Name,
    string? SourceUrl,
    IReadOnlyCollection<string> Jurisdictions)
{
    public int Count => Jurisdictions.Count;

    public static FatfJurisdictionList FromSnapshot(FatfSnapshot snapshot, FatfListCategory category, string fallbackName)
    {
        var source = snapshot.Sources.FirstOrDefault(item => item.Category == category);
        var jurisdictions = snapshot.Jurisdictions
            .Where(jurisdiction => jurisdiction.Category == category)
            .Select(jurisdiction => jurisdiction.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new FatfJurisdictionList(
            category,
            source?.Name ?? fallbackName,
            source?.Url.ToString(),
            jurisdictions);
    }
}

public sealed record FatfLlmReview(
    bool Enabled,
    string Provider,
    string Model,
    string Summary,
    decimal? Confidence);

public sealed class FatfMonitorUnavailableException : Exception
{
    public FatfMonitorUnavailableException(string message, Uri sourceUrl, Exception? innerException = null)
        : base(message, innerException)
    {
        SourceUrl = sourceUrl;
    }

    public Uri SourceUrl { get; }
}

public sealed class FatfMonitorOptions
{
    public string HomePageUrl { get; set; } = "https://www.fatf-gafi.org/";

    public string? IncreasedMonitoringUrl { get; set; }

    public string? CallForActionUrl { get; set; }

    public string SnapshotPath { get; set; } = Path.Combine("data", "fatf-latest.json");

    public string? CheckToken { get; set; }
}

public sealed class LlmOptions
{
    public string Provider { get; set; } = "OpenAI";
    public string Model { get; set; } = "gpt-4.1-mini";
    public string SearchModel { get; set; } = "gpt-4.1-mini";
    public string? ApiKey { get; set; }
}
