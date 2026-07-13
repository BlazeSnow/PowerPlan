using PowerPlan.Models;

namespace PowerPlan.Services;

public interface ISettingsService
{
    AppSettings Current { get; }

    event EventHandler<AppSettings>? SettingsChanged;

    Task InitializeAsync();

    Task SaveAsync(AppSettings settings);

    Task SaveCurrentAsync();

    string ResolveLanguage(string? language);
}
