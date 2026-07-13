using PowerPlan.Tray.Services;

namespace PowerPlan.Services;

public sealed class TrayLocalizer : ITrayLocalizer
{
    public string Get(string key)
    {
        return LocalizationService.Get(key);
    }

    public string Format(string key, params object[] arguments)
    {
        return LocalizationService.Format(key, arguments);
    }
}
