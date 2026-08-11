using PowerPlan.Models;
using PowerPlan.Tray;

namespace PowerPlan.Tests;

public sealed class TrayPlansSnapshotTests
{
    [Fact]
    public void Constructor_CopiesInputPlans()
    {
        var plans = new List<PowerPlanInfo> { Plan("First") };

        var snapshot = new TrayPlansSnapshot(plans);
        plans.Add(Plan("Second"));

        Assert.Single(snapshot.Plans);
        Assert.Equal("First", snapshot.Plans[0].Name);
    }

    [Fact]
    public void WithActivePlan_UpdatesStateIgnoringGuidCaseWithoutMutatingSource()
    {
        var activeGuid = Guid.NewGuid().ToString("D");
        var inactiveGuid = Guid.NewGuid().ToString("D");
        var snapshot = new TrayPlansSnapshot([Plan(activeGuid, true), Plan(inactiveGuid)]);

        var updated = snapshot.WithActivePlan(inactiveGuid.ToUpperInvariant());

        Assert.True(snapshot.Plans[0].IsActive);
        Assert.False(snapshot.Plans[1].IsActive);
        Assert.False(updated.Plans[0].IsActive);
        Assert.True(updated.Plans[1].IsActive);
    }

    [Fact]
    public void WithActivePlan_LeavesAllPlansInactiveForUnknownGuid()
    {
        var snapshot = new TrayPlansSnapshot([Plan(Guid.NewGuid().ToString("D"), true)]);

        var updated = snapshot.WithActivePlan(Guid.NewGuid().ToString("D"));

        Assert.All(updated.Plans, plan => Assert.False(plan.IsActive));
    }

    [Fact]
    public void CreateMenuContext_PreservesPlansAndState()
    {
        var plan = Plan(Guid.NewGuid().ToString("D"));
        var snapshot = new TrayPlansSnapshot([plan]);

        var context = snapshot.CreateMenuContext("hidden-guid", true);

        Assert.Equal(snapshot.Plans, context.Plans);
        Assert.Equal("hidden-guid", context.HiddenUltimatePlanGuid);
        Assert.True(context.IsStartupEnabled);
    }

    private static PowerPlanInfo Plan(string guid, bool isActive = false)
    {
        return new PowerPlanInfo { Guid = guid, Name = guid, IsActive = isActive };
    }
}
