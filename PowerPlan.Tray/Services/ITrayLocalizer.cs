namespace PowerPlan.Tray.Services;

public interface ITrayLocalizer
{
    string Get(string key);

    string Format(string key, params object[] arguments);
}
