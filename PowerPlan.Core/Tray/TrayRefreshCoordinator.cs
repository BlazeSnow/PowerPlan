namespace PowerPlan.Tray;

public sealed class TrayRefreshCoordinator
{
    private readonly object _syncRoot = new();
    private Task? _refreshTask;
    private bool _refreshTaskForceRefresh;
    private bool _pendingForceRefresh;

    public async Task RefreshAsync(bool forceRefresh, Func<bool, Task> refreshCoreAsync)
    {
        ArgumentNullException.ThrowIfNull(refreshCoreAsync);

        var nextForceRefresh = forceRefresh;
        while (true)
        {
            Task refreshTask;

            lock (_syncRoot)
            {
                if (_refreshTask is null)
                {
                    if (nextForceRefresh)
                    {
                        _pendingForceRefresh = false;
                    }

                    var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    refreshTask = completion.Task;
                    _refreshTask = refreshTask;
                    _refreshTaskForceRefresh = nextForceRefresh;
                    _ = CompleteRefreshAsync(nextForceRefresh, refreshCoreAsync, completion);
                }
                else if (nextForceRefresh && !_refreshTaskForceRefresh)
                {
                    _pendingForceRefresh = true;
                    refreshTask = _refreshTask;
                }
                else
                {
                    refreshTask = _refreshTask;
                }
            }

            await refreshTask;

            lock (_syncRoot)
            {
                if (!forceRefresh || !_pendingForceRefresh)
                {
                    return;
                }

                nextForceRefresh = true;
            }
        }
    }

    private async Task CompleteRefreshAsync(
        bool forceRefresh,
        Func<bool, Task> refreshCoreAsync,
        TaskCompletionSource completion)
    {
        try
        {
            await refreshCoreAsync(forceRefresh);
            completion.SetResult();
        }
        catch (Exception ex)
        {
            completion.SetException(ex);
        }
        finally
        {
            lock (_syncRoot)
            {
                _refreshTask = null;
                _refreshTaskForceRefresh = false;
            }
        }
    }
}
