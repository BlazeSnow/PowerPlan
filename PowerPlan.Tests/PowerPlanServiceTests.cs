using PowerPlan.Services;
using PowerPlan.Tests.TestDoubles;

namespace PowerPlan.Tests;

public sealed class PowerPlanServiceTests
{
    [Fact]
    public async Task GetPlansAsync_ReadsPlansAndFallsBackToGuidForBlankName()
    {
        var activeGuid = Guid.NewGuid();
        var otherGuid = Guid.NewGuid();
        var nativeApi = CreateNativeApi(activeGuid, otherGuid);
        nativeApi.FriendlyNames[activeGuid] = "Balanced";
        nativeApi.FriendlyNames[otherGuid] = string.Empty;
        var service = new PowerPlanService(nativeApi, new FakePowerPlanErrorFormatter());

        var plans = await service.GetPlansAsync();

        Assert.Collection(
            plans,
            plan =>
            {
                Assert.Equal(activeGuid.ToString("D"), plan.Guid);
                Assert.Equal("Balanced", plan.Name);
                Assert.True(plan.IsActive);
            },
            plan =>
            {
                Assert.Equal(otherGuid.ToString("D"), plan.Guid);
                Assert.Equal(otherGuid.ToString("D"), plan.Name);
                Assert.False(plan.IsActive);
            });
    }

    [Fact]
    public async Task GetPlansAsync_UsesCacheUntilForcedRefresh()
    {
        var activeGuid = Guid.NewGuid();
        var nativeApi = CreateNativeApi(activeGuid);
        var service = new PowerPlanService(nativeApi, new FakePowerPlanErrorFormatter());

        _ = await service.GetPlansAsync();
        _ = await service.GetPlansAsync();

        Assert.Equal(1, nativeApi.GetActiveSchemeCallCount);
        Assert.Equal(2, nativeApi.EnumerateSchemeCallCount);

        _ = await service.GetPlansAsync(forceRefresh: true);

        Assert.Equal(2, nativeApi.GetActiveSchemeCallCount);
        Assert.Equal(4, nativeApi.EnumerateSchemeCallCount);
    }

    [Fact]
    public async Task SetActivePlanAsync_InvalidatesCachedPlans()
    {
        var activeGuid = Guid.NewGuid();
        var nextGuid = Guid.NewGuid();
        var nativeApi = CreateNativeApi(activeGuid);
        var service = new PowerPlanService(nativeApi, new FakePowerPlanErrorFormatter());

        _ = await service.GetPlansAsync();
        await service.SetActivePlanAsync(nextGuid.ToString("D"));
        _ = await service.GetPlansAsync();

        Assert.Equal(nextGuid, nativeApi.SetActiveSchemeArgument);
        Assert.Equal(2, nativeApi.GetActiveSchemeCallCount);
    }

    [Fact]
    public async Task CopyPlanAsync_DuplicatesSourceAndWritesTrimmedName()
    {
        var sourceGuid = Guid.NewGuid();
        var newGuid = Guid.NewGuid();
        var nativeApi = new FakePowerSchemeNativeApi
        {
            DuplicateSchemeResult = new PowerSchemeDuplicateResult(0, newGuid)
        };
        var service = new PowerPlanService(nativeApi, new FakePowerPlanErrorFormatter());

        var copiedGuid = await service.CopyPlanAsync(sourceGuid.ToString("D"), "  Office profile  ");

        Assert.Equal(newGuid.ToString("D"), copiedGuid);
        Assert.Equal(sourceGuid, nativeApi.DuplicateSchemeArgument);
        Assert.Equal((newGuid, "Office profile"), nativeApi.WriteFriendlyNameArgument);
    }

    [Fact]
    public async Task CopyPlanAsync_RejectsBlankNameAfterDuplication()
    {
        var sourceGuid = Guid.NewGuid();
        var nativeApi = new FakePowerSchemeNativeApi();
        var service = new PowerPlanService(nativeApi, new FakePowerPlanErrorFormatter());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CopyPlanAsync(sourceGuid.ToString("D"), "   "));

