using System.Net.Http.Headers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FatfMonitor.Api.Compliance;

public interface IFatfWebSearchProvider
{
    Task<FatfSnapshot?> TryFetchLatestAsync(CancellationToken cancellationToken);
}

public sealed class OpenAiFatfWebSearchProvider(
    HttpClient httpClient,
    IConfiguration configuration,
    GovUkFatfAdvisoryParser govUkParser,
    ILogger<OpenAiFatfWebSearchProvider> logger) : IFatfWebSearchProvider
{
    public async Task<FatfSnapshot?> TryFetchLatestAsync(CancellationToken cancellationToken)
    {
        var options = configuration.GetSection("Llm").Get<LlmOptions>() ?? new LlmOptions();
        var apiKey = options.ApiKey ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var minimumPublicationDate = FatfPublicationSchedule.MinimumExpectedPublicationDate(today);

        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            var openAiSnapshot = await TryFetchFromOpenAiAsync(
                options,
                apiKey,
                today,
                minimumPublicationDate,
                cancellationToken);
            if (openAiSnapshot is not null)
            {
                return openAiSnapshot;
            }
        }

        return await TryFetchFromGovUkAsync(
            options,
            today,
            minimumPublicationDate,
            cancellationToken);
    }

    private async Task<FatfSnapshot?> TryFetchFromOpenAiAsync(
        LlmOptions options,
        string apiKey,
        DateOnly today,
        DateOnly minimumPublicationDate,
        CancellationToken cancellationToken)
    {
        var model = string.IsNullOrWhiteSpace(options.SearchModel) ? options.Model : options.SearchModel;
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(new
        {
            model,
            tools = new[]
            {
                new
                {
                    type = "web_search",
                    search_context_size = "high",
                    filters = new
                    {
                        allowed_domains = new[] { "fatf-gafi.org", "gov.uk" }
                    }
                }
            },
            tool_choice = "required",
            input = BuildPrompt(today, minimumPublicationDate)
        }), Encoding.UTF8, "application/json");

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("OpenAI FATF search returned HTTP {StatusCode}.", (int)response.StatusCode);
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var responseText = ExtractResponseText(document.RootElement);
        if (string.IsNullOrWhiteSpace(responseText))
        {
            return null;
        }

        var snapshot = TryParseSnapshot(responseText, options.Provider, model, today, minimumPublicationDate);
        if (snapshot is null)
        {
            logger.LogWarning("OpenAI returned a FATF dataset that failed freshness or source validation.");
        }

        return snapshot;
    }

    private async Task<FatfSnapshot?> TryFetchFromGovUkAsync(
        LlmOptions options,
        DateOnly today,
        DateOnly minimumPublicationDate,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(options.GovUkAdvisoryUrl, UriKind.Absolute, out var advisoryUrl)
            || !string.Equals(advisoryUrl.Host, "www.gov.uk", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("The configured GOV.UK advisory URL is invalid.");
            return null;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, advisoryUrl);
            request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (compatible; FatfMonitor/1.0; +https://github.com/andyheggs/fatf-monitor)");
            request.Headers.Accept.ParseAdd("text/html,application/xhtml+xml");
            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("HM Treasury FATF advisory returned HTTP {StatusCode}.", (int)response.StatusCode);
                return null;
            }

            var html = await response.Content.ReadAsStringAsync(cancellationToken);
            var snapshot = govUkParser.TryParse(html, advisoryUrl, today, minimumPublicationDate);
            if (snapshot is null)
            {
                logger.LogWarning("HM Treasury FATF advisory failed freshness or list validation.");
            }

            return snapshot;
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "Unable to retrieve the HM Treasury FATF advisory.");
            return null;
        }
    }

    private static string BuildPrompt(DateOnly today, DateOnly minimumPublicationDate) => $$"""
        Today's date is {{today:yyyy-MM-dd}}.

        Search the web for the latest FATF publications on fatf-gafi.org for both:
        1. Jurisdictions under Increased Monitoring
        2. High-Risk Jurisdictions subject to a Call for Action

        FATF normally publishes these statements after its February, June, and October plenaries.
        The latest publication must be dated on or after {{minimumPublicationDate:yyyy-MM-dd}}.
        Do not return an older publication merely because it ranks higher in search results.

        First identify the newest plenary/update using official FATF pages. Cross-check recency
        against the latest official HM Treasury Money Laundering Advisory Notice on GOV.UK.
        The two FATF lists must come from the same plenary date. Return FATF publication URLs
        as sourceUrl values, not search-result or third-party URLs.

        Return JSON only, with exactly this shape:
        {
          "increasedMonitoring": {
            "publicationTitle": "...",
            "publicationDate": "YYYY-MM-DD or source date text",
            "sourceUrl": "https://...",
            "jurisdictions": ["..."]
          },
          "callForAction": {
            "publicationTitle": "...",
            "publicationDate": "YYYY-MM-DD or source date text",
            "sourceUrl": "https://...",
            "jurisdictions": ["..."]
          }
        }
        Do not include commentary or Markdown fences.
        """;

    private static FatfSnapshot? TryParseSnapshot(
        string responseText,
        string provider,
        string model,
        DateOnly today,
        DateOnly minimumPublicationDate)
    {
        var json = StripCodeFence(responseText);
        try
        {
            using var document = JsonDocument.Parse(json);
            var increased = document.RootElement.GetProperty("increasedMonitoring");
            var callForAction = document.RootElement.GetProperty("callForAction");
            var increasedDate = ToPublicationDate(increased);
            var callForActionDate = ToPublicationDate(callForAction);

            if (increasedDate != callForActionDate
                || increasedDate < minimumPublicationDate
                || increasedDate > today)
            {
                return null;
            }

            var increasedSource = ToSource(
                FatfListCategory.IncreasedMonitoring,
                increased,
                "Jurisdictions under Increased Monitoring",
                increasedDate);
            var callForActionSource = ToSource(
                FatfListCategory.CallForAction,
                callForAction,
                "High-Risk Jurisdictions subject to a Call for Action",
                callForActionDate);

            var jurisdictions = ToJurisdictions(increased, increasedSource)
                .Concat(ToJurisdictions(callForAction, callForActionSource))
                .OrderBy(jurisdiction => jurisdiction.Category)
                .ThenBy(jurisdiction => jurisdiction.Name)
                .ToArray();

            var increasedCount = jurisdictions.Count(item => item.Category == FatfListCategory.IncreasedMonitoring);
            var callForActionCount = jurisdictions.Count(item => item.Category == FatfListCategory.CallForAction);
            if (increasedCount < 10 || callForActionCount < 2)
            {
                return null;
            }

            var review = new FatfLlmReview(
                true,
                provider,
                model,
                "Retrieved latest FATF jurisdiction lists using OpenAI hosted web search.",
                null);

            return new FatfSnapshot(
                DateTimeOffset.UtcNow,
                jurisdictions,
                [increasedSource, callForActionSource],
                review);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static FatfSource ToSource(
        FatfListCategory category,
        JsonElement publication,
        string fallbackTitle,
        DateOnly publicationDate)
    {
        var title = TryGetString(publication, "publicationTitle") ?? fallbackTitle;
        var sourceUrl = TryGetString(publication, "sourceUrl")
            ?? throw new InvalidOperationException("Missing sourceUrl.");
        var uri = new Uri(sourceUrl);
        if (!string.Equals(uri.Host, "www.fatf-gafi.org", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("sourceUrl must be an official FATF URL.");
        }

        return new FatfSource(category, title, uri, publicationDate);
    }

    private static DateOnly ToPublicationDate(JsonElement publication)
    {
        var value = TryGetString(publication, "publicationDate")
            ?? throw new InvalidOperationException("Missing publicationDate.");
        if (!DateOnly.TryParseExact(
                value,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var publicationDate))
        {
            throw new InvalidOperationException("publicationDate must use YYYY-MM-DD.");
        }

        return publicationDate;
    }

    private static IEnumerable<FatfJurisdiction> ToJurisdictions(JsonElement publication, FatfSource source)
    {
        if (!publication.TryGetProperty("jurisdictions", out var jurisdictionsElement)
            || jurisdictionsElement.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var item in jurisdictionsElement.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var name = item.GetString();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    yield return new FatfJurisdiction(name.Trim(), source.Category, source.Url.ToString());
                }
            }
        }
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static string StripCodeFence(string text)
    {
        var trimmed = text.Trim();
        var match = Regex.Match(trimmed, "^```(?:json)?\\s*(?<json>.*?)\\s*```$", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        return match.Success ? match.Groups["json"].Value.Trim() : trimmed;
    }

    private static string? ExtractResponseText(JsonElement root)
    {
        if (root.TryGetProperty("output_text", out var outputText)
            && outputText.ValueKind == JsonValueKind.String)
        {
            return outputText.GetString();
        }

        if (!root.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var item in output.EnumerateArray())
        {
            if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var contentItem in content.EnumerateArray())
            {
                if (contentItem.TryGetProperty("text", out var textElement)
                    && textElement.ValueKind == JsonValueKind.String)
                {
                    return textElement.GetString();
                }
            }
        }

        return null;
    }
}
