using System.Diagnostics;
using System.Security.Principal;

namespace PowerPlan.Services;

public static class PrivilegeService
{
    public static bool IsRunningAsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static bool TryRestartAsAdministrator()
    {
        try
        {
            var processPath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(processPath))
            {
                return false;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = processPath,
                UseShellExecute = true,
                Verb = "runas"
            };

            _ = Process.Start(startInfo);
            return true;
        }
        catch
        {
            return false;
        }
    }
}