using PowerPlan.Models;

namespace PowerPlan.Services;

public interface ILegacySettingsStore
{
    Task<AppSettings?> LoadPrimaryAsync();

    Task<AppSettings?> LoadFallbackAsync();

    void MarkPrimaryMigrated();

    void MarkFallbackMigrated();
}
