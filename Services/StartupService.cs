using Microsoft.Win32;
using Windows.ApplicationModel;

namespace PowerPlan.Services;

public sealed class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppValueName = "PowerPlan";
    private const string StartupTaskId = "PowerPlanStartupTask";
    public const string TrayStartupArgument = "--tray-startup";

    public async Task<bool> IsEnabledAsync()
    {
        if (IsPackaged())
        {
            var startupTask = await StartupTask.GetAsync(StartupTaskId);
            return startupTask.State is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
        }

        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(AppValueName) is string;
    }

    public async Task<bool> SetEnabledAsync(bool enabled, bool startInTray)
    {
        if (IsPackaged())
        {
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

        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("Cannot open startup registry key.");

        if (enabled)
        {
            var processPath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(processPath))
            {
                throw new InvalidOperationException("Cannot locate application executable path.");
            }

            var command = startInTray
                ? $"\"{processPath}\" {TrayStartupArgument}"
                : $"\"{processPath}\"";
            key.SetValue(AppValueName, command);
            return true;
        }

        if (key.GetValue(AppValueName) is not null)
        {
            key.DeleteValue(AppValueName, throwOnMissingValue: false);
        }

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
