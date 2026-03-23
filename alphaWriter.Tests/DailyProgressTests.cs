using alphaWriter.ViewModels;
using Xunit;

namespace alphaWriter.Tests;

/// <summary>
/// Tests for DailyProgressEntry display formatting.
/// DeltaColor references Microsoft.Maui.Graphics.Color, which is a pure struct
/// (no MAUI host required for Color.FromArgb).
/// </summary>
public class DailyProgressTests
{
    // ── DeltaText ─────────────────────────────────────────────────────────────

    [Fact]
    public void DeltaText_PositiveDelta_HasPlusPrefix()
    {
        var entry = new DailyProgressEntry { Delta = 500 };
        Assert.StartsWith("+", entry.DeltaText);
        Assert.Contains("500", entry.DeltaText);
    }

    [Fact]
    public void DeltaText_ZeroDelta_HasPlusPrefix()
    {
        var entry = new DailyProgressEntry { Delta = 0 };
        Assert.StartsWith("+", entry.DeltaText);
    }

    [Fact]
    public void DeltaText_NegativeDelta_NoPlus()
    {
        var entry = new DailyProgressEntry { Delta = -200 };
        Assert.DoesNotContain("+", entry.DeltaText);
        Assert.Contains("200", entry.DeltaText);
    }

    [Fact]
    public void DeltaText_LargeNumber_FormattedWithComma()
    {
        var entry = new DailyProgressEntry { Delta = 1500 };
        // N0 format inserts thousands separator
        Assert.Contains(",", entry.DeltaText);
    }

    // ── DeltaColor ────────────────────────────────────────────────────────────

    [Fact]
    public void DeltaColor_PositiveDelta_IsGreen()
    {
        var entry = new DailyProgressEntry { Delta = 100 };
        var expected = Microsoft.Maui.Graphics.Color.FromArgb("#4CAF50");
        Assert.Equal(expected, entry.DeltaColor);
    }

    [Fact]
    public void DeltaColor_ZeroDelta_IsGreen()
    {
        var entry = new DailyProgressEntry { Delta = 0 };
        var expected = Microsoft.Maui.Graphics.Color.FromArgb("#4CAF50");
        Assert.Equal(expected, entry.DeltaColor);
    }

    [Fact]
    public void DeltaColor_NegativeDelta_IsRed()
    {
        var entry = new DailyProgressEntry { Delta = -1 };
        var expected = Microsoft.Maui.Graphics.Color.FromArgb("#F44336");
        Assert.Equal(expected, entry.DeltaColor);
    }

    // ── DisplayDate ───────────────────────────────────────────────────────────

    [Fact]
    public void DisplayDate_Today_ReturnsToday()
    {
        var todayStr = DateTime.Today.ToString("yyyy-MM-dd");
        var entry = new DailyProgressEntry { Date = todayStr };
        Assert.Equal("Today", entry.DisplayDate);
    }

    [Fact]
    public void DisplayDate_Yesterday_ReturnsYesterday()
    {
        var yesterdayStr = DateTime.Today.AddDays(-1).ToString("yyyy-MM-dd");
        var entry = new DailyProgressEntry { Date = yesterdayStr };
        Assert.Equal("Yesterday", entry.DisplayDate);
    }

    [Fact]
    public void DisplayDate_OlderDate_ReturnsFormattedString()
    {
        // A fixed date well in the past
        var entry = new DailyProgressEntry { Date = "2024-01-15" };
        var display = entry.DisplayDate;
        // Should not be "Today" or "Yesterday"
        Assert.NotEqual("Today", display);
        Assert.NotEqual("Yesterday", display);
        // Should contain the day abbreviation and month abbreviation
        Assert.Contains("Jan", display);
        Assert.Contains("15", display);
    }

    [Fact]
    public void DisplayDate_InvalidDate_ReturnsRawString()
    {
        var entry = new DailyProgressEntry { Date = "not-a-date" };
        Assert.Equal("not-a-date", entry.DisplayDate);
    }

    [Fact]
    public void DisplayDate_EmptyDate_ReturnsEmpty()
    {
        var entry = new DailyProgressEntry { Date = string.Empty };
        Assert.Equal(string.Empty, entry.DisplayDate);
    }
}
