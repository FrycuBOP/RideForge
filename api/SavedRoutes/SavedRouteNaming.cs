using System.Globalization;

namespace RideForgeApi.SavedRoutes;

/// <summary>
/// The auto-name a saved ride gets. Derived on the server so every client names routes the same way.
/// </summary>
public static class SavedRouteNaming
{
    /// <summary>How much of the start label survives into the name before it is cut.</summary>
    public const int MaxLabelLength = 40;

    /// <summary>
    /// <c>Loop from {label} · {km} km</c>, or <c>Loop · {km} km</c> when there is no usable label.
    /// <para>
    /// The label is trimmed and, when longer than <see cref="MaxLabelLength"/> characters, cut to that
    /// length and ended with an ellipsis. The kilometres are the <em>stitched</em> distance — the ride
    /// the rider saw on the stats card, not the one they asked for — rounded to a whole kilometre,
    /// halves away from zero.
    /// </para>
    /// <para>
    /// Stays inside the 120-character <c>name</c> column for any input that passes
    /// <see cref="SavedRouteValidation"/>: at most 40 label characters plus under 25 of fixed text and
    /// digits.
    /// </para>
    /// </summary>
    public static string Derive(string? startLabel, double distanceMeters)
    {
        var km = Math.Round(distanceMeters / 1000, MidpointRounding.AwayFromZero)
            .ToString("0", CultureInfo.InvariantCulture);

        var label = startLabel?.Trim();
        if (string.IsNullOrEmpty(label))
        {
            return $"Loop · {km} km";
        }

        return $"Loop from {Shorten(label)} · {km} km";
    }

    private static string Shorten(string label)
    {
        if (label.Length <= MaxLabelLength)
        {
            return label;
        }

        // Never split a surrogate pair: half an emoji is not a valid string, and Postgres would
        // refuse the row over it.
        var cut = char.IsHighSurrogate(label[MaxLabelLength - 1]) ? MaxLabelLength - 1 : MaxLabelLength;
        return label[..cut].TrimEnd() + "…";
    }
}
