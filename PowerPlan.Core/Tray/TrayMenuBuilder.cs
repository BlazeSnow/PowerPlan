using PowerPlan.Models;

namespace PowerPlan.Tray;

public sealed class TrayMenuBuilder(ITrayTextProvider textProvider)
{
    private const string OpenMainWindowIcon = "\u2302 ";
    private const string PowerPlanIcon = "\u26A1 ";
    private const string RefreshPlansIcon = "\u21BB ";
    private const string StartupIcon = "\u23FB ";
    private const string ExitIcon = "\u2715 ";
    private const uint FirstPlanCommandId = 1000;
    private const uint OpenMainWindowCommandId = 1;
    private const uint RefreshPlansCommandId = 2;
    private const uint ToggleStartupCommandId = 3;
    private const uint ExitCommandId = 4;
    private const uint ActivateHiddenUltimateCommandId = 5;

    public IReadOnlyList<TrayMenuItem> Build(TrayMenuContext context)
    {
        var items = new List<TrayMenuItem>
        {
            new(TrayMenuItemKind.Command, Text: textProvider.Get("App.WindowTitle"), IsEnabled: false),
            CreateCommand(OpenMainWindowCommandId, OpenMainWindowIcon + textProvider.Get("Tray.Menu.OpenMainWindow"), new(TrayMenuAction.OpenMainWindow)),
            Separator()
        };

        var commandId = FirstPlanCommandId;
        foreach (var plan in context.Plans)
        {
            items.Add(CreateCommand(
                commandId++,
                PowerPlanIcon + plan.Name,
                new(TrayMenuAction.SwitchPlan, plan.Guid, plan.Name),
                plan.IsActive));
        }

        if (!string.IsNullOrWhiteSpace(context.HiddenUltimatePlanGuid)
            && !context.Plans.Any(plan => string.Equals(plan.Guid, context.HiddenUltimatePlanGuid, StringComparison.OrdinalIgnoreCase)))
        {
            items.Add(CreateCommand(
                ActivateHiddenUltimateCommandId,
                PowerPlanIcon + textProvider.Get("Tray.Menu.OpenHiddenUltimate"),
                new(TrayMenuAction.ActivateHiddenUltimate, context.HiddenUltimatePlanGuid)));
        }

        items.Add(Separator());
        items.Add(CreateCommand(
            RefreshPlansCommandId,
            RefreshPlansIcon + textProvider.Get("Tray.Menu.RefreshPlans"),
            new(TrayMenuAction.RefreshPlans)));
        items.Add(CreateCommand(
            ToggleStartupCommandId,
            StartupIcon + textProvider.Get(context.IsStartupEnabled
                ? "Tray.Menu.DisableAutoStart"
                : "Tray.Menu.EnableAutoStart"),
            new(TrayMenuAction.ToggleStartup)));
        items.Add(Separator());
        items.Add(CreateCommand(
            ExitCommandId,
            ExitIcon + textProvider.Get("Tray.Menu.Exit"),
            new(TrayMenuAction.Exit)));
        return items;
    }

    private static TrayMenuItem CreateCommand(uint commandId, string text, TrayMenuCommand command, bool isChecked = false)
    {
        return new TrayMenuItem(TrayMenuItemKind.Command, commandId, text, isChecked, Command: command);
    }

    private static TrayMenuItem Separator() => new(TrayMenuItemKind.Separator);
}
