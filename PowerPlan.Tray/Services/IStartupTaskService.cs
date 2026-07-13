namespace PowerPlan.Tray.Services;

public interface IStartupTaskService
{
    Task<bool> SetEnabledAsync(bool enabled);
}
