namespace PowerPlan.Services;

public interface IStartupTaskService
{
    bool IsSupported { get; }

    Task<bool> SetEnabledAsync(bool enabled);

    Task<bool> GetEffectiveEnabledAsync();
}
