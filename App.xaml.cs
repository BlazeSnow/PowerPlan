using Microsoft.UI.Dispatching;
using PowerPlan.Models;
using PowerPlan.Services;
using Windows.Globalization;

namespace PowerPlan;

public partial class App : Application
{
    private readonly PowerPlanService _powerPlanService = new();
    private readonly StartupService _startupService = new();
    private readonly WindowService _windowService = new();
    private readonly ActivationService _activationService;
    private readonly PackageUpdateService _packageUpdateService;
    private ShellPage? _shellPage;
    private TrayService? _trayService;
    private bool _isExiting;
    private bool _lastKnownAutoStart;
    private bool _lastKnownTrayEnabled;
    private bool _pendingMainPageRefresh;
    private IReadOnlyList<PowerPlanInfo>? _pendingMainPagePlans;

    public App()
    {
        try
        {
            ApplicationLanguages.PrimaryLanguageOverride = SettingsService.LoadLanguageSynchronously();
        }
        catch
        {
            // Language preference must never prevent the app from starting.
        }

        InitializeComponent();

        SettingsService = new SettingsService();
        SettingsService.SettingsChanged += OnSettingsChanged;
        _activationService = new ActivationService(() => _windowService.DispatcherQueue, ShowMainWindow);
        _packageUpdateService = new PackageUpdateService(ExitApplication);
    }

    public SettingsService SettingsService { get; }
    public PowerPlanService PowerPlanService => _powerPlanService;
    public StartupService StartupService => _startupService;

    protected override async void OnLaunched(LaunchActivatedEventArgs e)
    {
        if (!await _activationService.InitializeAsync())
        {
            Exit();
            return;
        }

        _packageUpdateService.Initialize();
        var startupTaskLaunch = _activationService.IsStartupTaskLaunch;

        try
        {
            await SettingsService.InitializeAsync();
        }
        catch
        {
            // Keep app startup resilient even when settings file cannot be loaded.
        }

        _lastKnownAutoStart = SettingsService.Current.AutoStart;
        _lastKnownTrayEnabled = SettingsService.Current.TrayEnabled;

        var window = _windowService.EnsureWindowCreated();
        var launchToTray = startupTaskLaunch && SettingsService.Current.TrayEnabled;

        if (launchToTray)
        {
            // Defer ShellPage and window content creation — only tray icon is needed.
        }
        else
        {
            EnsureShellPageCreated();
            window.Activate();
        }

        window.Closed -= OnMainWindowClosed;
        window.Closed += OnMainWindowClosed;

        await ApplyStartupSettingAsync();
        await EnsureTrayStateAsync();
        _activationService.ShowPendingActivationIfRequested();

        // For startup-task launch with tray enabled, window was never activated so it stays hidden.
    }

    private async void OnSettingsChanged(object? sender, AppSettings e)
    {
        var autoStartChanged = e.AutoStart != _lastKnownAutoStart;
        var trayChanged = e.TrayEnabled != _lastKnownTrayEnabled;

        _lastKnownAutoStart = e.AutoStart;
        _lastKnownTrayEnabled = e.TrayEnabled;

        if (autoStartChanged)
        {
            await ApplyStartupSettingAsync();
            _trayService?.UpdateStatus();
        }

        if (trayChanged)
        {
            await EnsureTrayStateAsync();
        }
    }

    private async Task ApplyStartupSettingAsync()
    {
        try
        {
            var expected = SettingsService.Current.AutoStart;

            if (expected)
            {
                // Keep desired=true stable here. Reading StartupTask state immediately after
                // user-initiated enable can be transiently false and would wrongly revert settings.
                _ = await _startupService.GetEffectiveEnabledAsync();
                return;
            }

            var effective = await _startupService.SetEnabledAsync(false);
            if (effective != expected)
            {
                SettingsService.Current.AutoStart = effective;
                _lastKnownAutoStart = effective;
                await SettingsService.SaveCurrentAsync();
            }
        }
        catch (Exception ex)
        {
            AddStatusToVisibleMainPage(LocalizationService.Format("App.Status.StartupSettingFailed", ex.Message), true);
        }
    }

    private async Task EnsureTrayStateAsync()
    {
        var shouldEnableTray = SettingsService.Current.TrayEnabled;

        if (!shouldEnableTray)
        {
            _trayService?.Dispose();
            _trayService = null;
            return;
        }

        if (_trayService is not null || _windowService.Window is null)
        {
            return;
        }

        var uiDispatcherQueue = DispatcherQueue.GetForCurrentThread();
        if (uiDispatcherQueue is null)
        {
            AddStatusToVisibleMainPage(LocalizationService.Get("Tray.DispatcherUnavailable"), true);
            return;
        }

        _trayService = new TrayService(
            uiDispatcherQueue: uiDispatcherQueue,
            getPlansAsync: forceRefresh => _powerPlanService.GetPlansAsync(forceRefresh),
            setActivePlanAsync: async guid =>
            {
                await _powerPlanService.SetActivePlanAsync(guid);

                var page = GetVisibleMainPage();
                if (page is not null)
                {
                    if (!page.TryApplyActivePlanFromExternal(guid))
                    {
                        await page.RefreshFromExternalAsync(forceRefresh: true);
                    }

                    page.AddExternalStatus(LocalizationService.Format("App.Status.TraySwitched", guid), InfoBarSeverity.Success);
                }
                else
                {
                    _pendingMainPageRefresh = true;
                    _pendingMainPagePlans = null;
                }
            },
            getHiddenUltimatePlanGuid: () =>
            {
                var guid = SettingsService.Current.UltimatePerformancePlanGuid;
                return string.IsNullOrWhiteSpace(guid) ? null : guid;
            },
            activateHiddenUltimatePlanAsync: async guid =>
            {
                try
                {
                    await _powerPlanService.SetActivePlanAsync(guid);
                    await RefreshTrayPlansAsync(forceRefresh: true);
                }
                catch
                {
                    SettingsService.Current.UltimatePerformancePlanGuid = string.Empty;
                    try
                    {
                        await SettingsService.SaveCurrentAsync();
                    }
                    catch
                    {
                        // Keep tray activation failure focused on the power plan operation.
                    }

                    await RefreshTrayPlansAsync(forceRefresh: true);
                    throw;
                }
            },
            isStartupEnabled: () => SettingsService.Current.AutoStart,
            setStartupEnabled: UpdateAutoStartFromTrayAsync,
            onPlansRefreshed: SyncMainPageAfterPlansRefreshAsync,
            showMainWindow: ShowMainWindow,
            exitApplication: ExitApplication,
            log: (message, severity) => AddStatusToVisibleMainPage(message, severity));

        try
        {
            await _trayService.InitializeAsync();
        }
        catch (Exception ex)
        {
            AddStatusToVisibleMainPage(LocalizationService.Format("App.Status.TrayInitFailed", ex.Message), true);
            _trayService?.Dispose();
            _trayService = null;
        }
    }

