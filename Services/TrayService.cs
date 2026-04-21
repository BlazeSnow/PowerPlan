using H.NotifyIcon;
using H.NotifyIcon.Core;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using PowerPlan.Models;
using System.Drawing;
using System.Windows.Input;

namespace PowerPlan.Services;

public sealed class TrayService : IDisposable
{
    private readonly Func<Task<IReadOnlyList<PowerPlanInfo>>> _getPlansAsync;
    private readonly Func<string, Task> _setActivePlanAsync;
    private readonly Func<string?> _getHiddenUltimatePlanGuid;
    private readonly Func<string, Task> _activateHiddenUltimatePlanAsync;
    private readonly Func<bool> _isStartupEnabled;
    private readonly Func<bool, Task<bool>> _setStartupEnabled;
    private readonly Func<Task> _onPlansRefreshed;
    private readonly Action _showMainWindow;
    private readonly Action _exitApplication;
    private readonly Action<string, InfoBarSeverity> _log;
    private readonly DispatcherQueue _uiDispatcherQueue;

    private readonly object _plansLock = new();
    private readonly object _refreshTaskLock = new();
    private IReadOnlyList<PowerPlanInfo> _cachedPlans = Array.Empty<PowerPlanInfo>();
    private Task? _refreshPlansTask;

    private TaskbarIcon? _taskbarIcon;
    private MenuFlyout? _contextFlyout;
    private bool _disposed;

    public TrayService(
        DispatcherQueue uiDispatcherQueue,
        Func<Task<IReadOnlyList<PowerPlanInfo>>> getPlansAsync,
        Func<string, Task> setActivePlanAsync,
        Func<string?> getHiddenUltimatePlanGuid,
        Func<string, Task> activateHiddenUltimatePlanAsync,
        Func<bool> isStartupEnabled,
        Func<bool, Task<bool>> setStartupEnabled,
        Func<Task> onPlansRefreshed,
        Action showMainWindow,
        Action exitApplication,
        Action<string, InfoBarSeverity> log)
    {
        _uiDispatcherQueue = uiDispatcherQueue ?? throw new ArgumentNullException(nameof(uiDispatcherQueue));
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
    }

