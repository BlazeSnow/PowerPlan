using PowerPlan.Models;
using PowerPlan.Tests.TestDoubles;
using PowerPlan.Tray;

namespace PowerPlan.Tests;

public sealed class TrayTooltipFormatterTests
{
    [Fact]
    public void Format_UsesActivePlanAndStartupState()
    {
        var provider = new FakeTrayTextProvider();

        var tooltip = TrayTooltipFormatter.Format(
            [Plan("Balanced", true)],
            true,
            provider);

        Assert.Equal("PowerPlan\nPlan: Balanced\nStartup: On", tooltip);
    }

    [Fact]
    public void Format_UsesFirstActivePlanAndUnavailableFallback()
    {
        var provider = new FakeTrayTextProvider();

        var first = TrayTooltipFormatter.Format([Plan("First", true), Plan("Second", true)], false, provider);
        var unavailable = TrayTooltipFormatter.Format([Plan("Inactive")], false, provider);

        Assert.Equal("PowerPlan\nPlan: First\nStartup: Off", first);
        Assert.Equal("PowerPlan\nPlan unavailable\nStartup: Off", unavailable);
    }

    [Theory]
    [InlineData(127, 127)]
    [InlineData(128, 127)]
    public void Format_TruncatesToMaximumUtf16Length(int inputLength, int expectedLength)
    {
        var provider = new FakeTrayTextProvider();
        provider.Text["App.WindowTitle"] = new string('A', inputLength);

        var tooltip = TrayTooltipFormatter.Format([], false, provider, maximumLength: expectedLength);

        Assert.Equal(expectedLength, tooltip.Length);
    }

    [Fact]
    public void Format_RejectsNegativeMaximumLength()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TrayTooltipFormatter.Format([], false, new FakeTrayTextProvider(), -1));
    }

    private static PowerPlanInfo Plan(string name, bool isActive = false)
    {
        return new PowerPlanInfo { Guid = Guid.NewGuid().ToString("D"), Name = name, IsActive = isActive };
    }
}
