using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FatfMonitor.Api.Compliance;

public sealed class GovUkFatfAdvisoryParser
{
    private static readonly Regex FatfDatePattern = new(
        @"On\s+(?<date>\d{1,2}\s+[A-Za-z]+\s+\d{4}),\s+the\s+FATF\s+published",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ListPattern = new(
        @"<h4[^>]*id=""(?<id>jurisdictions-under-increased-monitoring|high-risk-jurisdictions-subject-to-a-call-for-action)""[^>]*>.*?</h4>\s*<ul>(?<list>.*?)</ul>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex ItemPattern = new(
        @"<li[^>]*>(?<name>.*?)</li>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    public FatfSnapshot? TryParse(
        string html,
        Uri sourceUrl,
        DateOnly today,
        DateOnly minimumPublicationDate)
    {
        var answerHtml = ExtractFatfStatementHtml(html);
        if (string.IsNullOrWhiteSpace(answerHtml))
        {
            return null;
        }

        var plainText = HtmlToText(answerHtml);
        var dateMatch = FatfDatePattern.Match(plainText);
        if (!dateMatch.Success
            || !DateOnly.TryParseExact(
                dateMatch.Groups["date"].Value,
                "d MMMM yyyy",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var publicationDate)
            || publicationDate < minimumPublicationDate
            || publicationDate > today)
        {
            return null;
        }

        var lists = ListPattern.Matches(answerHtml)
            .ToDictionary(
                match => match.Groups["id"].Value,
                match => ParseItems(match.Groups["list"].Value),
                StringComparer.OrdinalIgnoreCase);

        if (!lists.TryGetValue("jurisdictions-under-increased-monitoring", out var increased)
            || !lists.TryGetValue("high-risk-jurisdictions-subject-to-a-call-for-action", out var callForAction)
            || increased.Count < 10
            || callForAction.Count < 2)
        {
            return null;
        }

        var increasedSource = new FatfSource(
            FatfListCategory.IncreasedMonitoring,
            $"Jurisdictions under Increased Monitoring - {publicationDate:dd MMMM yyyy}",
            sourceUrl,
            publicationDate);
        var callForActionSource = new FatfSource(
            FatfListCategory.CallForAction,
            $"High-Risk Jurisdictions subject to a Call for Action - {publicationDate:dd MMMM yyyy}",
            sourceUrl,
            publicationDate);

        var jurisdictions = increased
            .Select(name => new FatfJurisdiction(name, FatfListCategory.IncreasedMonitoring, sourceUrl.ToString()))
            .Concat(callForAction.Select(name =>
                new FatfJurisdiction(name, FatfListCategory.CallForAction, sourceUrl.ToString())))
            .OrderBy(jurisdiction => jurisdiction.Category)
            .ThenBy(jurisdiction => jurisdiction.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new FatfSnapshot(
            DateTimeOffset.UtcNow,
            jurisdictions,
            [increasedSource, callForActionSource],
            new FatfLlmReview(
                false,
                "HM Treasury",
                "Deterministic parser",
                "Retrieved the latest FATF jurisdiction lists from the official HM Treasury advisory.",
                null));
    }

    private static string? ExtractFatfStatementHtml(string html)
    {
        var scriptMatch = Regex.Match(
            html,
            @"<script[^>]*type=""application/ld\+json""[^>]*>(?<json>.*?)</script>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!scriptMatch.Success)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(scriptMatch.Groups["json"].Value);
            if (!document.RootElement.TryGetProperty("mainEntity", out var entities)
                || entities.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (var entity in entities.EnumerateArray())
            {
                if (entity.TryGetProperty("name", out var name)
                    && name.GetString()?.Equals("FATF public statement", StringComparison.OrdinalIgnoreCase) == true
                    && entity.TryGetProperty("acceptedAnswer", out var answer)
                    && answer.TryGetProperty("text", out var text))
                {
                    return text.GetString();
                }
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private static IReadOnlyCollection<string> ParseItems(string listHtml) =>
        ItemPattern.Matches(listHtml)
            .Select(match => HtmlToText(match.Groups["name"].Value))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string HtmlToText(string html)
    {
        var withoutTags = Regex.Replace(html, "<[^>]+>", " ");
        var decoded = WebUtility.HtmlDecode(withoutTags)
            .Replace('\u00a0', ' ')
            .Replace('\u2018', '\'')
            .Replace('\u2019', '\'');
        return Regex.Replace(decoded, @"\s+", " ").Trim();
    }
}
