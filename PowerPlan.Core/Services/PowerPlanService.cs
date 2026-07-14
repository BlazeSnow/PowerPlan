using PowerPlan.Models;

namespace PowerPlan.Services;

public sealed class PowerPlanService : IPowerPlanService
{
    public const string UltimatePerformanceGuid = "e9a42b02-d5df-448d-aa00-03f14749eb61";
    private static readonly TimeSpan PlansCacheDuration = TimeSpan.FromMinutes(5);

    private readonly IPowerSchemeNativeApi _nativeApi;
    private readonly IPowerPlanErrorFormatter _errorFormatter;
    private readonly object _plansCacheLock = new();
    private Task<IReadOnlyList<PowerPlanInfo>>? _plansFetchTask;
    private long _plansFetchTaskVersion = -1;
    private IReadOnlyList<PowerPlanInfo>? _cachedPlans;
    private DateTimeOffset _cachedPlansAt;
    private long _plansCacheVersion;

    public PowerPlanService(IPowerSchemeNativeApi nativeApi, IPowerPlanErrorFormatter errorFormatter)
    {
        _nativeApi = nativeApi;
        _errorFormatter = errorFormatter;
    }

    public async Task<IReadOnlyList<PowerPlanInfo>> GetPlansAsync(bool forceRefresh = false)
    {
        Task<IReadOnlyList<PowerPlanInfo>> fetchTask;
        long fetchVersion;

        lock (_plansCacheLock)
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
                return _cachedPlans;
            }

            fetchVersion = _plansCacheVersion;
            if (_plansFetchTask is null)
            {
                _plansFetchTask = FetchPlansCoreAsync(fetchVersion);
                _plansFetchTaskVersion = fetchVersion;
            }

            fetchTask = _plansFetchTask;
        }

        return await fetchTask;
    }

    public Task SetActivePlanAsync(string planGuid)
    {
        var guid = ParsePowerSchemeGuid(planGuid, "PowerPlan.Error.InvalidPlanGuid");
        ThrowIfFailed(_nativeApi.SetActiveScheme(guid), "PowerPlan.Error.SetActiveFailed");
        InvalidatePlansCache();
        return Task.CompletedTask;
    }

    public Task<string> CopyPlanAsync(string sourcePlanGuid, string newName)
    {
        var sourceGuid = ParsePowerSchemeGuid(sourcePlanGuid, "PowerPlan.Error.InvalidSourcePlanGuid");
        var newPlanGuid = DuplicatePowerScheme(sourceGuid);
        var trimmedName = newName.Trim();
        if (string.IsNullOrWhiteSpace(trimmedName))
        {
            throw _errorFormatter.CreateEmptyNameException();
        }

        ThrowIfFailed(_nativeApi.WriteFriendlyName(newPlanGuid, trimmedName), "PowerPlan.Error.WriteNameFailed");
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
        ThrowIfFailed(_nativeApi.RestoreDefaultSchemes(), "PowerPlan.Error.RestoreDefaultsFailed");
        InvalidatePlansCache();
        return Task.CompletedTask;
    }

    public bool IsUltimatePerformancePlan(PowerPlanInfo plan)
    {
        return plan.Guid.Equals(UltimatePerformanceGuid, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<IReadOnlyList<PowerPlanInfo>> FetchPlansCoreAsync(long fetchVersion)
    {
        try
        {
            var plans = await Task.Run(ReadPowerSchemes);

            lock (_plansCacheLock)
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
            lock (_plansCacheLock)
            {
                if (_plansFetchTaskVersion == fetchVersion)
                {
                    _plansFetchTask = null;
                    _plansFetchTaskVersion = -1;
                }
            }
        }
    }

    private IReadOnlyList<PowerPlanInfo> ReadPowerSchemes()
    {
        ThrowIfFailed(_nativeApi.GetActiveScheme(out var activeGuid), "PowerPlan.Error.ReadActiveFailed");

        var plans = new List<PowerPlanInfo>();
        for (uint index = 0; ; index++)
        {
            var result = _nativeApi.EnumerateScheme(index, out var guid);
            if (result == ErrorNoMoreItems)
            {
                break;
            }

            ThrowIfFailed(result, "PowerPlan.Error.EnumerateFailed");
            ThrowIfFailed(_nativeApi.ReadFriendlyName(guid, out var name), "PowerPlan.Error.ReadNameFailed");

            var guidText = guid.ToString("D");
            plans.Add(new PowerPlanInfo
            {
                Guid = guidText,
                Name = string.IsNullOrWhiteSpace(name) ? guidText : name,
                IsActive = guid.Equals(activeGuid)
            });
        }

        return plans;
    }

    private Guid DuplicatePowerScheme(Guid sourceGuid)
    {
        var result = _nativeApi.DuplicateScheme(sourceGuid);
        ThrowIfFailed(result.Result, "PowerPlan.Error.DuplicateFailed");

        return result.SchemeGuid ?? throw _errorFormatter.CreateDuplicateMissingGuidException();
    }

    private void InvalidatePlansCache()
    {
        lock (_plansCacheLock)
        {
            _cachedPlans = null;
            _cachedPlansAt = default;
            _plansFetchTask = null;
            _plansFetchTaskVersion = -1;
            _plansCacheVersion++;
        }
    }

    private Guid ParsePowerSchemeGuid(string value, string errorKey)
    {
        if (Guid.TryParse(value, out var guid))
        {
            return guid;
        }

        throw _errorFormatter.CreateInvalidGuidException(errorKey);
    }

    private void ThrowIfFailed(uint result, string errorKey)
    {
        if (result != ErrorSuccess)
        {
            throw _errorFormatter.CreateWin32Exception(result, errorKey);
        }
    }

    private const uint ErrorSuccess = 0;
    private const uint ErrorNoMoreItems = 259;
}
