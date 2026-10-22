namespace CivilDynamoTools.Diagnostics.Geometry;

public static class AngleFormatter
{
    /// <summary>
    /// Degrees-minutes-seconds rounded to the nearest whole second, e.g. 87°14'32".
    /// Rounding carries (59.6" becomes the next minute), so the text never shows 60" or 60'.
    /// </summary>
    public static string ToDms(double degrees)
    {
        if (double.IsNaN(degrees) || double.IsInfinity(degrees))
            throw new ArgumentOutOfRangeException(nameof(degrees), degrees, "Angle must be a finite number.");

        long totalSeconds = (long)Math.Round(Math.Abs(degrees) * 3600.0, MidpointRounding.AwayFromZero);
        long d = totalSeconds / 3600;
        long m = totalSeconds % 3600 / 60;
        long s = totalSeconds % 60;
        string sign = degrees < 0 && totalSeconds > 0 ? "-" : string.Empty;
        return $"{sign}{d}°{m:00}'{s:00}\"";
    }
}
