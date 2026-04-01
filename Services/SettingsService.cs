using System.Text.Json;
using PowerPlan.Models;
using Windows.ApplicationModel;

namespace PowerPlan.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _settingsPath;

    public SettingsService()
    {
        _settingsPath = ResolveSettingsPath();
    }

    public AppSettings Current { get; private set; } = new();

    public event EventHandler<AppSettings>? SettingsChanged;

    public async Task InitializeAsync()
    {
        Current = await LoadAsync();
    }

    public async Task<AppSettings> LoadAsync()
    {
        if (!File.Exists(_settingsPath))
        {
            var defaults = new AppSettings();
            await SaveAsync(defaults);
            return defaults;
        }

        try
        {
            var json = await File.ReadAllTextAsync(_settingsPath);
            var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            return loaded ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public async Task SaveAsync(AppSettings settings)
    {
        var directory = Path.GetDirectoryName(_settingsPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(settings, JsonOptions);
        await File.WriteAllTextAsync(_settingsPath, json);

        Current = settings;
        SettingsChanged?.Invoke(this, Current);
    }

    public async Task SaveCurrentAsync()
    {
        await SaveAsync(Current);
    }

    public string GetSettingsPath() => _settingsPath;

    private static string ResolveSettingsPath()
    {
        if (IsPackaged())
        {
            return Path.Combine(AppContext.BaseDirectory, "settings.json");
        }

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
}