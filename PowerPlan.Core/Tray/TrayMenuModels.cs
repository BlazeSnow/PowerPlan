using PowerPlan.Models;

namespace PowerPlan.Tray;

public sealed record TrayMenuContext(
    IReadOnlyList<PowerPlanInfo> Plans,
    string? HiddenUltimatePlanGuid,
    bool IsStartupEnabled);

public enum TrayMenuItemKind
{
    Command,
    Separator
}

public enum TrayMenuAction
{
    OpenMainWindow,
    SwitchPlan,
    ActivateHiddenUltimate,
    RefreshPlans,
    ToggleStartup,
    Exit
}

public readonly record struct TrayMenuCommand(TrayMenuAction Action, string? PlanGuid = null, string? PlanName = null);

public sealed record TrayMenuItem(
    TrayMenuItemKind Kind,
    uint CommandId = 0,
    string? Text = null,
    bool IsChecked = false,
    bool IsEnabled = true,
    TrayMenuCommand? Command = null);
