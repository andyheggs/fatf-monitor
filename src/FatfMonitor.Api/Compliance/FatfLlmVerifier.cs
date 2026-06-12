using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace FatfMonitor.Api.Compliance;

public interface IFatfLlmVerifier
{
    Task<FatfLlmReview> ReviewAsync(
        FatfSnapshot snapshot,
        IReadOnlyDictionary<FatfListCategory, string> sourceExcerpts,
        CancellationToken cancellationToken);
}

public sealed class OpenAiFatfLlmVerifier(HttpClient httpClient, IConfiguration configuration) : IFatfLlmVerifier
{
    public async Task<FatfLlmReview> ReviewAsync(
        FatfSnapshot snapshot,
        IReadOnlyDictionary<FatfListCategory, string> sourceExcerpts,
        CancellationToken cancellationToken)
    {
        var options = configuration.GetSection("Llm").Get<LlmOptions>() ?? new LlmOptions();
        var apiKey = options.ApiKey ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new FatfLlmReview(false, options.Provider, options.Model, "LLM review skipped because no API key is configured.", null);
        }

        var prompt = BuildPrompt(snapshot, sourceExcerpts);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(new
        {
            model = options.Model,
            temperature = 0,
            messages = new[]
            {
                new
                {
                    role = "system",
                    content = "You review FATF jurisdiction extraction. Return compact JSON with keys summary and confidence. Do not add countries that are not present in the provided excerpts."
                },
                new
                {
                    role = "user",
                    content = prompt
                }
            }
        }), Encoding.UTF8, "application/json");

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return new FatfLlmReview(
                false,
                options.Provider,
                options.Model,
                $"LLM review failed with OpenAI HTTP {(int)response.StatusCode} ({response.StatusCode}).",
                null);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var content = document.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? string.Empty;

        return ParseReview(content, options);
    }

    private static string BuildPrompt(
        FatfSnapshot snapshot,
        IReadOnlyDictionary<FatfListCategory, string> sourceExcerpts)
    {
        var extracted = snapshot.Jurisdictions
            .GroupBy(jurisdiction => jurisdiction.Category)
            .Select(group => $"{group.Key}: {string.Join(", ", group.Select(jurisdiction => jurisdiction.Name).Order())}");

        var excerpts = sourceExcerpts.Select(pair => $"{pair.Key} excerpt:\n{pair.Value}");

        return $"""
            Extracted jurisdiction lists:
            {string.Join("\n", extracted)}

            Source excerpts:
            {string.Join("\n\n", excerpts)}

            Check whether the extracted names match the publication-details country lists. Reply JSON only.
            """;
    }

    private static FatfLlmReview ParseReview(string content, LlmOptions options)
    {
        try
        {
            using var document = JsonDocument.Parse(content);
            var summary = document.RootElement.TryGetProperty("summary", out var summaryElement)
                ? ToReviewText(summaryElement)
                : content;
            decimal? confidence = document.RootElement.TryGetProperty("confidence", out var confidenceElement)
                ? ToConfidence(confidenceElement)
                : null;

            return new FatfLlmReview(true, options.Provider, options.Model, summary, confidence);
        }
        catch (JsonException)
        {
            return new FatfLlmReview(true, options.Provider, options.Model, content, null);
        }
    }

    private static string ToReviewText(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? string.Empty,
            JsonValueKind.Null => string.Empty,
            JsonValueKind.Undefined => string.Empty,
            _ => element.GetRawText()
        };
    }

    private static decimal? ToConfidence(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetDecimal(out var number))
        {
            return number;
        }

        if (element.ValueKind == JsonValueKind.String
            && decimal.TryParse(element.GetString(), out var parsed))
        {
            return parsed;
        }

        return null;
    }
}
