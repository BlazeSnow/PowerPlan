using Windows.Storage;

namespace PowerPlan.Services;

public sealed class WindowsSettingsStore : ISettingsStore
{
    private readonly ApplicationDataContainer _localSettings = ApplicationData.Current.LocalSettings;

    public bool Contains(string key)
    {
        return _localSettings.Values.ContainsKey(key);
    }

    public bool GetBoolean(string key, bool defaultValue)
    {
        return _localSettings.Values.TryGetValue(key, out var value) && value is bool boolValue
            ? boolValue
            : defaultValue;
    }

    public string GetString(string key, string defaultValue)
    {
        return _localSettings.Values.TryGetValue(key, out var value) && value is string stringValue
            ? stringValue
            : defaultValue;
    }

    public void SetBoolean(string key, bool value)
    {
        _localSettings.Values[key] = value;
    }

    public void SetString(string key, string value)
    {
        _localSettings.Values[key] = value;
    }
}
