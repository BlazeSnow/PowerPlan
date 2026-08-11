using PowerPlan.Tray;

namespace PowerPlan.Tests;

public sealed class TrayRefreshCoordinatorTests
{
    [Fact]
    public async Task RefreshAsync_ConcurrentNormalRequestsShareOneRefresh()
    {
        var coordinator = new TrayRefreshCoordinator();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = new List<bool>();

        Task RefreshCoreAsync(bool forceRefresh)
        {
            calls.Add(forceRefresh);
            started.TrySetResult();
            return release.Task;
        }

        var first = coordinator.RefreshAsync(false, RefreshCoreAsync);
        await started.Task;
        var second = coordinator.RefreshAsync(false, RefreshCoreAsync);
        release.SetResult();
        await Task.WhenAll(first, second);

        Assert.Equal([false], calls);
    }

    [Fact]
    public async Task RefreshAsync_ForceRequestDuringNormalRefreshRunsOneFollowUpForceRefresh()
    {
        var coordinator = new TrayRefreshCoordinator();
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = new List<bool>();

        Task RefreshCoreAsync(bool forceRefresh)
        {
            calls.Add(forceRefresh);
            if (calls.Count == 1)
            {
                firstStarted.TrySetResult();
                return firstRelease.Task;
            }

            secondStarted.TrySetResult();
            return secondRelease.Task;
        }

        var normal = coordinator.RefreshAsync(false, RefreshCoreAsync);
        await firstStarted.Task;
        var force = coordinator.RefreshAsync(true, RefreshCoreAsync);
        var secondForce = coordinator.RefreshAsync(true, RefreshCoreAsync);
        firstRelease.SetResult();
        await secondStarted.Task;
        secondRelease.SetResult();
        await Task.WhenAll(normal, force, secondForce);

        Assert.Equal([false, true], calls);
    }

    [Fact]
    public async Task RefreshAsync_NormalRequestDuringForceRefreshDoesNotStartAnotherRefresh()
    {
        var coordinator = new TrayRefreshCoordinator();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = new List<bool>();

        Task RefreshCoreAsync(bool forceRefresh)
        {
            calls.Add(forceRefresh);
            started.TrySetResult();
            return release.Task;
        }

        var force = coordinator.RefreshAsync(true, RefreshCoreAsync);
        await started.Task;
        var normal = coordinator.RefreshAsync(false, RefreshCoreAsync);
        release.SetResult();
        await Task.WhenAll(force, normal);

        Assert.Equal([true], calls);
    }

    [Fact]
    public async Task RefreshAsync_AllowsRetryAfterFailure()
    {
        var coordinator = new TrayRefreshCoordinator();
        var calls = 0;

        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.RefreshAsync(false, _ => throw new InvalidOperationException("failed")));
        await coordinator.RefreshAsync(false, _ =>
        {
            calls++;
            return Task.CompletedTask;
        });

        Assert.Equal(1, calls);
    }
}