    private async Task<bool> UpdateAutoStartFromTrayAsync(bool enabled)
    {
        try
        {
            var effective = await _startupService.SetEnabledAsync(enabled);
            SettingsService.Current.AutoStart = effective;
            _lastKnownAutoStart = effective;
            await SettingsService.SaveCurrentAsync();
            var state = LocalizationService.Get(effective ? "App.Status.On" : "App.Status.Off");
            AddStatusToVisibleMainPage(LocalizationService.Format("App.Status.TrayAutoStart", state), InfoBarSeverity.Success);
            return effective;
        }
        catch (Exception ex)
        {
            AddStatusToVisibleMainPage(LocalizationService.Format("App.Status.TrayAutoStartFailed", ex.Message), true);
            return SettingsService.Current.AutoStart;
        }
    }

    public void UpdateTrayPlans(IReadOnlyList<PowerPlanInfo> plans)
    {
        _trayService?.UpdatePlansSnapshot(plans);
    }

    public async Task RefreshTrayPlansAsync()
    {
        await RefreshTrayPlansAsync(forceRefresh: false);
    }

    public async Task RefreshTrayPlansAsync(bool forceRefresh)
    {
        if (_trayService is not null)
        {
            await _trayService.RefreshPlansAsync(forceRefresh);
        }
    }

    private Task SyncMainPageAfterPlansRefreshAsync(IReadOnlyList<PowerPlanInfo> plans)
    {
        var page = GetVisibleMainPage();
        if (page is not null)
        {
            page.ApplyPlansFromExternalSnapshot(plans);
        }
        else if (GetMainPage() is not null)
        {
            _pendingMainPageRefresh = true;
            _pendingMainPagePlans = plans;
        }

        return Task.CompletedTask;
    }

    private MainPage? GetMainPage() => _shellPage?.GetMainPage();

    private void OnMainWindowClosed(object sender, WindowEventArgs args)
    {
        if (_isExiting || _windowService.Window is null)
        {
            return;
        }

        if (SettingsService.Current.TrayEnabled && _trayService is not null)
        {
            args.Handled = true;
            _windowService.Hide();
            return;
        }

        args.Handled = true;
        ExitApplication();
    }

    private void ExitApplication()
    {
        if (_isExiting)
        {
            return;
        }

        _isExiting = true;
        SettingsService.SettingsChanged -= OnSettingsChanged;
        _activationService.Dispose();
        _packageUpdateService.Dispose();
        _trayService?.Dispose();
        _trayService = null;

        Environment.Exit(0);
    }

    private void ShowMainWindow()
    {
        if (_windowService.Window is null)
        {
            return;
        }

        EnsureShellPageCreated();
        if (_shellPage is null)
        {
            return;
        }

        _windowService.Show();

        var page = _shellPage.EnsureMainPageLoaded();
        _ = RefreshMainPageAfterShowAsync(page);
    }

    private void EnsureShellPageCreated()
    {
        if (_shellPage is not null)
        {
            return;
        }

        _shellPage = new ShellPage(navigateToHomeOnStartup: true);
        _windowService.Configure(_shellPage);
    }

    private async Task RefreshMainPageAfterShowAsync(MainPage page)
    {
        if (!_pendingMainPageRefresh)
        {
            return;
        }

        _pendingMainPageRefresh = false;
        var pendingPlans = _pendingMainPagePlans;
        _pendingMainPagePlans = null;
        if (pendingPlans is not null)
        {
            page.ApplyPlansFromExternalSnapshot(pendingPlans);
            return;
        }

        await page.RefreshFromExternalAsync(forceRefresh: true);
    }

    private void AddStatusToVisibleMainPage(string message, bool isError = false)
    {
        var page = GetVisibleMainPage();
        if (page is not null)
        {
            page.AddExternalStatus(message, isError);
        }
    }

    private void AddStatusToVisibleMainPage(string message, InfoBarSeverity severity)
    {
        var page = GetVisibleMainPage();
        if (page is not null)
        {
            page.AddExternalStatus(message, severity);
        }
    }

    private MainPage? GetVisibleMainPage()
    {
        var page = GetMainPage();
        return page is not null && _windowService.IsVisible ? page : null;
    }
}
