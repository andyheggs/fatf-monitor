using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FatfMonitor.Api.Compliance;

public interface IFatfWebSearchProvider
{
    Task<FatfSnapshot?> TryFetchLatestAsync(CancellationToken cancellationToken);
}

public sealed class OpenAiFatfWebSearchProvider(HttpClient httpClient, IConfiguration configuration) : IFatfWebSearchProvider
{
    public async Task<FatfSnapshot?> TryFetchLatestAsync(CancellationToken cancellationToken)
    {
        var options = configuration.GetSection("Llm").Get<LlmOptions>() ?? new LlmOptions();
        var apiKey = options.ApiKey ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return null;
        }

        var model = string.IsNullOrWhiteSpace(options.SearchModel) ? options.Model : options.SearchModel;
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(new
        {
            model,
            tools = new[] { new { type = "web_search" } },
            tool_choice = "required",
            input = BuildPrompt()
        }), Encoding.UTF8, "application/json");

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var responseText = ExtractResponseText(document.RootElement);
        if (string.IsNullOrWhiteSpace(responseText))
        {
            return null;
        }

        return TryParseSnapshot(responseText, options.Provider, model);
    }

    private static string BuildPrompt() => """
        Search the web for the latest FATF publications on fatf-gafi.org for both:
        1. Jurisdictions under Increased Monitoring
        2. High-Risk Jurisdictions subject to a Call for Action

        Use the latest available FATF publication pages. Prefer official fatf-gafi.org sources.
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

    private static FatfSnapshot? TryParseSnapshot(string responseText, string provider, string model)
    {
        var json = StripCodeFence(responseText);
        try
        {
            using var document = JsonDocument.Parse(json);
            var increased = document.RootElement.GetProperty("increasedMonitoring");
            var callForAction = document.RootElement.GetProperty("callForAction");

            var increasedSource = ToSource(
                FatfListCategory.IncreasedMonitoring,
                increased,
                "Jurisdictions under Increased Monitoring");
            var callForActionSource = ToSource(
                FatfListCategory.CallForAction,
                callForAction,
                "High-Risk Jurisdictions subject to a Call for Action");

            var jurisdictions = ToJurisdictions(increased, increasedSource)
                .Concat(ToJurisdictions(callForAction, callForActionSource))
                .OrderBy(jurisdiction => jurisdiction.Category)
                .ThenBy(jurisdiction => jurisdiction.Name)
                .ToArray();

            if (jurisdictions.Length == 0)
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

    private static FatfSource ToSource(FatfListCategory category, JsonElement publication, string fallbackTitle)
    {
        var title = TryGetString(publication, "publicationTitle") ?? fallbackTitle;
        var sourceUrl = TryGetString(publication, "sourceUrl")
            ?? throw new InvalidOperationException("Missing sourceUrl.");
        return new FatfSource(category, title, new Uri(sourceUrl));
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
