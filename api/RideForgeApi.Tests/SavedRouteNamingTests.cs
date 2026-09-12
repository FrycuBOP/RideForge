using System.Text;

using RideForgeApi.SavedRoutes;

namespace RideForgeApi.Tests;

/// <summary>
/// Pins the auto-name rule decided in the save-route plan: <c>Loop from {label} · {km} km</c>, label
/// trimmed and cut at 40 characters with an ellipsis, <c>Loop · {km} km</c> without a label, and the
/// kilometres taken from the stitched distance rounded to a whole number.
/// <para>
/// Expected names are written out literally. Building them with the same formatting the code uses
/// would pass against a wrong format just as happily as against the right one.
/// </para>
/// </summary>
public class SavedRouteNamingTests
{
    [Fact]
    public void ShortLabel_IsUsedWhole()
    {
        Assert.Equal("Loop from Kraków · 42 km", SavedRouteNaming.Derive("Kraków", 41_600));
    }

    [Fact]
    public void Label_IsTrimmed()
    {
        Assert.Equal("Loop from Kraków · 42 km", SavedRouteNaming.Derive("  Kraków \t", 41_600));
    }

    [Fact]
    public void LabelOfExactlyFortyCharacters_IsKeptWithoutAnEllipsis()
    {
        const string forty = "Zakopane, Tatra County, Lesser Poland Vo";

        Assert.Equal($"Loop from {forty} · 30 km", SavedRouteNaming.Derive(forty, 30_000));
    }

    [Fact]
    public void LabelOverFortyCharacters_IsCutAtFortyWithAnEllipsis()
    {
        Assert.Equal(
            "Loop from Zakopane, Tatra County, Lesser Poland Vo… · 30 km",
            SavedRouteNaming.Derive("Zakopane, Tatra County, Lesser Poland Voivodeship", 30_000));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MissingOrBlankLabel_FallsBackToTheLabelFreeName(string? label)
    {
        Assert.Equal("Loop · 42 km", SavedRouteNaming.Derive(label, 41_600));
    }

    [Theory]
    [InlineData(41_600, "42")]
    [InlineData(41_499, "41")]
    // A half rounds up, as a rider reading "42.5 km" would expect. Banker's rounding (Math.Round's
    // default) would say 42 here — the case exists to catch exactly that.
    [InlineData(42_500, "43")]
    public void Kilometres_AreTheStitchedDistanceRoundedToAWholeNumber(double meters, string km)
    {
        Assert.Equal($"Loop from Kraków · {km} km", SavedRouteNaming.Derive("Kraków", meters));
    }

    [Fact]
    public void CutThroughAnEmoji_NeverLeavesHalfACharacter()
    {
        // 39 letters then a motorcycle emoji: the 40th and 41st UTF-16 units are its surrogate pair.
        // Keeping only the first half would produce a string Postgres cannot store.
        var label = new string('a', 39) + "🏍" + " ride";

        var name = SavedRouteNaming.Derive(label, 30_000);

        var strictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        var ex = Record.Exception(() => strictUtf8.GetBytes(name));
        Assert.Null(ex);
    }

    [Fact]
    public void LongestAcceptedInput_FitsTheNameColumn()
    {
        var name = SavedRouteNaming.Derive(
            new string('x', SavedRouteValidation.MaxStartLabelLength),
            SavedRouteValidation.MaxDistanceMeters);

        // The saved_routes.name column is varchar(120).
        Assert.True(name.Length <= 120, $"Name is {name.Length} characters: {name}");
    }
}
