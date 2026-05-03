using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using PowerPlan.Models;

namespace PowerPlan.Services;

public sealed class PowerPlanService
{
    public const string UltimatePerformanceGuid = "e9a42b02-d5df-448d-aa00-03f14749eb61";
    private static readonly TimeSpan PlansCacheDuration = TimeSpan.FromMilliseconds(750);

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

    public Task SetActivePlanAsync(string planGuid)
    {
        var guid = ParsePowerSchemeGuid(planGuid, "电源计划 GUID 无效。");
        ThrowIfFailed(PowerSetActiveScheme(IntPtr.Zero, ref guid), "切换电源计划失败");
        InvalidatePlansCache();
        return Task.CompletedTask;
    }

    public Task<string> CopyPlanAsync(string sourcePlanGuid, string newName)
    {
        var sourceGuid = ParsePowerSchemeGuid(sourcePlanGuid, "源电源计划 GUID 无效。");
        var newPlanGuid = DuplicatePowerScheme(sourceGuid);
        WritePowerSchemeName(newPlanGuid, newName.Trim());
        InvalidatePlansCache();
        return Task.FromResult(newPlanGuid.ToString("D"));
    }

    public Task<string> CreateUltimatePerformancePlanAsync()
    {
        var ultimatePerformanceGuid = Guid.Parse(UltimatePerformanceGuid);
        var createdGuid = DuplicatePowerScheme(ultimatePerformanceGuid);
        InvalidatePlansCache();
        return Task.FromResult(createdGuid.ToString("D"));
    }

    public Task RestoreDefaultSchemesAsync()
    {
        ThrowIfFailed(PowerRestoreDefaultPowerSchemes(), "还原默认电源计划失败");
        InvalidatePlansCache();
        return Task.CompletedTask;
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

    private static async Task<IReadOnlyList<PowerPlanInfo>> FetchPlansCoreAsync(long fetchVersion)
    {
        try
        {
            var plans = await Task.Run(ReadPowerSchemes);

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

    private static IReadOnlyList<PowerPlanInfo> ReadPowerSchemes()
    {
        var activeGuid = GetActivePowerSchemeGuid();
        var plans = new List<PowerPlanInfo>();

        for (uint index = 0; ; index++)
        {
            var bufferSize = GuidSize;
            var buffer = new byte[GuidSize];
            var result = PowerEnumerate(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, AccessScheme, index, buffer, ref bufferSize);
            if (result == ErrorNoMoreItems)
            {
                break;
            }

            ThrowIfFailed(result, "枚举电源计划失败");

            var guid = new Guid(buffer);
            var guidText = guid.ToString("D");
            var name = ReadPowerSchemeName(guid);
            plans.Add(new PowerPlanInfo
            {
                Guid = guidText,
                Name = string.IsNullOrWhiteSpace(name) ? guidText : name,
                IsActive = guid.Equals(activeGuid)
            });
        }

        return plans;
    }

    private static Guid GetActivePowerSchemeGuid()
    {
        var result = PowerGetActiveScheme(IntPtr.Zero, out var activeGuidPointer);
        ThrowIfFailed(result, "读取当前电源计划失败");

        try
        {
            return Marshal.PtrToStructure<Guid>(activeGuidPointer);
        }
        finally
        {
            if (activeGuidPointer != IntPtr.Zero)
            {
                _ = LocalFree(activeGuidPointer);
            }
        }
    }

    private static Guid DuplicatePowerScheme(Guid sourceGuid)
    {
        var result = PowerDuplicateScheme(IntPtr.Zero, ref sourceGuid, out var destinationGuidPointer);
        ThrowIfFailed(result, "复制电源计划失败");

        try
        {
            if (destinationGuidPointer == IntPtr.Zero)
            {
                throw new InvalidOperationException("复制电源计划失败：系统未返回新计划 GUID。");
            }

            return Marshal.PtrToStructure<Guid>(destinationGuidPointer);
        }
        finally
        {
            if (destinationGuidPointer != IntPtr.Zero)
            {
                _ = LocalFree(destinationGuidPointer);
            }
        }
    }

    private static string ReadPowerSchemeName(Guid schemeGuid)
    {
        uint bufferSize = 0;
        var result = PowerReadFriendlyName(IntPtr.Zero, ref schemeGuid, IntPtr.Zero, IntPtr.Zero, null, ref bufferSize);
        if (result is not ErrorSuccess and not ErrorMoreData)
        {
            ThrowIfFailed(result, "读取电源计划名称失败");
        }

        if (bufferSize == 0)
        {
            return string.Empty;
        }

        var buffer = new byte[bufferSize];
        result = PowerReadFriendlyName(IntPtr.Zero, ref schemeGuid, IntPtr.Zero, IntPtr.Zero, buffer, ref bufferSize);
        ThrowIfFailed(result, "读取电源计划名称失败");

        return Encoding.Unicode.GetString(buffer).TrimEnd('\0');
    }

    private static void WritePowerSchemeName(Guid schemeGuid, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("电源计划名称不能为空。");
        }

        var buffer = Encoding.Unicode.GetBytes(name + '\0');
        ThrowIfFailed(
            PowerWriteFriendlyName(IntPtr.Zero, ref schemeGuid, IntPtr.Zero, IntPtr.Zero, buffer, (uint)buffer.Length),
            "写入电源计划名称失败");
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

    private static Guid ParsePowerSchemeGuid(string value, string errorMessage)
    {
        if (Guid.TryParse(value, out var guid))
        {
            return guid;
        }

        throw new InvalidOperationException(errorMessage);
    }

    private static void ThrowIfFailed(uint result, string message)
    {
        if (result == ErrorSuccess)
        {
            return;
        }

        throw new Win32Exception((int)result, $"{message}：{new Win32Exception((int)result).Message}");
    }

    private const uint ErrorSuccess = 0;
    private const uint ErrorMoreData = 234;
    private const uint ErrorNoMoreItems = 259;
    private const uint AccessScheme = 16;
    private const uint GuidSize = 16;

    [DllImport("powrprof.dll")]
    private static extern uint PowerEnumerate(
        IntPtr rootPowerKey,
        IntPtr schemeGuid,
        IntPtr subGroupOfPowerSettingsGuid,
        uint accessFlags,
        uint index,
        [Out] byte[] buffer,
        ref uint bufferSize);

    [DllImport("powrprof.dll")]
    private static extern uint PowerGetActiveScheme(IntPtr rootPowerKey, out IntPtr activePolicyGuid);

    [DllImport("powrprof.dll")]
    private static extern uint PowerSetActiveScheme(IntPtr rootPowerKey, ref Guid schemeGuid);

    [DllImport("powrprof.dll")]
    private static extern uint PowerDuplicateScheme(IntPtr rootPowerKey, ref Guid sourceSchemeGuid, out IntPtr destinationSchemeGuid);

    [DllImport("powrprof.dll")]
    private static extern uint PowerReadFriendlyName(
        IntPtr rootPowerKey,
        ref Guid schemeGuid,
        IntPtr subGroupOfPowerSettingsGuid,
        IntPtr powerSettingGuid,
        [Out] byte[]? buffer,
        ref uint bufferSize);

    [DllImport("powrprof.dll")]
    private static extern uint PowerWriteFriendlyName(
        IntPtr rootPowerKey,
        ref Guid schemeGuid,
        IntPtr subGroupOfPowerSettingsGuid,
        IntPtr powerSettingGuid,
        byte[] buffer,
        uint bufferSize);

    [DllImport("powrprof.dll")]
    private static extern uint PowerRestoreDefaultPowerSchemes();

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr hMem);
}
