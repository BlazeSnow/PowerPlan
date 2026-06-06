using H.NotifyIcon;
using H.NotifyIcon.Core;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media.Imaging;
using PowerPlan.Models;
using System.Windows.Input;

namespace PowerPlan.Services;

public sealed class TrayService : IDisposable
{
    private static readonly string AppTitleText = LocalizationService.Get("App.WindowTitle", "PowerPlan");
    private const string OpenMainWindowIcon = "\u2302 ";
    private const string PowerPlanIcon = "\u26A1 ";
    private const string RefreshPlansIcon = "\u21BB ";
    private const string StartupIcon = "\u23FB ";
    private const string ExitIcon = "\u2715 ";

    private readonly Func<bool, Task<IReadOnlyList<PowerPlanInfo>>> _getPlansAsync;
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
    private bool _refreshPlansTaskForceRefresh;
    private bool _pendingForceRefresh;

    private TaskbarIcon? _taskbarIcon;
    private MenuFlyout? _contextFlyout;
    private string _lastMenuSignature = string.Empty;
    private ElementTheme _currentTheme = ElementTheme.Default;
    private bool _disposed;

    public TrayService(
        DispatcherQueue uiDispatcherQueue,
        Func<bool, Task<IReadOnlyList<PowerPlanInfo>>> getPlansAsync,
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
        ArgumentNullException.ThrowIfNull(uiDispatcherQueue);
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
    }

