using alphaWriter.Converters;
using alphaWriter.Models;
using System.Globalization;
using Xunit;

namespace alphaWriter.Tests;

/// <summary>
/// Tests for all IValueConverter implementations. Converters are stateless and
/// can be tested without a running MAUI host — Color.FromArgb is pure parsing.
/// </summary>
public class ConverterTests
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // ── SceneStatusColorConverter ─────────────────────────────────────────────

    [Theory]
    [InlineData(SceneStatus.Outline,    "#B05060")]
    [InlineData(SceneStatus.Draft,      "#C0A030")]
    [InlineData(SceneStatus.FirstEdit,  "#5090C0")]
    [InlineData(SceneStatus.SecondEdit, "#6080B0")]
    [InlineData(SceneStatus.Done,       "#50A060")]
    public void SceneStatusColor_KnownStatus_ReturnsExpectedColor(SceneStatus status, string expectedArgb)
    {
        var converter = new SceneStatusColorConverter();
        var result = converter.Convert(status, typeof(Microsoft.Maui.Graphics.Color), null, Inv);
        var expected = Microsoft.Maui.Graphics.Color.FromArgb(expectedArgb);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void SceneStatusColor_NullInput_ReturnsFallbackColor()
    {
        var converter = new SceneStatusColorConverter();
        var result = converter.Convert(null, typeof(Microsoft.Maui.Graphics.Color), null, Inv);
        var fallback = Microsoft.Maui.Graphics.Color.FromArgb("#5A5A6A");
        Assert.Equal(fallback, result);
    }

    [Fact]
    public void SceneStatusColor_NonStatusInput_ReturnsFallbackColor()
    {
        var converter = new SceneStatusColorConverter();
        var result = converter.Convert("not a status", typeof(Microsoft.Maui.Graphics.Color), null, Inv);
        var fallback = Microsoft.Maui.Graphics.Color.FromArgb("#5A5A6A");
        Assert.Equal(fallback, result);
    }

    [Fact]
    public void SceneStatusColor_ConvertBack_Throws()
    {
        var converter = new SceneStatusColorConverter();
        Assert.Throws<NotSupportedException>(() =>
            converter.ConvertBack(null, typeof(SceneStatus), null, Inv));
    }

    // ── SceneStatusDisplayConverter ───────────────────────────────────────────

    [Theory]
    [InlineData(SceneStatus.Outline,    "Outline")]
    [InlineData(SceneStatus.Draft,      "Draft")]
    [InlineData(SceneStatus.FirstEdit,  "1st Edit")]
    [InlineData(SceneStatus.SecondEdit, "2nd Edit")]
    [InlineData(SceneStatus.Done,       "Done")]
    public void SceneStatusDisplay_KnownStatus_ReturnsLabel(SceneStatus status, string expected)
    {
        var converter = new SceneStatusDisplayConverter();
        var result = converter.Convert(status, typeof(string), null, Inv);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void SceneStatusDisplay_NullInput_ReturnsEmptyString()
    {
        var converter = new SceneStatusDisplayConverter();
        var result = converter.Convert(null, typeof(string), null, Inv);
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void SceneStatusDisplay_NonStatusInput_ReturnsEmptyString()
    {
        var converter = new SceneStatusDisplayConverter();
        var result = converter.Convert(42, typeof(string), null, Inv);
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void SceneStatusDisplay_ConvertBack_Throws()
    {
        var converter = new SceneStatusDisplayConverter();
        Assert.Throws<NotSupportedException>(() =>
            converter.ConvertBack("Draft", typeof(SceneStatus), null, Inv));
    }

    // ── IsNotNullConverter ────────────────────────────────────────────────────

    [Fact]
    public void IsNotNull_NullInput_ReturnsFalse()
    {
        var converter = new IsNotNullConverter();
        var result = converter.Convert(null, typeof(bool), null, Inv);
        Assert.Equal(false, result);
    }

    [Fact]
    public void IsNotNull_StringInput_ReturnsTrue()
    {
        var converter = new IsNotNullConverter();
        var result = converter.Convert("hello", typeof(bool), null, Inv);
        Assert.Equal(true, result);
    }

    [Fact]
    public void IsNotNull_ObjectInput_ReturnsTrue()
    {
        var converter = new IsNotNullConverter();
        var result = converter.Convert(new object(), typeof(bool), null, Inv);
        Assert.Equal(true, result);
    }

    [Fact]
    public void IsNotNull_ConvertBack_Throws()
    {
        var converter = new IsNotNullConverter();
        Assert.Throws<NotSupportedException>(() =>
            converter.ConvertBack(true, typeof(object), null, Inv));
    }

    // ── IsNotNullOrEmptyConverter ─────────────────────────────────────────────

    [Fact]
    public void IsNotNullOrEmpty_NullInput_ReturnsFalse()
    {
        var converter = new IsNotNullOrEmptyConverter();
        var result = converter.Convert(null, typeof(bool), null, Inv);
        Assert.Equal(false, result);
    }

    [Fact]
    public void IsNotNullOrEmpty_EmptyString_ReturnsFalse()
    {
        var converter = new IsNotNullOrEmptyConverter();
        var result = converter.Convert(string.Empty, typeof(bool), null, Inv);
        Assert.Equal(false, result);
    }

    [Fact]
    public void IsNotNullOrEmpty_NonEmptyString_ReturnsTrue()
    {
        var converter = new IsNotNullOrEmptyConverter();
        var result = converter.Convert("hello", typeof(bool), null, Inv);
        Assert.Equal(true, result);
    }

    [Fact]
    public void IsNotNullOrEmpty_NonStringInput_ReturnsFalse()
    {
        // The converter only returns true for string values
        var converter = new IsNotNullOrEmptyConverter();
        var result = converter.Convert(42, typeof(bool), null, Inv);
        Assert.Equal(false, result);
    }

    [Fact]
    public void IsNotNullOrEmpty_ConvertBack_Throws()
    {
        var converter = new IsNotNullOrEmptyConverter();
        Assert.Throws<NotSupportedException>(() =>
            converter.ConvertBack(true, typeof(string), null, Inv));
    }
}
