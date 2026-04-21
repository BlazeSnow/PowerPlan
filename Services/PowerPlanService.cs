using System.Diagnostics;
using System.Text.RegularExpressions;
using PowerPlan.Models;

namespace PowerPlan.Services;

public sealed class PowerPlanService
{
    public const string UltimatePerformanceGuid = "e9a42b02-d5df-448d-aa00-03f14749eb61";
    private static readonly TimeSpan PlansCacheDuration = TimeSpan.FromMilliseconds(750);

    private static readonly Regex GuidRegex = new(
        @"(?<guid>[a-fA-F0-9]{8}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-[a-fA-F0-9]{12})",
        RegexOptions.Compiled);

    private static readonly string[] UltimatePlanNameKeywords =
    {
        "Ultimate Performance",
        LocalizationService.Get("PowerPlan.UltimateKeywordZh")
    };

    private static readonly object PlansCacheLock = new();
    private static Task<IReadOnlyList<PowerPlanInfo>>? _plansFetchTask;
    private static long _plansFetchTaskVersion = -1;
    private static IReadOnlyList<PowerPlanInfo>? _cachedPlans;
    private static DateTimeOffset _cachedPlansAt;
    private static long _plansCacheVersion;

    public async Task<IReadOnlyList<PowerPlanInfo>> GetPlansAsync(bool forceRefresh = false)
    {
        Task<IReadOnlyList<PowerPlanInfo>> fetchTask;
        long fetchVersion;

        lock (PlansCacheLock)
        {
            if (forceRefresh)
            {
                _cachedPlans = null;
                _cachedPlansAt = default;
                _plansFetchTask = null;
                _plansCacheVersion++;
            }
            else if (_cachedPlans is not null && DateTimeOffset.UtcNow - _cachedPlansAt <= PlansCacheDuration)
            {
                return ClonePlans(_cachedPlans);
            }

            fetchVersion = _plansCacheVersion;
            if (_plansFetchTask is null)
            {
                _plansFetchTask = FetchPlansCoreAsync(fetchVersion);
                _plansFetchTaskVersion = fetchVersion;
            }
            fetchTask = _plansFetchTask;
        }

        var plans = await fetchTask;
        return ClonePlans(plans);
    }

    public async Task SetActivePlanAsync(string planGuid)
    {
        await RunPowerCfgAsync($"/setactive {planGuid}");
        InvalidatePlansCache();
    }

    public async Task<string> CopyPlanAsync(string sourcePlanGuid, string newName)
    {
        var duplicateOutput = await RunPowerCfgAsync($"/duplicatescheme {sourcePlanGuid}");
        var guidMatch = GuidRegex.Match(duplicateOutput);
        if (!guidMatch.Success)
        {
            throw new InvalidOperationException("复制失败：无法获取新计划 GUID。");
        }

        var newPlanGuid = guidMatch.Groups["guid"].Value;
        var safeName = newName.Trim().Replace("\"", "'");
        await RunPowerCfgAsync($"/changename {newPlanGuid} \"{safeName}\"");
        InvalidatePlansCache();
        return newPlanGuid;
    }

    public async Task<string> CreateUltimatePerformancePlanAsync()
    {
        var duplicateOutput = await RunPowerCfgAsync($"/duplicatescheme {UltimatePerformanceGuid}");
        var guidMatch = GuidRegex.Match(duplicateOutput);
        if (!guidMatch.Success)
        {
            throw new InvalidOperationException("创建失败：无法获取卓越性能计划 GUID。");
        }

        InvalidatePlansCache();
        return guidMatch.Groups["guid"].Value;
    }

    public async Task RestoreDefaultSchemesAsync()
    {
        await RunPowerCfgAsync("/restoredefaultschemes");
        InvalidatePlansCache();
    }

    public bool IsUltimatePerformancePlan(PowerPlanInfo plan)
    {
        if (plan.Guid.Equals(UltimatePerformanceGuid, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        foreach (var keyword in UltimatePlanNameKeywords)
        {
            if (!string.IsNullOrWhiteSpace(keyword) && plan.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
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


    private static bool IsActivePlanLine(string line)
    {
        return line.Contains('*');
    }

    private static async Task<IReadOnlyList<PowerPlanInfo>> FetchPlansCoreAsync(long fetchVersion)
    {
        try
        {
            var output = await RunPowerCfgAsync("/list");
            var plans = ParsePlans(output);

            lock (PlansCacheLock)
            {
                if (fetchVersion == _plansCacheVersion)
                {
                    _cachedPlans = plans;
                    _cachedPlansAt = DateTimeOffset.UtcNow;
                }
            }

            return plans;
        }
        finally
        {
            lock (PlansCacheLock)
            {
                if (_plansFetchTaskVersion == fetchVersion)
                {
                    _plansFetchTask = null;
                    _plansFetchTaskVersion = -1;
                }
            }
        }
    }

    private static IReadOnlyList<PowerPlanInfo> ParsePlans(string output)
    {
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
                IsActive = IsActivePlanLine(trimmed)
            });
        }

        return plans;
    }

    private static IReadOnlyList<PowerPlanInfo> ClonePlans(IReadOnlyList<PowerPlanInfo> source)
    {
        return source
            .Select(plan => new PowerPlanInfo
            {
                Guid = plan.Guid,
                Name = plan.Name,
                IsActive = plan.IsActive
            })
            .ToArray();
    }

    private static void InvalidatePlansCache()
    {
        lock (PlansCacheLock)
        {
            _cachedPlans = null;
            _cachedPlansAt = default;
            _plansFetchTask = null;
            _plansFetchTaskVersion = -1;
            _plansCacheVersion++;
        }
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
