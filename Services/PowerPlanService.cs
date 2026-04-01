using System.Diagnostics;
using System.Text.RegularExpressions;
using PowerPlan.Models;

namespace PowerPlan.Services;

public sealed class PowerPlanService
{
    // Microsoft documented GUID for "Ultimate Performance".
    public const string UltimatePerformanceGuid = "e9a42b02-d5df-448d-aa00-03f14749eb61";

    private static readonly Regex PlanLineRegex = new(
        @"Power Scheme GUID:\s*(?<guid>[a-fA-F0-9\-]+)\s*\((?<name>.+?)\)\s*(?<active>\*)?",
        RegexOptions.Compiled);

    public async Task<IReadOnlyList<PowerPlanInfo>> GetPlansAsync()
    {
        var output = await RunPowerCfgAsync("/list");
        var plans = new List<PowerPlanInfo>();

        foreach (var line in output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
        {
            var match = PlanLineRegex.Match(line.Trim());
            if (!match.Success)
            {
                continue;
            }

            plans.Add(new PowerPlanInfo
            {
                Guid = match.Groups["guid"].Value,
                Name = match.Groups["name"].Value,
                IsActive = match.Groups["active"].Success
            });
        }

        return plans;
    }

    public async Task SetActivePlanAsync(string planGuid)
    {
        await RunPowerCfgAsync($"/setactive {planGuid}");
    }

    public async Task<bool> HasUltimatePerformancePlanAsync()
    {
        var plans = await GetPlansAsync();
        return plans.Any(static p => p.Guid.Equals(UltimatePerformanceGuid, StringComparison.OrdinalIgnoreCase));
    }

    public async Task CreateUltimatePerformancePlanAsync()
    {
        await RunPowerCfgAsync($"/duplicatescheme {UltimatePerformanceGuid}");
    }

    private static async Task<string> RunPowerCfgAsync(string args)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "powercfg",
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };
        _ = process.Start();

        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            var error = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            throw new InvalidOperationException($"powercfg failed ({process.ExitCode}): {error.Trim()}");
        }

        return stdout;
    }
}