    public async Task InitializeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await RunOnUiThreadAsync(() =>
        {
            _contextFlyout = new MenuFlyout();
            _taskbarIcon = new TaskbarIcon
            {
                ToolTipText = "PowerPlan",
                ContextFlyout = _contextFlyout,
                MenuActivation = PopupActivationMode.LeftOrRightClick
            };
            _taskbarIcon.NoLeftClickDelay = true;

            try
            {
                var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "powerplan.ico");
                if (File.Exists(iconPath))
                {
                    _taskbarIcon.Icon = new Icon(iconPath);
                }
                else
                {
                    _taskbarIcon.IconSource = new BitmapImage(new Uri("ms-appx:///Assets/powerplan.png"));
                }
            }
            catch
            {
                // Keep tray available even if icon source fails to resolve.
            }

            _taskbarIcon.ForceCreate();
        });

        await RefreshPlansAsync();
        _log(LocalizationService.Get("Tray.Init"), InfoBarSeverity.Success);
    }

    public async Task RefreshPlansAsync()
    {
        Task refreshTask;

        lock (_refreshTaskLock)
        {
            _refreshPlansTask ??= RefreshPlansCoreAsync();
            refreshTask = _refreshPlansTask;
        }

        await refreshTask;
    }

    private async Task RefreshPlansCoreAsync()
    {
        try
        {
            var plans = await _getPlansAsync();
            UpdatePlansSnapshot(plans);
            await _onPlansRefreshed();
        }
        catch (Exception ex)
        {
            _log(LocalizationService.Format("Tray.RefreshFailed", ex.Message), InfoBarSeverity.Error);
        }
        finally
        {
            lock (_refreshTaskLock)
            {
                _refreshPlansTask = null;
            }
        }
    }

    public void UpdatePlansSnapshot(IReadOnlyList<PowerPlanInfo> plans)
    {
        var changed = false;

        lock (_plansLock)
        {
            var nextPlans = plans
                .Select(plan => new PowerPlanInfo
                {
                    Guid = plan.Guid,
                    Name = plan.Name,
                    IsActive = plan.IsActive
                })
                .ToArray();

            changed = !ArePlansEqual(_cachedPlans, nextPlans);
            if (changed)
            {
                _cachedPlans = nextPlans;
            }
        }

        if (!changed)
        {
            return;
        }

        RebuildMenu();
    }

    public void ShowBalloon(string message)
    {
        _log(message, InfoBarSeverity.Informational);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        RunOnUiThread(() =>
        {
            if (_taskbarIcon is not null)
            {
                _taskbarIcon.Dispose();
                _taskbarIcon = null;
            }

            _contextFlyout = null;
        });
    }

    private void RebuildMenu()
    {
        RunOnUiThread(() =>
        {
            if (_contextFlyout is null)
            {
                return;
            }

            _contextFlyout.Items.Clear();

            IReadOnlyList<PowerPlanInfo> plans;
            lock (_plansLock)
            {
                plans = _cachedPlans.ToArray();
            }

            _contextFlyout.Items.Add(new MenuFlyoutItem
            {
                Text = LocalizationService.Get("App.WindowTitle", "PowerPlan"),
                IsEnabled = false
            });

            _contextFlyout.Items.Add(CreateActionItem(
                "\u2302 " + LocalizationService.Get("Tray.Menu.OpenMainWindow"),
                _showMainWindow));
            _contextFlyout.Items.Add(new MenuFlyoutSeparator());

            for (var i = 0; i < plans.Count; i++)
            {
                var plan = plans[i];
                var planGuid = plan.Guid;
                var planName = plan.Name;
                var prefix = plan.IsActive ? "\u2713 " : string.Empty;
                _contextFlyout.Items.Add(CreateActionItem(
                    prefix + "\u26A1 " + planName,
                    () => _ = OnSwitchPlanAsync(planGuid, planName)));
            }

            var hiddenUltimatePlanGuid = _getHiddenUltimatePlanGuid();
            var hasHiddenUltimate = !string.IsNullOrWhiteSpace(hiddenUltimatePlanGuid)
                && !plans.Any(plan => string.Equals(plan.Guid, hiddenUltimatePlanGuid, StringComparison.OrdinalIgnoreCase));
            if (hasHiddenUltimate)
            {
                _contextFlyout.Items.Add(CreateActionItem(
                    "\u26A1 " + LocalizationService.Get("Tray.Menu.OpenHiddenUltimate"),
                    () => _ = OnActivateHiddenUltimateAsync(hiddenUltimatePlanGuid!)));
            }

            _contextFlyout.Items.Add(new MenuFlyoutSeparator());
            _contextFlyout.Items.Add(CreateActionItem(
                "\u21BB " + LocalizationService.Get("Tray.Menu.RefreshPlans"),
                () =>
                {
                    _ = RefreshPlansAsync();
                    _log(LocalizationService.Get("Tray.RefreshStarted"), InfoBarSeverity.Informational);
                }));

            var startupText = _isStartupEnabled()
                ? LocalizationService.Get("Tray.Menu.DisableAutoStart")
                : LocalizationService.Get("Tray.Menu.EnableAutoStart");
            _contextFlyout.Items.Add(CreateActionItem(
                "\u23FB " + startupText,
                () => _ = ToggleStartupAsync()));

            _contextFlyout.Items.Add(new MenuFlyoutSeparator());
            _contextFlyout.Items.Add(CreateActionItem(
                "\u2715 " + LocalizationService.Get("Tray.Menu.Exit"),
                _exitApplication));
        });
    }

    private MenuFlyoutItem CreateActionItem(string text, Action action)
    {
        var item = new MenuFlyoutItem { Text = text };
        var command = new ActionCommand(action);
        item.Command = command;
        return item;
    }

    private async Task OnSwitchPlanAsync(string planGuid, string planName)
    {
        try
        {
            await _setActivePlanAsync(planGuid);
            SetActivePlanInCache(planGuid);
            _log(LocalizationService.Format("Tray.SwitchTo", planName), InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            _log(LocalizationService.Format("Tray.SwitchFailed", ex.Message), InfoBarSeverity.Error);
        }
    }

    private async Task OnActivateHiddenUltimateAsync(string planGuid)
    {
        try
        {
            await _activateHiddenUltimatePlanAsync(planGuid);
            _log(LocalizationService.Get("Tray.HiddenUltimateActivated"), InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            _log(LocalizationService.Format("Tray.HiddenUltimateActivateFailed", ex.Message), InfoBarSeverity.Error);
        }
    }

    private void SetActivePlanInCache(string activePlanGuid)
    {
        var changed = false;

        lock (_plansLock)
        {
            var nextPlans = _cachedPlans
                .Select(plan => new PowerPlanInfo
                {
                    Guid = plan.Guid,
                    Name = plan.Name,
                    IsActive = string.Equals(plan.Guid, activePlanGuid, StringComparison.OrdinalIgnoreCase)
                })
                .ToArray();

            changed = !ArePlansEqual(_cachedPlans, nextPlans);
            if (changed)
            {
                _cachedPlans = nextPlans;
            }
        }

        if (!changed)
        {
            return;
        }

        RebuildMenu();
    }

    private static bool ArePlansEqual(IReadOnlyList<PowerPlanInfo> current, IReadOnlyList<PowerPlanInfo> next)
    {
        if (ReferenceEquals(current, next))
        {
            return true;
        }

        if (current.Count != next.Count)
        {
            return false;
        }

        for (var i = 0; i < current.Count; i++)
        {
            var left = current[i];
            var right = next[i];
            if (!string.Equals(left.Guid, right.Guid, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(left.Name, right.Name, StringComparison.Ordinal)
                || left.IsActive != right.IsActive)
            {
                return false;
            }
        }

        return true;
    }

    private async Task ToggleStartupAsync()
    {
        try
        {
            var next = !_isStartupEnabled();
            var effective = await _setStartupEnabled(next);
            var state = LocalizationService.Get(effective ? "App.Status.On" : "App.Status.Off");
            _log(LocalizationService.Format("Tray.AutoStartState", state), InfoBarSeverity.Success);
            RebuildMenu();
        }
        catch (Exception ex)
        {
            _log(LocalizationService.Format("Tray.AutoStartToggleFailed", ex.Message), InfoBarSeverity.Error);
        }
    }

    private void RunOnUiThread(Action action)
    {
        if (_uiDispatcherQueue.HasThreadAccess)
        {
            action();
            return;
        }

        if (!_uiDispatcherQueue.TryEnqueue(() => action()))
        {
            _log(LocalizationService.Get("Tray.DispatcherUnavailable"), InfoBarSeverity.Error);
        }
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
            var message = LocalizationService.Get("Tray.DispatcherUnavailable");
            completion.SetException(new InvalidOperationException(message));
        }

        return completion.Task;
    }

    private sealed class ActionCommand : ICommand
    {
        private readonly Action _execute;

        public ActionCommand(Action execute)
        {
            _execute = execute;
        }

        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter)
        {
            _execute();
        }
    }
}
