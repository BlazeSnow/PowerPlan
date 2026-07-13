using Microsoft.UI.Dispatching;
using Microsoft.Windows.AppLifecycle;
using WinAppInstance = Microsoft.Windows.AppLifecycle.AppInstance;

namespace PowerPlan.Services;

public sealed class ActivationService : IDisposable
{
    private readonly Func<DispatcherQueue?> _getUiDispatcherQueue;
    private readonly Action _showMainWindow;
    private bool _isMainInstance;
    private int _pendingShowRequested;
    private bool _disposed;

    public ActivationService(Func<DispatcherQueue?> getUiDispatcherQueue, Action showMainWindow)
    {
        _getUiDispatcherQueue = getUiDispatcherQueue;
        _showMainWindow = showMainWindow;
    }

    public bool IsStartupTaskLaunch
    {
        get
        {
            try
            {
                if (WinAppInstance.GetCurrent().GetActivatedEventArgs().Kind == ExtendedActivationKind.StartupTask)
                {
                    return true;
                }

                return string.Equals(Environment.GetEnvironmentVariable("POWERPLAN_SIMULATE_STARTUP"), "1", StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }
    }

    public async Task<bool> InitializeAsync()
    {
        var mainInstance = WinAppInstance.FindOrRegisterForKey("PowerPlan.Main");
        if (!mainInstance.IsCurrent)
        {
            await mainInstance.RedirectActivationToAsync(WinAppInstance.GetCurrent().GetActivatedEventArgs());
            return false;
        }

        _isMainInstance = true;
        WinAppInstance.GetCurrent().Activated -= OnAppActivated;
        WinAppInstance.GetCurrent().Activated += OnAppActivated;
        return true;
    }

    public void ShowPendingActivationIfRequested()
    {
        if (Interlocked.Exchange(ref _pendingShowRequested, 0) == 1)
        {
            _showMainWindow();
        }
    }

    private void OnAppActivated(object? sender, AppActivationArguments args)
    {
        if (args.Kind != ExtendedActivationKind.StartupTask)
        {
            RequestShowMainWindow();
        }
    }

    private void RequestShowMainWindow()
    {
        var dispatcher = _getUiDispatcherQueue();
        if (dispatcher is null || !dispatcher.TryEnqueue(() => _showMainWindow()))
        {
            Interlocked.Exchange(ref _pendingShowRequested, 1);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_isMainInstance)
        {
            WinAppInstance.GetCurrent().Activated -= OnAppActivated;
        }
    }
}
