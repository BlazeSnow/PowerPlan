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
    private readonly Func<bool> _isStartupEnabled;
    private readonly Func<bool, Task<bool>> _setStartupEnabled;
    private readonly Action _showMainWindow;
    private readonly Action _exitApplication;
    private readonly Action<string> _log;
    private readonly DispatcherQueue _uiDispatcherQueue;

    private readonly object _plansLock = new();
    private IReadOnlyList<PowerPlanInfo> _cachedPlans = Array.Empty<PowerPlanInfo>();

    private TaskbarIcon? _taskbarIcon;
    private MenuFlyout? _contextFlyout;
    private bool _disposed;

    public TrayService(
        DispatcherQueue uiDispatcherQueue,
        Func<Task<IReadOnlyList<PowerPlanInfo>>> getPlansAsync,
        Func<string, Task> setActivePlanAsync,
        Func<bool> isStartupEnabled,
        Func<bool, Task<bool>> setStartupEnabled,
        Action showMainWindow,
        Action exitApplication,
        Action<string> log)
    {
        _uiDispatcherQueue = uiDispatcherQueue ?? throw new ArgumentNullException(nameof(uiDispatcherQueue));
        _getPlansAsync = getPlansAsync;
        _setActivePlanAsync = setActivePlanAsync;
        _isStartupEnabled = isStartupEnabled;
        _setStartupEnabled = setStartupEnabled;
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
        _log(LocalizationService.Get("Tray.Init"));
    }

    public async Task RefreshPlansAsync()
    {
        try
        {
            var plans = await _getPlansAsync();
            UpdatePlansSnapshot(plans);
        }
        catch (Exception ex)
        {
            _log(LocalizationService.Format("Tray.RefreshFailed", ex.Message));
        }
    }

    public void UpdatePlansSnapshot(IReadOnlyList<PowerPlanInfo> plans)
    {
        lock (_plansLock)
        {
            _cachedPlans = plans
                .Select(plan => new PowerPlanInfo
                {
                    Guid = plan.Guid,
                    Name = plan.Name,
                    IsActive = plan.IsActive
                })
                .ToArray();
        }

        RebuildMenu();
    }

    public void ShowBalloon(string message)
    {
        _log(message);
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

            _contextFlyout.Items.Add(new MenuFlyoutSeparator());
            _contextFlyout.Items.Add(CreateActionItem(
                "\u21BB " + LocalizationService.Get("Tray.Menu.RefreshPlans"),
                () =>
                {
                    _ = RefreshPlansAsync();
                    _log(LocalizationService.Get("Tray.RefreshStarted"));
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
            _log(LocalizationService.Format("Tray.SwitchTo", planName));
        }
        catch (Exception ex)
        {
            _log(LocalizationService.Format("Tray.SwitchFailed", ex.Message));
        }
    }

    private void SetActivePlanInCache(string activePlanGuid)
    {
        lock (_plansLock)
        {
            _cachedPlans = _cachedPlans
                .Select(plan => new PowerPlanInfo
                {
                    Guid = plan.Guid,
                    Name = plan.Name,
                    IsActive = string.Equals(plan.Guid, activePlanGuid, StringComparison.OrdinalIgnoreCase)
                })
                .ToArray();
        }

        RebuildMenu();
    }

    private async Task ToggleStartupAsync()
    {
        try
        {
            var next = !_isStartupEnabled();
            var effective = await _setStartupEnabled(next);
            var state = LocalizationService.Get(effective ? "App.Status.On" : "App.Status.Off");
            _log(LocalizationService.Format("Tray.AutoStartState", state));
            RebuildMenu();
        }
        catch (Exception ex)
        {
            _log(LocalizationService.Format("Tray.AutoStartToggleFailed", ex.Message));
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
            _log(LocalizationService.Get("Tray.DispatcherUnavailable"));
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
