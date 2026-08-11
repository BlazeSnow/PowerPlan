using PowerPlan.Tray;

namespace PowerPlan.Tests.TestDoubles;

internal sealed class FakeTrayTextProvider : ITrayTextProvider
{
    public Dictionary<string, string> Text { get; } = new(StringComparer.Ordinal)
    {
        ["App.WindowTitle"] = "PowerPlan",
        ["Tray.Menu.OpenMainWindow"] = "Open",
        ["Tray.Menu.OpenHiddenUltimate"] = "Open hidden ultimate",
        ["Tray.Menu.RefreshPlans"] = "Refresh",
        ["Tray.Menu.EnableAutoStart"] = "Enable startup",
        ["Tray.Menu.DisableAutoStart"] = "Disable startup",
        ["Tray.Menu.Exit"] = "Exit",
        ["Tray.Tooltip.PlanUnavailable"] = "Plan unavailable",
        ["App.Status.On"] = "On",
        ["App.Status.Off"] = "Off"
    };

    public string Get(string key) => Text.GetValueOrDefault(key, key);

    public string Format(string key, params object[] arguments)
    {
        return key switch
        {
            "Tray.Tooltip.Plan" => $"Plan: {arguments[0]}",
            "Tray.Tooltip.AutoStart" => $"Startup: {arguments[0]}",
            _ => $"{key}: {string.Join(",", arguments)}"
        };
    }
}
