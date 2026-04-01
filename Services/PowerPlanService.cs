using System.Diagnostics;
using System.Text.RegularExpressions;
using PowerPlan.Models;

namespace PowerPlan.Services;

public sealed class PowerPlanService
{
    // Microsoft documented GUID for "Ultimate Performance".
    public const string UltimatePerformanceGuid = "e9a42b02-d5df-448d-aa00-03f14749eb61";

    private static readonly Regex GuidRegex = new(
        @"(?<guid>[a-fA-F0-9]{8}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-[a-fA-F0-9]{12})",
        RegexOptions.Compiled);

    private static readonly string[] UltimatePlanNameKeywords =
    {
        "Ultimate Performance",
        "\u5353\u8D8A\u6027\u80FD"
    };

    public async Task<IReadOnlyList<PowerPlanInfo>> GetPlansAsync()
    {
        var output = await RunPowerCfgAsync("/list");
        var activeGuid = await GetActivePlanGuidAsync();
        var plans = new List<PowerPlanInfo>();

        foreach (var line in output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            var match = GuidRegex.Match(trimmed);
            if (!match.Success)
            {
                continue;
            }

            var guid = match.Groups["guid"].Value;
            var name = ExtractPlanName(trimmed);

            plans.Add(new PowerPlanInfo
            {
                Guid = guid,
                Name = string.IsNullOrWhiteSpace(name) ? guid : name,
                IsActive = guid.Equals(activeGuid, StringComparison.OrdinalIgnoreCase)
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
        return plans.Any(IsUltimatePerformancePlan);
    }

    public async Task CreateUltimatePerformancePlanAsync()
    {
        await RunPowerCfgAsync($"/duplicatescheme {UltimatePerformanceGuid}");
    }

    public bool IsUltimatePerformancePlan(PowerPlanInfo plan)
    {
        if (plan.Guid.Equals(UltimatePerformanceGuid, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        foreach (var keyword in UltimatePlanNameKeywords)
        {
            if (plan.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private async Task<string?> GetActivePlanGuidAsync()
    {
        var output = await RunPowerCfgAsync("/getactivescheme");
        var match = GuidRegex.Match(output);
        return match.Success ? match.Groups["guid"].Value : null;
    }

    private static string ExtractPlanName(string line)
    {
        var start = line.IndexOf('(');
        if (start < 0)
        {
            return string.Empty;
        }

        var end = line.IndexOf(')', start + 1);
        if (end <= start)
        {
            return string.Empty;
        }

        return line[(start + 1)..end].Trim();
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