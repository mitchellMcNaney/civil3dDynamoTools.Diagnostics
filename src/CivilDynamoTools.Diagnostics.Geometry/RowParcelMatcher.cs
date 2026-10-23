using System.Text.RegularExpressions;

namespace CivilDynamoTools.Diagnostics.Geometry;

/// <summary>
/// Decides whether a parcel is right-of-way from its name and style name. Terms are matched
/// case-insensitively as whole words, so "ROW" matches "ROW 1" or "Road ROW" but not "Arrow".
/// </summary>
public static class RowParcelMatcher
{
    public const string DefaultTerms = "ROW;Right-of-Way;Right of Way";

    /// <summary>Splits a ';'-separated term list, ignoring blanks. An empty list matches nothing.</summary>
    public static IReadOnlyList<string> ParseTerms(string? terms)
        => (terms ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

    /// <summary>Why the parcel counts as ROW (which field matched which term), or null if it does not.</summary>
    public static string? Match(string? parcelName, string? styleName, IReadOnlyList<string> terms)
    {
        foreach (string term in terms)
        {
            if (ContainsWord(styleName, term))
                return $"Style '{styleName}' matches '{term}'.";
            if (ContainsWord(parcelName, term))
                return $"Name '{parcelName}' matches '{term}'.";
        }
        return null;
    }

    private static bool ContainsWord(string? text, string term)
        => !string.IsNullOrEmpty(text)
        && Regex.IsMatch(text, $"(?<![\\p{{L}}\\p{{N}}]){Regex.Escape(term)}(?![\\p{{L}}\\p{{N}}])", RegexOptions.IgnoreCase);
}
