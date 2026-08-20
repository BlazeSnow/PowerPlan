using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using PowerPlan.Models;

namespace PowerPlan.Tray.Services;

public sealed class TrayService : IDisposable
{
    private readonly Func<bool, Task<IReadOnlyList<PowerPlanInfo>>> _getPlansAsync;
    private readonly Func<string, Task> _setActivePlanAsync;
    private readonly Func<string?> _getHiddenUltimatePlanGuid;
    private readonly Func<string, Task> _activateHiddenUltimatePlanAsync;
    private readonly Func<bool> _isStartupEnabled;
    private readonly Func<bool, Task<bool>> _setStartupEnabled;
    private readonly Func<IReadOnlyList<PowerPlanInfo>, Task> _onPlansRefreshed;
    private readonly Action _showMainWindow;
    private readonly Action _exitApplication;
    private readonly Action<string, InfoBarSeverity> _log;
    private readonly ITrayLocalizer _localizer;
    private readonly DispatcherQueue _uiDispatcherQueue;
    private readonly TrayNativeHost _nativeHost = new();
    private readonly TrayMenuPresenter _menuPresenter;
    private readonly TrayRefreshCoordinator _refreshCoordinator = new();

    private readonly object _plansLock = new();
    private TrayPlansSnapshot _plansSnapshot = new(Array.Empty<PowerPlanInfo>());
    private bool _disposed;

    public TrayService(
        DispatcherQueue uiDispatcherQueue,
        Func<bool, Task<IReadOnlyList<PowerPlanInfo>>> getPlansAsync,
        Func<string, Task> setActivePlanAsync,
        Func<string?> getHiddenUltimatePlanGuid,
        Func<string, Task> activateHiddenUltimatePlanAsync,
        Func<bool> isStartupEnabled,
        Func<bool, Task<bool>> setStartupEnabled,
        Func<IReadOnlyList<PowerPlanInfo>, Task> onPlansRefreshed,
        Action showMainWindow,
        Action exitApplication,
        Action<string, InfoBarSeverity> log,
        ITrayLocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(uiDispatcherQueue);
        ArgumentNullException.ThrowIfNull(localizer);
        _uiDispatcherQueue = uiDispatcherQueue;
        _getPlansAsync = getPlansAsync;
        _setActivePlanAsync = setActivePlanAsync;
        _getHiddenUltimatePlanGuid = getHiddenUltimatePlanGuid;
        _activateHiddenUltimatePlanAsync = activateHiddenUltimatePlanAsync;
        _isStartupEnabled = isStartupEnabled;
        _setStartupEnabled = setStartupEnabled;
        _onPlansRefreshed = onPlansRefreshed;
        _showMainWindow = showMainWindow;
        _exitApplication = exitApplication;
        _log = log;
        _localizer = localizer;
        _menuPresenter = new TrayMenuPresenter(new TrayMenuBuilder(localizer));
        _nativeHost.MenuRequested += OnMenuRequested;
        _nativeHost.RestoreFailed += OnNativeHostRestoreFailed;
    }

    public bool IsInitialized => _nativeHost.IsInitialized && !_disposed;

