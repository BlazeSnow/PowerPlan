using PowerPlan.Models;
using PowerPlan.Services;

namespace PowerPlan.Pages;

public interface IPageHost
{
    IPowerPlanService PowerPlanService { get; }

    ISettingsService SettingsService { get; }

    IStartupTaskService StartupTaskService { get; }

    string GetString(string key);

    string FormatString(string key, params object[] arguments);

    string GetStringForLanguage(string key, string language);

    void UpdateTrayPlans(IReadOnlyList<PowerPlanInfo> plans);

    Task RefreshTrayPlansAsync(bool forceRefresh);
}
