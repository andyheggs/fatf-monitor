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

    private static readonly Regex MarkdownCitationPattern = new(
        "†(?<text>[^]+)",
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
            .Where(name => IsJurisdictionName(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
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
