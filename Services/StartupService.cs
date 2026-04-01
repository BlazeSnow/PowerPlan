using Microsoft.Win32;

namespace PowerPlan.Services;

public sealed class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppValueName = "PowerPlan";

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(AppValueName) is string;
    }

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("Cannot open startup registry key.");

        if (enabled)
        {
            var processPath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(processPath))
            {
                throw new InvalidOperationException("Cannot locate application executable path.");
            }

            key.SetValue(AppValueName, $"\"{processPath}\"");
            return;
        }

        if (key.GetValue(AppValueName) is not null)
        {
            key.DeleteValue(AppValueName, throwOnMissingValue: false);
        }
    }
}