    public async Task InitializeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await RunOnUiThreadAsync(() => _nativeHost.Initialize(BuildTooltipText()));
        await RefreshPlansAsync();
        _log(_localizer.Get("Tray.Init"), InfoBarSeverity.Success);
    }

    public Task RefreshPlansAsync(bool forceRefresh = false)
    {
        return _refreshCoordinator.RefreshAsync(forceRefresh, RefreshPlansCoreAsync);
    }

    public void UpdatePlansSnapshot(IReadOnlyList<PowerPlanInfo> plans)
    {
        lock (_plansLock)
        {
            _plansSnapshot = _plansSnapshot.Replace(plans);
        }

        UpdateTrayTooltip();
    }

    public void UpdateStatus()
    {
        UpdateTrayTooltip();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _nativeHost.MenuRequested -= OnMenuRequested;
        _nativeHost.RestoreFailed -= OnNativeHostRestoreFailed;
        RunOnUiThreadSynchronously(_nativeHost.Dispose);
    }

    private async Task RefreshPlansCoreAsync(bool forceRefresh)
    {
        try
        {
            var plans = await _getPlansAsync(forceRefresh);
            UpdatePlansSnapshot(plans);
            await _onPlansRefreshed(plans);
        }
        catch (Exception ex)
        {
            _log(_localizer.Format("Tray.RefreshFailed", ex.Message), InfoBarSeverity.Error);
        }
    }

    private void UpdateTrayTooltip()
    {
        _ = RunOnUiThread(() =>
        {
            if (!_disposed)
            {
                _nativeHost.UpdateTooltip(BuildTooltipText());
            }
        });
    }

    private string BuildTooltipText()
    {
        lock (_plansLock)
        {
            return TrayTooltipFormatter.Format(_plansSnapshot.Plans, _isStartupEnabled(), _localizer);
        }
    }

    private void OnMenuRequested(nint window, nint packedPosition)
    {
        if (_disposed)
        {
            return;
        }

        TrayMenuContext context;
        lock (_plansLock)
        {
            context = _plansSnapshot.CreateMenuContext(_getHiddenUltimatePlanGuid(), _isStartupEnabled());
        }

        var command = _menuPresenter.Show(window, packedPosition, context);
        if (command is not null)
        {
            DispatchMenuCommand(command.Value);
        }
    }

    private void OnNativeHostRestoreFailed(Exception exception)
    {
        _log(_localizer.Format("Tray.RefreshFailed", exception.Message), InfoBarSeverity.Error);
    }

    private void DispatchMenuCommand(TrayMenuCommand command)
    {
        if (!_uiDispatcherQueue.TryEnqueue(() => _ = ExecuteMenuCommandAsync(command)))
        {
            _log(_localizer.Get("Tray.DispatcherUnavailable"), InfoBarSeverity.Error);
        }
    }

    private async Task ExecuteMenuCommandAsync(TrayMenuCommand command)
    {
        switch (command.Action)
        {
            case TrayMenuAction.OpenMainWindow:
                _showMainWindow();
                break;
            case TrayMenuAction.SwitchPlan:
                if (command.PlanGuid is not null && command.PlanName is not null)
                {
                    await OnSwitchPlanAsync(command.PlanGuid, command.PlanName);
                }

                break;
            case TrayMenuAction.ActivateHiddenUltimate:
                if (command.PlanGuid is not null)
                {
                    await OnActivateHiddenUltimateAsync(command.PlanGuid);
                }

                break;
            case TrayMenuAction.RefreshPlans:
                OnRefreshPlansRequested();
                break;
            case TrayMenuAction.ToggleStartup:
                await ToggleStartupAsync();
                break;
            case TrayMenuAction.Exit:
                _exitApplication();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(command));
        }
    }

    private async Task OnSwitchPlanAsync(string planGuid, string planName)
    {
        try
        {
            await _setActivePlanAsync(planGuid);
            SetActivePlanInCache(planGuid);
            UpdateTrayTooltip();
            _log(_localizer.Format("Tray.SwitchTo", planName), InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            _log(_localizer.Format("Tray.SwitchFailed", ex.Message), InfoBarSeverity.Error);
        }
    }

    private async Task OnActivateHiddenUltimateAsync(string planGuid)
    {
        try
        {
            await _activateHiddenUltimatePlanAsync(planGuid);
            _log(_localizer.Get("Tray.HiddenUltimateActivated"), InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            _log(_localizer.Format("Tray.HiddenUltimateActivateFailed", ex.Message), InfoBarSeverity.Error);
        }
    }

    private void SetActivePlanInCache(string activePlanGuid)
    {
        lock (_plansLock)
        {
            _plansSnapshot = _plansSnapshot.WithActivePlan(activePlanGuid);
        }
    }

    private async Task ToggleStartupAsync()
    {
        try
        {
            var next = !_isStartupEnabled();
            _ = await _setStartupEnabled(next);
            UpdateTrayTooltip();
        }
        catch (Exception ex)
        {
            _log(_localizer.Format("Tray.AutoStartToggleFailed", ex.Message), InfoBarSeverity.Error);
        }
    }

    private void OnRefreshPlansRequested()
    {
        _ = RefreshPlansAsync(forceRefresh: true);
        _log(_localizer.Get("Tray.RefreshStarted"), InfoBarSeverity.Informational);
    }

    private bool RunOnUiThread(Action action)
    {
        if (_uiDispatcherQueue.HasThreadAccess)
        {
            action();
            return true;
        }

        if (!_uiDispatcherQueue.TryEnqueue(() => action()))
        {
            _log(_localizer.Get("Tray.DispatcherUnavailable"), InfoBarSeverity.Error);
            return false;
        }

        return true;
    }

    private Task RunOnUiThreadAsync(Action action)
    {
        if (_uiDispatcherQueue.HasThreadAccess)
        {
            action();
            return Task.CompletedTask;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var enqueued = _uiDispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                action();
                completion.SetResult();
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        });

        if (!enqueued)
        {
            completion.SetException(new InvalidOperationException(_localizer.Get("Tray.DispatcherUnavailable")));
        }

        return completion.Task;
    }

    private void RunOnUiThreadSynchronously(Action action)
    {
        if (_uiDispatcherQueue.HasThreadAccess)
        {
            action();
            return;
        }

        using var completion = new ManualResetEventSlim();
        Exception? exception = null;
        if (!_uiDispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    exception = ex;
                }
                finally
                {
                    completion.Set();
                }
            }))
        {
            return;
        }

        completion.Wait();
        if (exception is not null)
        {
            throw new InvalidOperationException("Unable to dispose the tray icon.", exception);
        }
    }
}
