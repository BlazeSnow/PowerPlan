using Windows.ApplicationModel;

namespace PowerPlan.Services;

public sealed class StartupService
{
    private const string StartupTaskId = "PowerPlanStartupTask";

    public async Task<bool> IsEnabledAsync()
    {
        if (!IsPackaged())
        {
            return false;
        }

        var startupTask = await StartupTask.GetAsync(StartupTaskId);
        return startupTask.State is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
    }

    public async Task<bool> SetEnabledAsync(bool enabled)
    {
        if (!IsPackaged())
        {
            return false;
        }

        var startupTask = await StartupTask.GetAsync(StartupTaskId);
        if (enabled)
        {
            if (startupTask.State is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy)
            {
                return true;
            }

            var state = await startupTask.RequestEnableAsync();
            return state is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
        }

        startupTask.Disable();
        return false;
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
