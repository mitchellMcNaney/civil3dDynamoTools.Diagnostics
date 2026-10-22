using CivilDynamoTools.Diagnostics.Geometry;

namespace CivilDynamoTools.Diagnostics.Geometry.Tests;

public class AngleFormatterTests
{
    [Theory]
    [InlineData(90.0, "90°00'00\"")]
    [InlineData(87.0 + 14.0 / 60 + 32.0 / 3600, "87°14'32\"")]
    [InlineData(270.0, "270°00'00\"")]
    [InlineData(0.0, "0°00'00\"")]
    [InlineData(5.0 + 3.0 / 60 + 7.0 / 3600, "5°03'07\"")]
    public void Formats_whole_seconds(double degrees, string expected)
    {
        Assert.Equal(expected, AngleFormatter.ToDms(degrees));
    }

    [Theory]
    [InlineData(89.0 + 59.0 / 60 + 59.6 / 3600, "90°00'00\"")]
    [InlineData(12.0 + 34.0 / 60 + 59.5 / 3600, "12°35'00\"")]
    [InlineData(12.0 + 34.0 / 60 + 59.4 / 3600, "12°34'59\"")]
    public void Rounding_carries_into_minutes_and_degrees(double degrees, string expected)
    {
        Assert.Equal(expected, AngleFormatter.ToDms(degrees));
    }

    [Fact]
    public void Negative_angles_keep_their_sign()
    {
        Assert.Equal("-10°30'00\"", AngleFormatter.ToDms(-10.5));
    }

    [Fact]
    public void Non_finite_angles_throw()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AngleFormatter.ToDms(double.NaN));
    }
}
