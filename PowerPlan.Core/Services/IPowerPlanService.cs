using PowerPlan.Models;

namespace PowerPlan.Services;

public interface IPowerPlanService
{
    Task<IReadOnlyList<PowerPlanInfo>> GetPlansAsync(bool forceRefresh = false);

    Task SetActivePlanAsync(string planGuid);

    Task<string> CopyPlanAsync(string sourcePlanGuid, string newName);

    Task<string> CreateUltimatePerformancePlanAsync();

    Task RestoreDefaultSchemesAsync();

    bool IsUltimatePerformancePlan(PowerPlanInfo plan);
}
