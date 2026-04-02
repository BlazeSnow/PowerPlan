using System.ComponentModel;
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
        return TryRestartAsAdministrator(out _);
    }

    public static bool TryRestartAsAdministrator(out string? error)
    {
        error = null;

        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath))
        {
            error = "无法获取当前程序路径。";
            return false;
        }

        if (TryStartElevated(processPath, out error))
        {
            return true;
        }

        // Fallback path for some shell environments.
        var escapedPath = processPath.Replace("'", "''", StringComparison.Ordinal);
        var fallbackCommand = $"Start-Process -FilePath '{escapedPath}' -Verb RunAs";
        return TryStartElevated(
            "powershell.exe",
            $"-NoProfile -ExecutionPolicy Bypass -Command \"{fallbackCommand}\"",
            out error);
    }

    private static bool TryStartElevated(string fileName, out string? error)
    {
        return TryStartElevated(fileName, null, out error);
    }

    private static bool TryStartElevated(string fileName, string? args, out string? error)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = AppContext.BaseDirectory
            };

            if (!string.IsNullOrWhiteSpace(args))
            {
                startInfo.Arguments = args;
            }

            var process = Process.Start(startInfo);
            if (process is null)
            {
                error = "提权启动未返回进程句柄。";
                return false;
            }

            error = null;
            return true;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            error = "你取消了 UAC 提权请求。";
            return false;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
