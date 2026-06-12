using System.Net;
using System.Text.RegularExpressions;

namespace FatfMonitor.Api.Compliance;

public sealed class FatfJurisdictionParser
{
    private static readonly Regex ScriptAndStylePattern = new(
        "<(script|style)\\b[^>]*>.*?</\\1>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex AnchorPattern = new(
        "<a\\b[^>]*>(?<text>.*?)</a>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex AnchorWithHrefPattern = new(
        "<a\\b(?=[^>]*\\bhref\\s*=\\s*[\"'](?<href>[^\"']+)[\"'])[^>]*>(?<text>.*?)</a>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex MarkdownCitationPattern = new(
        "\\u2020(?<text>.*?)\\ue201",
        RegexOptions.Compiled);

    public IReadOnlyCollection<string> ParseJurisdictionNames(string html)
    {
        var cleanHtml = ScriptAndStylePattern.Replace(html, string.Empty);
        var countrySegment = ExtractCountrySegment(cleanHtml);
        var names = ExtractAnchorNames(countrySegment);

        if (names.Count == 0)
        {
            names = ExtractMarkdownCitationNames(countrySegment);
        }

        return names
            .Select(NormalizeName)
            .Where(IsJurisdictionName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public IReadOnlyCollection<FatfPublicationLink> ExtractPublicationLinks(string html, Uri baseUri)
    {
        var cleanHtml = ScriptAndStylePattern.Replace(html, string.Empty);
        return AnchorWithHrefPattern
            .Matches(cleanHtml)
            .Select(match => new
            {
                Text = NormalizeName(HtmlToText(match.Groups["text"].Value)),
                Href = WebUtility.HtmlDecode(match.Groups["href"].Value)
            })
            .Select(candidate => TryCreatePublicationLink(candidate.Text, candidate.Href, baseUri))
            .Where(link => link is not null)
            .Select(link => link!)
            .GroupBy(link => $"{link.Category}:{link.Url}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
    }

    public string ExtractReviewExcerpt(string html)
    {
        var text = HtmlToText(ScriptAndStylePattern.Replace(html, string.Empty));
        var countryIndex = text.IndexOf("Country", StringComparison.OrdinalIgnoreCase);
        if (countryIndex < 0)
        {
            return text[..Math.Min(text.Length, 4000)];
        }

        var length = Math.Min(5000, text.Length - countryIndex);
        return text.Substring(countryIndex, length);
    }

    private static FatfPublicationLink? TryCreatePublicationLink(string text, string href, Uri baseUri)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(href))
        {
            return null;
        }

        FatfListCategory? category = null;
        if (text.Contains("Jurisdictions under Increased Monitoring", StringComparison.OrdinalIgnoreCase))
        {
            category = FatfListCategory.IncreasedMonitoring;
        }
        else if (text.Contains("High-Risk Jurisdictions subject to a Call for Action", StringComparison.OrdinalIgnoreCase))
        {
            category = FatfListCategory.CallForAction;
        }

        if (category is null)
        {
            return null;
        }

        return Uri.TryCreate(baseUri, href, out var url)
            ? new FatfPublicationLink(category.Value, text, url)
            : null;
    }

    private static string ExtractCountrySegment(string html)
    {
        var publicationIndex = html.IndexOf("Publication details", StringComparison.OrdinalIgnoreCase);
        var searchStart = publicationIndex >= 0 ? publicationIndex : 0;
        var countryIndex = html.IndexOf("Country", searchStart, StringComparison.OrdinalIgnoreCase);
        if (countryIndex < 0)
        {
            return html;
        }

        var topicIndex = html.IndexOf("Topic", countryIndex, StringComparison.OrdinalIgnoreCase);
        if (topicIndex < 0)
        {
            topicIndex = html.IndexOf("Image", countryIndex, StringComparison.OrdinalIgnoreCase);
        }

        return topicIndex > countryIndex
            ? html[countryIndex..topicIndex]
            : html[countryIndex..];
    }

    private static List<string> ExtractAnchorNames(string html)
    {
        return AnchorPattern
            .Matches(html)
            .Select(match => HtmlToText(match.Groups["text"].Value))
            .ToList();
    }

    private static List<string> ExtractMarkdownCitationNames(string text)
    {
        return MarkdownCitationPattern
            .Matches(text)
            .Select(match => match.Groups["text"].Value)
            .ToList();
    }

    private static string NormalizeName(string value)
    {
        var decoded = WebUtility.HtmlDecode(value)
            .Replace('\u00a0', ' ')
            .Replace("\r", " ")
            .Replace("\n", " ")
            .Trim(' ', ',', ';', '.');

        return Regex.Replace(decoded, "\\s+", " ");
    }

    private static string HtmlToText(string html)
    {
        var withoutTags = Regex.Replace(html, "<[^>]+>", " ");
        return NormalizeName(withoutTags);
    }

    private static bool IsJurisdictionName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string[] excluded =
        [
            "Country",
            "Countries / Jurisdictions",
            "High-risk and other monitored jurisdictions",
            "High-risk and other jurisdictions",
            "Topic"
        ];

        return !excluded.Contains(value, StringComparer.OrdinalIgnoreCase);
    }
}
