using System.Globalization;

namespace F1Dash.Core.Analysis;

/// <summary>
/// Parses the feed's time strings. They arrive as "1:23.456" for a lap and
/// "23.456" for a sector, and are empty until a driver has set one.
/// </summary>
public static class LapTime
{
    public static double? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var span = value.AsSpan().Trim();
        var colon = span.IndexOf(':');

        if (colon < 0)
        {
            return double.TryParse(span, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
                ? seconds
                : null;
        }

        return int.TryParse(span[..colon], NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes)
            && double.TryParse(span[(colon + 1)..], NumberStyles.Float, CultureInfo.InvariantCulture, out var rest)
            ? (minutes * 60.0) + rest
            : null;
    }

    /// <summary>Formats seconds back into the feed's own shape, for display.</summary>
    public static string Format(double? seconds)
    {
        if (seconds is not { } s || s <= 0) return "";

        var minutes = (int)(s / 60);
        var rest = s - (minutes * 60);

        return minutes > 0
            ? $"{minutes}:{rest.ToString("00.000", CultureInfo.InvariantCulture)}"
            : rest.ToString("0.000", CultureInfo.InvariantCulture);
    }
}
