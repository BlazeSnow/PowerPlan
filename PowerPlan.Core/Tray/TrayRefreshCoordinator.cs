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

                    _refreshTask = RefreshCoreAsync(nextForceRefresh, refreshCoreAsync);
                    _refreshTaskForceRefresh = nextForceRefresh;
                }
                else if (nextForceRefresh && !_refreshTaskForceRefresh)
                {
                    _pendingForceRefresh = true;
                }

                refreshTask = _refreshTask;
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

    private async Task RefreshCoreAsync(bool forceRefresh, Func<bool, Task> refreshCoreAsync)
    {
        try
        {
            await refreshCoreAsync(forceRefresh);
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
