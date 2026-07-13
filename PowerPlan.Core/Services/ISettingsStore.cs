namespace PowerPlan.Services;

public interface ISettingsStore
{
    bool Contains(string key);

    bool GetBoolean(string key, bool defaultValue);

    string GetString(string key, string defaultValue);

    void SetBoolean(string key, bool value);

    void SetString(string key, string value);
}
