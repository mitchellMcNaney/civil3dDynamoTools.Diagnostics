using CivilDynamoTools.Diagnostics.Geometry;

namespace CivilDynamoTools.Diagnostics.Geometry.Tests;

public class RowParcelMatcherTests
{
    private static readonly IReadOnlyList<string> Defaults = RowParcelMatcher.ParseTerms(RowParcelMatcher.DefaultTerms);

    [Theory]
    [InlineData("ROW : 1", "Standard")]
    [InlineData("row", "Standard")]
    [InlineData("Lot 5", "ROW")]
    [InlineData("Lot 5", "Road - ROW")]
    [InlineData("Right-of-Way 2", "Standard")]
    [InlineData("Lot 5", "right of way")]
    public void Matches_whole_words_in_name_or_style(string name, string style)
        => Assert.NotNull(RowParcelMatcher.Match(name, style, Defaults));

    [Theory]
    [InlineData("Arrowhead 3", "Standard")]
    [InlineData("Lot 5", "Borrow Pit")]
    [InlineData("Rows 1", "Standard")]
    [InlineData("Lot 5", "Single-Family")]
    public void Does_not_match_inside_other_words(string name, string style)
        => Assert.Null(RowParcelMatcher.Match(name, style, Defaults));

    [Fact]
    public void Reason_names_the_field_and_term()
        => Assert.Equal("Style 'Road ROW' matches 'ROW'.", RowParcelMatcher.Match("Lot 1", "Road ROW", Defaults));

    [Fact]
    public void Empty_terms_match_nothing()
        => Assert.Null(RowParcelMatcher.Match("ROW 1", "ROW", RowParcelMatcher.ParseTerms(" ; ")));

    [Fact]
    public void Custom_terms_are_trimmed()
        => Assert.NotNull(RowParcelMatcher.Match("Street Parcel", null, RowParcelMatcher.ParseTerms(" Street ;")));
}
