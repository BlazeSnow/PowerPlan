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
    private bool _disposed;

    public TrayService(
        DispatcherQueue uiDispatcherQueue,
        Func<bool, Task<IReadOnlyList<PowerPlanInfo>>> getPlansAsync,
        Func<string, Task> setActivePlanAsync,
        Func<string?> getHiddenUltimatePlanGuid,
        Func<string, Task> activateHiddenUltimatePlanAsync,
        Func<bool> isStartupEnabled,
        Func<bool, Task<bool>> setStartupEnabled,
        Func<bool> isDarkTheme,
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

        EnsureTaskbarIcon();
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

        UpdateTaskbarIcon();
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
        try
        {
            if (_taskbarIcon is not null)
            {
                _taskbarIcon.ContextFlyout = null;
                _taskbarIcon.Dispose();
            }
        }
        catch
        {
            // The app is exiting; do not let tray cleanup failures crash shutdown.
        }

        _taskbarIcon = null;
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

        _taskbarIcon = new TaskbarIcon
        {
            IconSource = new BitmapImage(new Uri("ms-appx:///Assets/powerplan.ico")),
            MenuActivation = PopupActivationMode.LeftOrRightClick,
            ContextMenuMode = ContextMenuMode.PopupMenu,
            NoLeftClickDelay = true,
            Visibility = Visibility.Visible,
            ToolTipText = BuildTooltipText(),
            ContextFlyout = BuildContextMenu()
        };
        _taskbarIcon.ForceCreate();
    }

    private void UpdateTaskbarIcon()
    {
        if (_taskbarIcon is null || _disposed)
        {
            return;
        }

        _taskbarIcon.ToolTipText = BuildTooltipText();
        _taskbarIcon.ContextFlyout = BuildContextMenu();
    }

    private MenuFlyout BuildContextMenu()
    {
        var menu = new MenuFlyout
        {
            AreOpenCloseAnimationsEnabled = false
        };

        menu.Items.Add(new MenuFlyoutItem
        {
            Text = AppTitleText,
            IsEnabled = false,
            Width = 240
        });
        menu.Items.Add(new MenuFlyoutItem
        {
            Text = OpenMainWindowIcon + LocalizationService.Get("Tray.Menu.OpenMainWindow"),
            Command = new RelayCommand(_showMainWindow)
        });
        menu.Items.Add(new MenuFlyoutSeparator());

        IReadOnlyList<PowerPlanInfo> plans;
        lock (_plansLock)
        {
            plans = _cachedPlans.ToArray();
        }

        foreach (var plan in plans)
        {
            var planCopy = CopyPlan(plan);
            menu.Items.Add(new ToggleMenuFlyoutItem
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
            menu.Items.Add(new MenuFlyoutItem
            {
                Text = PowerPlanIcon + LocalizationService.Get("Tray.Menu.OpenHiddenUltimate"),
                Command = new RelayCommand(() => _ = OnActivateHiddenUltimateAsync(ultimatePlanGuid))
            });
        }

        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(new MenuFlyoutItem
        {
            Text = RefreshPlansIcon + LocalizationService.Get("Tray.Menu.RefreshPlans"),
            Command = new RelayCommand(OnRefreshPlansRequested)
        });
        menu.Items.Add(new MenuFlyoutItem
        {
            Text = StartupIcon + (_isStartupEnabled()
                ? LocalizationService.Get("Tray.Menu.DisableAutoStart")
                : LocalizationService.Get("Tray.Menu.EnableAutoStart")),
            Command = new RelayCommand(() => _ = ToggleStartupAsync())
        });
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(new MenuFlyoutItem
        {
            Text = ExitIcon + LocalizationService.Get("Tray.Menu.Exit"),
            Command = new RelayCommand(RequestExit)
        });

        return menu;
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

    private void RequestExit()
    {
        _exitApplication();
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
