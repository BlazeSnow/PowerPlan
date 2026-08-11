using PowerPlan.Models;
using PowerPlan.Tests.TestDoubles;
using PowerPlan.Tray;

namespace PowerPlan.Tests;

public sealed class TrayMenuBuilderTests
{
    [Fact]
    public void Build_EmptyPlansIncludesFixedCommandsInOrder()
    {
        var builder = new TrayMenuBuilder(new FakeTrayTextProvider());

        var items = builder.Build(new TrayMenuContext([], null, false));

        Assert.Collection(
            items,
            item => Assert.Equal("PowerPlan", item.Text),
            item => Assert.Equal(TrayMenuAction.OpenMainWindow, item.Command?.Action),
            item => Assert.Equal(TrayMenuItemKind.Separator, item.Kind),
            item => Assert.Equal(TrayMenuItemKind.Separator, item.Kind),
            item => Assert.Equal(TrayMenuAction.RefreshPlans, item.Command?.Action),
            item => Assert.Equal(TrayMenuAction.ToggleStartup, item.Command?.Action),
            item => Assert.Equal(TrayMenuItemKind.Separator, item.Kind),
            item => Assert.Equal(TrayMenuAction.Exit, item.Command?.Action));
        Assert.False(items[0].IsEnabled);
        Assert.Contains("Enable startup", items[5].Text);
    }

    [Fact]
    public void Build_MapsPlansWithSequentialIdsAndCheckedActivePlan()
    {
        var firstGuid = Guid.NewGuid().ToString("D");
        var secondGuid = Guid.NewGuid().ToString("D");
        var builder = new TrayMenuBuilder(new FakeTrayTextProvider());

        var items = builder.Build(new TrayMenuContext(
            [Plan(firstGuid, "Balanced"), Plan(secondGuid, "Performance", true)],
            null,
            true));
        var planItems = items.Where(item => item.Command?.Action == TrayMenuAction.SwitchPlan).ToArray();

        Assert.Equal([1000u, 1001u], planItems.Select(item => item.CommandId));
        Assert.Equal([firstGuid, secondGuid], planItems.Select(item => item.Command?.PlanGuid));
        Assert.Equal([false, true], planItems.Select(item => item.IsChecked));
        Assert.Contains("Disable startup", items.Single(item => item.Command?.Action == TrayMenuAction.ToggleStartup).Text);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(" ", false)]
    public void Build_DoesNotAddHiddenUltimateForBlankGuid(string? hiddenGuid, bool expected)
    {
        var builder = new TrayMenuBuilder(new FakeTrayTextProvider());

        var items = builder.Build(new TrayMenuContext([], hiddenGuid, false));

        Assert.Equal(expected, items.Any(item => item.Command?.Action == TrayMenuAction.ActivateHiddenUltimate));
    }

    [Fact]
    public void Build_DoesNotAddHiddenUltimateWhenPlanExistsIgnoringCase()
    {
        var guid = Guid.NewGuid().ToString("D");
        var builder = new TrayMenuBuilder(new FakeTrayTextProvider());

        var items = builder.Build(new TrayMenuContext([Plan(guid, "Ultimate")], guid.ToUpperInvariant(), false));

        Assert.DoesNotContain(items, item => item.Command?.Action == TrayMenuAction.ActivateHiddenUltimate);
    }

    [Fact]
    public void Build_AddsHiddenUltimateCommandWhenMissing()
    {
        var hiddenGuid = Guid.NewGuid().ToString("D");
        var builder = new TrayMenuBuilder(new FakeTrayTextProvider());

        var item = builder.Build(new TrayMenuContext([], hiddenGuid, false))
            .Single(item => item.Command?.Action == TrayMenuAction.ActivateHiddenUltimate);

        Assert.Equal(5u, item.CommandId);
        Assert.Equal(hiddenGuid, item.Command?.PlanGuid);
        Assert.Contains("Open hidden ultimate", item.Text);
    }

    private static PowerPlanInfo Plan(string guid, string name, bool isActive = false)
    {
        return new PowerPlanInfo { Guid = guid, Name = name, IsActive = isActive };
    }
}
