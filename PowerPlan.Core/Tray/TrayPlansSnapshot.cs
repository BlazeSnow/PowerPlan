using PowerPlan.Models;

namespace PowerPlan.Tray;

public sealed class TrayPlansSnapshot
{
    public TrayPlansSnapshot(IReadOnlyList<PowerPlanInfo> plans)
    {
        Plans = plans.ToArray();
    }

    public IReadOnlyList<PowerPlanInfo> Plans { get; }

    public TrayPlansSnapshot Replace(IReadOnlyList<PowerPlanInfo> plans) => new(plans);

    public TrayPlansSnapshot WithActivePlan(string activePlanGuid)
    {
        return new TrayPlansSnapshot(Plans
            .Select(plan => plan with { IsActive = string.Equals(plan.Guid, activePlanGuid, StringComparison.OrdinalIgnoreCase) })
            .ToArray());
    }

    public TrayMenuContext CreateMenuContext(string? hiddenUltimatePlanGuid, bool isStartupEnabled)
    {
        return new TrayMenuContext(Plans, hiddenUltimatePlanGuid, isStartupEnabled);
    }
}
