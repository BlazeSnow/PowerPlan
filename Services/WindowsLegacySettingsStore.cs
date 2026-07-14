using PowerPlan.Models;
using System.Text.Json;
using Windows.ApplicationModel;

namespace PowerPlan.Services;

public sealed class WindowsLegacySettingsStore : ILegacySettingsStore
{
    private const string MigratedSuffix = ".migrated";
    private readonly string _primaryPath;
    private readonly string _fallbackPath;

    public WindowsLegacySettingsStore()
    {
        _primaryPath = ResolvePrimaryPath();
        _fallbackPath = ResolveFallbackPath();
    }

    public Task<AppSettings?> LoadPrimaryAsync()
    {
        return LoadFromPathAsync(_primaryPath);
    }

    public Task<AppSettings?> LoadFallbackAsync()
    {
        return LoadFromPathAsync(_fallbackPath);
    }

    public void MarkPrimaryMigrated()
    {
        MarkMigrated(_primaryPath);
    }

    public void MarkFallbackMigrated()
    {
        MarkMigrated(_fallbackPath);
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

    private static void MarkMigrated(string path)
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
}
