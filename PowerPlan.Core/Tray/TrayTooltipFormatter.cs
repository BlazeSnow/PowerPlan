using PowerPlan.Models;

namespace PowerPlan.Tray;

public static class TrayTooltipFormatter
{
    public static string Format(
        IReadOnlyList<PowerPlanInfo> plans,
        bool isStartupEnabled,
        ITrayTextProvider textProvider,
        int maximumLength = 127)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumLength);

        var activePlanName = plans.FirstOrDefault(plan => plan.IsActive)?.Name;
        var planText = string.IsNullOrWhiteSpace(activePlanName)
            ? textProvider.Get("Tray.Tooltip.PlanUnavailable")
            : textProvider.Format("Tray.Tooltip.Plan", activePlanName);
        var startupState = textProvider.Get(isStartupEnabled ? "App.Status.On" : "App.Status.Off");
        var startupText = textProvider.Format("Tray.Tooltip.AutoStart", startupState);
        var tooltip = $"{textProvider.Get("App.WindowTitle")}\n{planText}\n{startupText}";
        return tooltip.Length <= maximumLength
            ? tooltip
            : tooltip[..maximumLength];
    }
}
