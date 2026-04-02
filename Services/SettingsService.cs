using System.Text.Json;
using PowerPlan.Models;
using Windows.ApplicationModel;

namespace PowerPlan.Services;

public sealed class SettingsService
{
    private string _settingsPath;
    private readonly string _fallbackPath;

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
        var loaded = await LoadFromPathAsync(_settingsPath);
        if (loaded is not null)
        {
            return loaded;
        }

        if (!_settingsPath.Equals(_fallbackPath, StringComparison.OrdinalIgnoreCase))
        {
            loaded = await LoadFromPathAsync(_fallbackPath);
            if (loaded is not null)
            {
                _settingsPath = _fallbackPath;
                return loaded;
            }
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
        var json = JsonSerializer.Serialize(settings, AppSettingsJsonContext.Default.AppSettings);

        try
        {
            await WriteToPathAsync(_settingsPath, json);
        }
        catch
        {
            if (!_settingsPath.Equals(_fallbackPath, StringComparison.OrdinalIgnoreCase))
            {
                await WriteToPathAsync(_fallbackPath, json);
                _settingsPath = _fallbackPath;
            }
            else
            {
                throw;
            }
        }

        Current = settings;
        SettingsChanged?.Invoke(this, Current);
    }

    public async Task SaveCurrentAsync()
    {
        await SaveAsync(Current);
    }

    public string GetSettingsPath() => _settingsPath;

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

    private static async Task WriteToPathAsync(string path, string json)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(path, json);
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
}