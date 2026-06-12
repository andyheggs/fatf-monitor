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

public sealed record FatfLlmReview(
    bool Enabled,
    string Provider,
    string Model,
    string Summary,
    decimal? Confidence);

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
