using PowerPlan.Models;

namespace PowerPlan.Services;

public sealed class SettingsService : ISettingsService
{
    private readonly ISettingsStore _settingsStore;
    private readonly ILegacySettingsStore _legacySettingsStore;
    private readonly ILanguagePreferenceProvider _languagePreferenceProvider;

    public SettingsService(
        ISettingsStore settingsStore,
        ILegacySettingsStore legacySettingsStore,
        ILanguagePreferenceProvider languagePreferenceProvider)
    {
        _settingsStore = settingsStore;
        _legacySettingsStore = legacySettingsStore;
        _languagePreferenceProvider = languagePreferenceProvider;
    }

    public AppSettings Current { get; private set; } = new();

    public event EventHandler<AppSettings>? SettingsChanged;

    public async Task InitializeAsync()
    {
        Current = await LoadAsync();
    }

    public async Task<AppSettings> LoadAsync()
    {
        var loaded = LoadFromSettingsStore();
        if (loaded is not null)
        {
            return loaded;
        }

        loaded = await _legacySettingsStore.LoadPrimaryAsync();
        if (loaded is not null)
        {
            await SaveAsync(loaded);
            _legacySettingsStore.MarkPrimaryMigrated();
            return loaded;
        }

        loaded = await _legacySettingsStore.LoadFallbackAsync();
        if (loaded is not null)
        {
            await SaveAsync(loaded);
            _legacySettingsStore.MarkFallbackMigrated();
            return loaded;
        }

        var defaults = new AppSettings();
        try
        {
            await SaveAsync(defaults);
        }
        catch
        {
            // Keep defaults in memory if settings storage is not available at startup.
        }

        return defaults;
    }

    public Task SaveAsync(AppSettings settings)
    {
        settings.Language = NormalizeLanguage(settings.Language);
        SaveToSettingsStore(settings);
        Current = settings;
        SettingsChanged?.Invoke(this, Current);
        return Task.CompletedTask;
    }

    public Task SaveCurrentAsync()
    {
        return SaveAsync(Current);
    }

    public string NormalizeLanguage(string? language)
    {
        return LanguageSettings.Normalize(language);
    }

    public string ResolveLanguage(string? language)
    {
        try
        {
            return LanguageSettings.Resolve(language, _languagePreferenceProvider.GetPreferredLanguage());
        }
        catch
        {
            return LanguageSettings.EnglishLanguage;
        }
    }

    private AppSettings? LoadFromSettingsStore()
    {
        if (!_settingsStore.Contains(AutoStartKey)
            && !_settingsStore.Contains(TrayEnabledKey)
            && !_settingsStore.Contains(LaunchToTrayKey)
            && !_settingsStore.Contains(LanguageKey)
            && !_settingsStore.Contains(UltimatePerformancePlanGuidKey))
        {
            return null;
        }

        return new AppSettings
        {
            AutoStart = _settingsStore.GetBoolean(AutoStartKey, defaultValue: false),
            TrayEnabled = _settingsStore.GetBoolean(TrayEnabledKey, defaultValue: true),
            LaunchToTray = _settingsStore.GetBoolean(LaunchToTrayKey, defaultValue: false),
            Language = NormalizeLanguage(_settingsStore.GetString(LanguageKey, LanguageSettings.DefaultLanguage)),
            UltimatePerformancePlanGuid = _settingsStore.GetString(UltimatePerformancePlanGuidKey, string.Empty)
        };
    }

    private void SaveToSettingsStore(AppSettings settings)
    {
        _settingsStore.SetBoolean(AutoStartKey, settings.AutoStart);
        _settingsStore.SetBoolean(TrayEnabledKey, settings.TrayEnabled);
        _settingsStore.SetBoolean(LaunchToTrayKey, settings.LaunchToTray);
        _settingsStore.SetString(UltimatePerformancePlanGuidKey, settings.UltimatePerformancePlanGuid);
        _settingsStore.SetString(LanguageKey, NormalizeLanguage(settings.Language));
    }

    public const string AutoLanguage = LanguageSettings.AutoLanguage;
    public const string ChineseLanguage = LanguageSettings.ChineseLanguage;
    public const string EnglishLanguage = LanguageSettings.EnglishLanguage;
    public const string DefaultLanguage = LanguageSettings.DefaultLanguage;

    public const string AutoStartKey = "AutoStartEnabled";
    public const string TrayEnabledKey = "TrayEnabled";
    public const string LaunchToTrayKey = "LaunchToTrayEnabled";
    public const string LanguageKey = "Language";
    public const string UltimatePerformancePlanGuidKey = "UltimatePerformancePlanGuid";
}