        Assert.Equal("EmptyName", exception.Message);
        Assert.Equal(sourceGuid, nativeApi.DuplicateSchemeArgument);
        Assert.Null(nativeApi.WriteFriendlyNameArgument);
    }

    [Fact]
    public async Task CreateUltimatePerformancePlanAsync_DuplicatesUltimatePlan()
    {
        var createdGuid = Guid.NewGuid();
        var nativeApi = new FakePowerSchemeNativeApi
        {
            DuplicateSchemeResult = new PowerSchemeDuplicateResult(0, createdGuid)
        };
        var service = new PowerPlanService(nativeApi, new FakePowerPlanErrorFormatter());

        var result = await service.CreateUltimatePerformancePlanAsync();

        Assert.Equal(createdGuid.ToString("D"), result);
        Assert.Equal(Guid.Parse(PowerPlanService.UltimatePerformanceGuid), nativeApi.DuplicateSchemeArgument);
    }

    [Fact]
    public async Task RestoreDefaultSchemesAsync_DelegatesToNativeApi()
    {
        var nativeApi = new FakePowerSchemeNativeApi();
        var service = new PowerPlanService(nativeApi, new FakePowerPlanErrorFormatter());

        await service.RestoreDefaultSchemesAsync();

        Assert.Equal(1, nativeApi.RestoreDefaultSchemesCallCount);
    }

    [Fact]
    public async Task SetActivePlanAsync_UsesFormatterForInvalidGuid()
    {
        var errorFormatter = new FakePowerPlanErrorFormatter();
        var service = new PowerPlanService(new FakePowerSchemeNativeApi(), errorFormatter);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.SetActivePlanAsync("not-a-guid"));

        Assert.Equal("PowerPlan.Error.InvalidPlanGuid", exception.Message);
        Assert.Equal(["PowerPlan.Error.InvalidPlanGuid"], errorFormatter.InvalidGuidErrorKeys);
    }

    [Fact]
    public async Task GetPlansAsync_UsesFormatterForNativeFailures()
    {
        var nativeApi = new FakePowerSchemeNativeApi { GetActiveSchemeResult = 5 };
        var errorFormatter = new FakePowerPlanErrorFormatter();
        var service = new PowerPlanService(nativeApi, errorFormatter);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetPlansAsync());

        Assert.Equal("PowerPlan.Error.ReadActiveFailed:5", exception.Message);
        Assert.Equal([(5u, "PowerPlan.Error.ReadActiveFailed")], errorFormatter.Win32Errors);
    }

    [Fact]
    public async Task CopyPlanAsync_ThrowsWhenNativeApiDoesNotReturnDuplicateGuid()
    {
        var nativeApi = new FakePowerSchemeNativeApi
        {
            DuplicateSchemeResult = new PowerSchemeDuplicateResult(0, null)
        };
        var service = new PowerPlanService(nativeApi, new FakePowerPlanErrorFormatter());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CopyPlanAsync(Guid.NewGuid().ToString("D"), "Copy"));

        Assert.Equal("DuplicateMissingGuid", exception.Message);
    }

    [Fact]
    public void IsUltimatePerformancePlan_ComparesGuidsIgnoringCase()
    {
        var service = new PowerPlanService(new FakePowerSchemeNativeApi(), new FakePowerPlanErrorFormatter());

        var isUltimate = service.IsUltimatePerformancePlan(new()
        {
            Guid = PowerPlanService.UltimatePerformanceGuid.ToUpperInvariant(),
            Name = "Ultimate",
            IsActive = false
        });

        Assert.True(isUltimate);
    }

    [Fact]
    public async Task GetPlansAsync_UsesFormatterForEnumerationFailures()
    {
        var nativeApi = CreateNativeApi(Guid.NewGuid());
        nativeApi.EnumerateResult = 5;
        var errors = new FakePowerPlanErrorFormatter();
        var service = new PowerPlanService(nativeApi, errors);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetPlansAsync());

        Assert.Equal("PowerPlan.Error.EnumerateFailed:5", exception.Message);
        Assert.Equal([(5u, "PowerPlan.Error.EnumerateFailed")], errors.Win32Errors);
    }

    [Fact]
    public async Task GetPlansAsync_UsesFormatterForFriendlyNameFailures()
    {
        var nativeApi = CreateNativeApi(Guid.NewGuid());
        nativeApi.ReadFriendlyNameResult = 5;
        var errors = new FakePowerPlanErrorFormatter();
        var service = new PowerPlanService(nativeApi, errors);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetPlansAsync());

        Assert.Equal("PowerPlan.Error.ReadNameFailed:5", exception.Message);
        Assert.Equal([(5u, "PowerPlan.Error.ReadNameFailed")], errors.Win32Errors);
    }

    [Fact]
    public async Task SetActivePlanAsync_UsesFormatterForNativeFailure()
    {
        var nativeApi = new FakePowerSchemeNativeApi { SetActiveSchemeResult = 5 };
        var errors = new FakePowerPlanErrorFormatter();
        var service = new PowerPlanService(nativeApi, errors);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SetActivePlanAsync(Guid.NewGuid().ToString("D")));

        Assert.Equal("PowerPlan.Error.SetActiveFailed:5", exception.Message);
        Assert.Equal([(5u, "PowerPlan.Error.SetActiveFailed")], errors.Win32Errors);
    }

    [Fact]
    public async Task CopyPlanAsync_UsesFormatterForInvalidSourceGuid()
    {
        var errors = new FakePowerPlanErrorFormatter();
        var nativeApi = new FakePowerSchemeNativeApi();
        var service = new PowerPlanService(nativeApi, errors);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CopyPlanAsync("not-a-guid", "Copy"));

        Assert.Equal("PowerPlan.Error.InvalidSourcePlanGuid", exception.Message);
        Assert.Null(nativeApi.DuplicateSchemeArgument);
        Assert.Equal(["PowerPlan.Error.InvalidSourcePlanGuid"], errors.InvalidGuidErrorKeys);
    }

    [Fact]
    public async Task CopyPlanAsync_UsesFormatterForDuplicateFailure()
    {
        var nativeApi = new FakePowerSchemeNativeApi
        {
            DuplicateSchemeResult = new PowerSchemeDuplicateResult(5, Guid.NewGuid())
        };
        var errors = new FakePowerPlanErrorFormatter();
        var service = new PowerPlanService(nativeApi, errors);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CopyPlanAsync(Guid.NewGuid().ToString("D"), "Copy"));

        Assert.Equal("PowerPlan.Error.DuplicateFailed:5", exception.Message);
        Assert.Equal([(5u, "PowerPlan.Error.DuplicateFailed")], errors.Win32Errors);
    }

    [Fact]
    public async Task CopyPlanAsync_UsesFormatterForFriendlyNameWriteFailure()
    {
        var nativeApi = new FakePowerSchemeNativeApi
        {
            WriteFriendlyNameResult = 5
        };
        var errors = new FakePowerPlanErrorFormatter();
        var service = new PowerPlanService(nativeApi, errors);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CopyPlanAsync(Guid.NewGuid().ToString("D"), "Copy"));

        Assert.Equal("PowerPlan.Error.WriteNameFailed:5", exception.Message);
        Assert.Equal([(5u, "PowerPlan.Error.WriteNameFailed")], errors.Win32Errors);
    }

    [Fact]
    public async Task RestoreDefaultSchemesAsync_UsesFormatterForNativeFailure()
    {
        var nativeApi = new FakePowerSchemeNativeApi { RestoreDefaultSchemesResult = 5 };
        var errors = new FakePowerPlanErrorFormatter();
        var service = new PowerPlanService(nativeApi, errors);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.RestoreDefaultSchemesAsync());

        Assert.Equal("PowerPlan.Error.RestoreDefaultsFailed:5", exception.Message);
        Assert.Equal([(5u, "PowerPlan.Error.RestoreDefaultsFailed")], errors.Win32Errors);
    }

    [Fact]
    public async Task GetPlansAsync_ConcurrentNormalRequestsShareFetchTask()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var nativeApi = CreateNativeApi(Guid.NewGuid());
        nativeApi.FirstReadStarted = started;
        nativeApi.FirstReadRelease = release;
        var service = new PowerPlanService(nativeApi, new FakePowerPlanErrorFormatter());

        var first = service.GetPlansAsync();
        await started.Task;
        var second = service.GetPlansAsync();
        release.SetResult();
        await Task.WhenAll(first, second);

        Assert.Equal(1, nativeApi.GetActiveSchemeCallCount);
    }

    [Fact]
    public void IsUltimatePerformancePlan_ReturnsFalseForOtherPlan()
    {
        var service = new PowerPlanService(new FakePowerSchemeNativeApi(), new FakePowerPlanErrorFormatter());

        Assert.False(service.IsUltimatePerformancePlan(new()
        {
            Guid = Guid.NewGuid().ToString("D"),
            Name = "Balanced",
            IsActive = false
        }));
    }
    [Fact]
    public async Task GetPlansAsync_AllowsRetryAfterReadFailure()
    {
        var nativeApi = CreateNativeApi(Guid.NewGuid());
        nativeApi.GetActiveSchemeResult = 5;
        var service = new PowerPlanService(nativeApi, new FakePowerPlanErrorFormatter());

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetPlansAsync());
        nativeApi.GetActiveSchemeResult = 0;
        var plans = await service.GetPlansAsync();

        Assert.Single(plans);
        Assert.Equal(2, nativeApi.GetActiveSchemeCallCount);
    }

    [Theory]
    [InlineData("copy")]
    [InlineData("ultimate")]
    [InlineData("restore")]
    public async Task SuccessfulPlanChanges_InvalidateCachedPlans(string operation)
    {
        var sourceGuid = Guid.NewGuid();
        var nativeApi = CreateNativeApi(sourceGuid);
        var service = new PowerPlanService(nativeApi, new FakePowerPlanErrorFormatter());
        _ = await service.GetPlansAsync();

        switch (operation)
        {
            case "copy":
                await service.CopyPlanAsync(sourceGuid.ToString("D"), "Copy");
                break;
            case "ultimate":
                await service.CreateUltimatePerformancePlanAsync();
                break;
            case "restore":
                await service.RestoreDefaultSchemesAsync();
                break;
        }

        _ = await service.GetPlansAsync();

        Assert.Equal(2, nativeApi.GetActiveSchemeCallCount);
    }
    private static FakePowerSchemeNativeApi CreateNativeApi(params Guid[] schemes)
    {
        var nativeApi = new FakePowerSchemeNativeApi
        {
            ActiveScheme = schemes[0]
        };
        nativeApi.Schemes.AddRange(schemes);
        return nativeApi;
    }
}