    public async Task InitializeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await RunOnUiThreadAsync(EnsureTaskbarIcon);
        await RefreshPlansAsync();
        _log(LocalizationService.Get("Tray.Init"), InfoBarSeverity.Success);
    }

    public async Task RefreshPlansAsync(bool forceRefresh = false)
    {
        var nextForceRefresh = forceRefresh;
        while (true)
        {
            Task refreshTask;

            lock (_refreshTaskLock)
            {
                if (_refreshPlansTask is null)
                {
                    if (nextForceRefresh)
                    {
                        _pendingForceRefresh = false;
                    }

                    _refreshPlansTask = RefreshPlansCoreAsync(nextForceRefresh);
                    _refreshPlansTaskForceRefresh = nextForceRefresh;
                }
                else if (nextForceRefresh && !_refreshPlansTaskForceRefresh)
                {
                    _pendingForceRefresh = true;
                }

                refreshTask = _refreshPlansTask
                    ?? throw new InvalidOperationException("Refresh task was not created.");
            }

            await refreshTask;

            lock (_refreshTaskLock)
            {
                if (!forceRefresh || !_pendingForceRefresh)
                {
                    return;
                }

                nextForceRefresh = true;
            }
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

        UpdateTaskbarIcon(forceRebuild: true);
    }

    public void UpdateStatus()
    {
        UpdateTaskbarIcon();
    }

    public void ShowBalloon(string message)
    {
        _log(message, InfoBarSeverity.Informational);
    }

    public void ApplyTheme(ElementTheme theme)
    {
        if (_disposed)
        {
            return;
        }

        _currentTheme = theme;
        _ = RunOnUiThread(ApplyContextFlyoutTheme);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _ = RunOnUiThread(SafeDisposeTaskbarIcon);
    }

    private void SafeDisposeTaskbarIcon()
    {
        try
        {
            _taskbarIcon?.Dispose();
        }
        catch
        {
            // The app is exiting or disabling tray; do not let tray cleanup failures crash shutdown.
        }

        _taskbarIcon = null;
        _contextFlyout = null;
        _lastMenuSignature = string.Empty;
    }

    private async Task RefreshPlansCoreAsync(bool forceRefresh)
    {
        try
        {
            var plans = await _getPlansAsync(forceRefresh);
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
                _refreshPlansTaskForceRefresh = false;
            }
        }
    }

    private void EnsureTaskbarIcon()
    {
        if (_taskbarIcon is not null)
        {
            return;
        }

        _contextFlyout = new MenuFlyout
        {
            AreOpenCloseAnimationsEnabled = false
        };

        _taskbarIcon = new TaskbarIcon
        {
            IconSource = new BitmapImage(new Uri("ms-appx:///Assets/powerplan.ico")),
            MenuActivation = PopupActivationMode.LeftOrRightClick,
            ContextMenuMode = ContextMenuMode.PopupMenu,
            NoLeftClickDelay = true,
            Visibility = Visibility.Visible,
            ToolTipText = BuildTooltipText(),
            ContextFlyout = _contextFlyout
        };
        _taskbarIcon.ForceCreate(enablesEfficiencyMode: false);
        ApplyContextFlyoutTheme();
    }

    private void UpdateTaskbarIcon(bool forceRebuild = false)
    {
        _ = RunOnUiThread(() =>
        {
            if (_taskbarIcon is null || _disposed)
            {
                return;
            }

            _taskbarIcon.ToolTipText = BuildTooltipText();
            RebuildMenuIfNeeded(forceRebuild);
        });
    }

    private string BuildTooltipText()
    {
        string? activePlanName;
        lock (_plansLock)
        {
            activePlanName = _cachedPlans.FirstOrDefault(plan => plan.IsActive)?.Name;
        }

        var planText = string.IsNullOrWhiteSpace(activePlanName)
            ? LocalizationService.Get("Tray.Tooltip.PlanUnavailable")
            : LocalizationService.Format("Tray.Tooltip.Plan", activePlanName);
        var startupState = LocalizationService.Get(_isStartupEnabled() ? "App.Status.On" : "App.Status.Off");
        var startupText = LocalizationService.Format("Tray.Tooltip.AutoStart", startupState);
        return TruncateTooltip($"{AppTitleText}\n{planText}\n{startupText}");
    }

    private static string TruncateTooltip(string text)
    {
        const int maxTooltipLength = 127;
        return text.Length <= maxTooltipLength
            ? text
            : text[..maxTooltipLength];
    }

    private void RebuildMenuIfNeeded(bool forceRebuild = false)
    {
        var signature = BuildMenuSignature();
        if (!forceRebuild && string.Equals(signature, _lastMenuSignature, StringComparison.Ordinal))
        {
            return;
        }

        if (RebuildMenu())
        {
            _lastMenuSignature = signature;
        }
    }

    private string BuildMenuSignature()
    {
        IReadOnlyList<PowerPlanInfo> plans;
        lock (_plansLock)
        {
            plans = _cachedPlans.ToArray();
        }

        var hiddenUltimatePlanGuid = _getHiddenUltimatePlanGuid() ?? string.Empty;
        var builder = new System.Text.StringBuilder();
        builder.Append(_isStartupEnabled() ? '1' : '0');
        builder.Append('|');
        builder.Append(hiddenUltimatePlanGuid);

        for (var i = 0; i < plans.Count; i++)
        {
            var plan = plans[i];
            builder.Append('|');
            builder.Append(plan.Guid);
            builder.Append(',');
            builder.Append(plan.Name);
            builder.Append(',');
            builder.Append(plan.IsActive ? '1' : '0');
        }

        return builder.ToString();
    }

    private bool RebuildMenu()
    {
        return RunOnUiThread(() =>
        {
            if (_contextFlyout is null)
            {
                return;
            }

            _contextFlyout.Items.Clear();
            _contextFlyout.Items.Add(new MenuFlyoutItem
            {
                Text = AppTitleText,
                IsEnabled = false,
                Width = 240
            });
            _contextFlyout.Items.Add(new MenuFlyoutItem
            {
                Text = OpenMainWindowIcon + LocalizationService.Get("Tray.Menu.OpenMainWindow"),
                Command = new RelayCommand(_showMainWindow)
            });
            _contextFlyout.Items.Add(new MenuFlyoutSeparator());

            IReadOnlyList<PowerPlanInfo> plans;
            lock (_plansLock)
            {
                plans = _cachedPlans.ToArray();
            }

            foreach (var plan in plans)
            {
                var planCopy = CopyPlan(plan);
                _contextFlyout.Items.Add(new ToggleMenuFlyoutItem
                {
                    Text = PowerPlanIcon + planCopy.Name,
                    IsChecked = planCopy.IsActive,
                    Command = new RelayCommand(() => _ = OnSwitchPlanAsync(planCopy.Guid, planCopy.Name))
                });
            }

            var hiddenUltimatePlanGuid = _getHiddenUltimatePlanGuid();
            if (!string.IsNullOrWhiteSpace(hiddenUltimatePlanGuid)
                && !plans.Any(plan => string.Equals(plan.Guid, hiddenUltimatePlanGuid, StringComparison.OrdinalIgnoreCase)))
            {
                var ultimatePlanGuid = hiddenUltimatePlanGuid;
                _contextFlyout.Items.Add(new MenuFlyoutItem
                {
                    Text = PowerPlanIcon + LocalizationService.Get("Tray.Menu.OpenHiddenUltimate"),
                    Command = new RelayCommand(() => _ = OnActivateHiddenUltimateAsync(ultimatePlanGuid))
                });
            }

            _contextFlyout.Items.Add(new MenuFlyoutSeparator());
            _contextFlyout.Items.Add(new MenuFlyoutItem
            {
                Text = RefreshPlansIcon + LocalizationService.Get("Tray.Menu.RefreshPlans"),
                Command = new RelayCommand(OnRefreshPlansRequested)
            });
            _contextFlyout.Items.Add(new MenuFlyoutItem
            {
                Text = StartupIcon + (_isStartupEnabled()
                    ? LocalizationService.Get("Tray.Menu.DisableAutoStart")
                    : LocalizationService.Get("Tray.Menu.EnableAutoStart")),
                Command = new RelayCommand(() => _ = ToggleStartupAsync())
            });
            _contextFlyout.Items.Add(new MenuFlyoutSeparator());
            _contextFlyout.Items.Add(new MenuFlyoutItem
            {
                Text = ExitIcon + LocalizationService.Get("Tray.Menu.Exit"),
                Command = new RelayCommand(RequestExit)
            });

            ApplyContextFlyoutTheme();
        });
    }

    private void ApplyContextFlyoutTheme()
    {
        if (_contextFlyout is null)
        {
            return;
        }

        foreach (var item in _contextFlyout.Items)
        {
            item.RequestedTheme = _currentTheme;
        }
    }

    private async Task OnSwitchPlanAsync(string planGuid, string planName)
    {
        try
        {
            await _setActivePlanAsync(planGuid);
            SetActivePlanInCache(planGuid);
            UpdateTaskbarIcon();
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
    }

    private async Task ToggleStartupAsync()
    {
        try
        {
            var next = !_isStartupEnabled();
            _ = await _setStartupEnabled(next);
            UpdateTaskbarIcon();
        }
        catch (Exception ex)
        {
            _log(LocalizationService.Format("Tray.AutoStartToggleFailed", ex.Message), InfoBarSeverity.Error);
        }
    }

    private void OnRefreshPlansRequested()
    {
        _ = RefreshPlansAsync(forceRefresh: true);
        _log(LocalizationService.Get("Tray.RefreshStarted"), InfoBarSeverity.Informational);
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
            _log(LocalizationService.Get("Tray.DispatcherUnavailable"), InfoBarSeverity.Error);
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
            var message = LocalizationService.Get("Tray.DispatcherUnavailable");
            completion.SetException(new InvalidOperationException(message));
        }

        return completion.Task;
    }

    private void RequestExit()
    {
        _ = RequestExitAsync();
    }

    private async Task RequestExitAsync()
    {
        await Task.Delay(300);
        _ = _uiDispatcherQueue.TryEnqueue(() => _exitApplication());
    }

    private static PowerPlanInfo CopyPlan(PowerPlanInfo plan)
    {
        return new PowerPlanInfo
        {
            Guid = plan.Guid,
            Name = plan.Name,
            IsActive = plan.IsActive
        };
    }

    private sealed class RelayCommand(Action execute) : ICommand
    {
        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => execute();

        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
