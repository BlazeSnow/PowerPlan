namespace PowerPlan.Tray;

public interface ITrayTextProvider
{
    string Get(string key);

    string Format(string key, params object[] arguments);
}
