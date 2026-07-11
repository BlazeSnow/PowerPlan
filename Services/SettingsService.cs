using PowerPlan.Models;
using System.Text.Json;
using Windows.ApplicationModel;
using Windows.Storage;

namespace PowerPlan.Services;

public sealed class SettingsService
{
    private readonly string _settingsPath;
    private readonly string _fallbackPath;
    private readonly ApplicationDataContainer _localSettings = ApplicationData.Current.LocalSettings;

    public SettingsService()
    {
        _settingsPath = ResolvePrimaryPath();
        _fallbackPath = ResolveFallbackPath();
    }

    public AppSettings Current { get; private set; } = new();

    public event EventHandler<AppSettings>? SettingsChanged;

    public async Task InitializeAsync()
    {
        Current = await LoadAsync();
    }

    public async Task<AppSettings> LoadAsync()
    {
        var loaded = LoadFromLocalSettings();
        if (loaded is not null)
        {
            return loaded;
        }

        loaded = await LoadFromPathAsync(_settingsPath);
        if (loaded is not null)
        {
            await SaveAsync(loaded);
            MarkJsonSettingsMigrated(_settingsPath);
            return loaded;
        }

        loaded = await LoadFromPathAsync(_fallbackPath);
        if (loaded is not null)
        {
            await SaveAsync(loaded);
            MarkJsonSettingsMigrated(_fallbackPath);
            return loaded;
        }

        var defaults = new AppSettings();
        try
        {
            await SaveAsync(defaults);
        }
        catch
        {
            // Keep defaults in memory if writing file is not available at startup.
        }

        return defaults;
    }

    public async Task SaveAsync(AppSettings settings)
    {
        settings.Language = NormalizeLanguage(settings.Language);
        SaveToLocalSettings(settings);
        Current = settings;
        SettingsChanged?.Invoke(this, Current);
        await Task.CompletedTask;
    }

    public async Task SaveCurrentAsync()
    {
        await SaveAsync(Current);
    }

    public string GetSettingsPath() => _settingsPath;

    public static string LoadLanguageSynchronously()
    {
        try
        {
            var values = ApplicationData.Current.LocalSettings.Values;
            return NormalizeLanguage(values.TryGetValue(LanguageKey, out var value) ? value as string : null);
        }
        catch
        {
            return DefaultLanguage;
        }
    }

    public static string NormalizeLanguage(string? language) =>
        string.Equals(language, EnglishLanguage, StringComparison.OrdinalIgnoreCase)
            ? EnglishLanguage
            : DefaultLanguage;

    private AppSettings? LoadFromLocalSettings()
    {
        var values = _localSettings.Values;
        if (!values.ContainsKey(AutoStartKey)
            && !values.ContainsKey(TrayEnabledKey)
            && !values.ContainsKey(LanguageKey)
            && !values.ContainsKey(UltimatePerformancePlanGuidKey))
        {
            return null;
        }

        return new AppSettings
        {
            AutoStart = GetLocalSetting(AutoStartKey, defaultValue: false),
            TrayEnabled = GetLocalSetting(TrayEnabledKey, defaultValue: true),
            Language = NormalizeLanguage(GetLocalSetting(LanguageKey, DefaultLanguage)),
            UltimatePerformancePlanGuid = GetLocalSetting(UltimatePerformancePlanGuidKey, string.Empty)
        };
    }

    private void SaveToLocalSettings(AppSettings settings)
    {
        var values = _localSettings.Values;
        values[AutoStartKey] = settings.AutoStart;
        values[TrayEnabledKey] = settings.TrayEnabled;
        values[UltimatePerformancePlanGuidKey] = settings.UltimatePerformancePlanGuid;
        values[LanguageKey] = NormalizeLanguage(settings.Language);
    }

    private bool GetLocalSetting(string key, bool defaultValue)
    {
        return _localSettings.Values.TryGetValue(key, out var value) && value is bool boolValue
            ? boolValue
            : defaultValue;
    }

    private string GetLocalSetting(string key, string defaultValue)
    {
        return _localSettings.Values.TryGetValue(key, out var value) && value is string stringValue
            ? stringValue
            : defaultValue;
    }

    private static async Task<AppSettings?> LoadFromPathAsync(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(path);
            return JsonSerializer.Deserialize(json, AppSettingsJsonContext.Default.AppSettings);
        }
        catch
        {
            return null;
        }
    }

    private static void MarkJsonSettingsMigrated(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        var migratedPath = path + MigratedSuffix;
        try
        {
            if (File.Exists(migratedPath))
            {
                File.Delete(migratedPath);
            }

            File.Move(path, migratedPath);
        }
        catch
        {
            // Migration to LocalSettings already succeeded; keep the JSON file if it cannot be renamed.
        }
    }

    private static string ResolvePrimaryPath()
    {
        if (IsPackaged())
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var packageFamilyName = Package.Current.Id.FamilyName;
            return Path.Combine(localAppData, "Packages", packageFamilyName, "LocalState", "settings.json");
        }

        return ResolveFallbackPath();
    }

    private static string ResolveFallbackPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "PowerPlan", "settings.json");
    }

    private static bool IsPackaged()
    {
        try
        {
            _ = Package.Current;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public const string DefaultLanguage = "zh-CN";
    public const string EnglishLanguage = "en-US";

    private const string AutoStartKey = "AutoStartEnabled";
    private const string TrayEnabledKey = "TrayEnabled";
    private const string LanguageKey = "Language";
    private const string UltimatePerformancePlanGuidKey = "UltimatePerformancePlanGuid";
    private const string MigratedSuffix = ".migrated";
}
