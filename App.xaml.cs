using Microsoft.UI.Dispatching;
using PowerPlan.Models;
using PowerPlan.Services;
using PowerPlan.Tray.Services;
using Windows.Globalization;

namespace PowerPlan;

public partial class App : Application, IPageHost
{
    private readonly IPowerPlanService _powerPlanService;
    private readonly StartupService _startupService = new();
    private readonly WindowService _windowService = new();
    private readonly ActivationService _activationService;
    private readonly PackageUpdateService _packageUpdateService;
    private ShellPage? _shellPage;
    private TrayCoordinator? _trayCoordinator;
    private DispatcherQueue? _uiDispatcherQueue;
    private bool _isExiting;
    private bool _lastKnownAutoStart;
    private bool _lastKnownTrayEnabled;
    private bool _pendingMainPageRefresh;
    private IReadOnlyList<PowerPlanInfo>? _pendingMainPagePlans;

    public App()
    {
        try
        {
            ApplicationLanguages.PrimaryLanguageOverride = SettingsLanguageLoader.LoadSynchronously();
        }
        catch
        {
            // Language preference must never prevent the app from starting.
        }

        InitializeComponent();

        _powerPlanService = new PowerPlanService(
            new WindowsPowerSchemeNativeApi(),
            new LocalizedPowerPlanErrorFormatter());
        SettingsService = new SettingsService(
            new WindowsSettingsStore(),
            new WindowsLegacySettingsStore(),
            new WindowsLanguagePreferenceProvider());
        SettingsService.SettingsChanged += OnSettingsChanged;
        _activationService = new ActivationService(() => _uiDispatcherQueue, ShowMainWindow);
        _packageUpdateService = new PackageUpdateService(ExitApplication);
    }

    public ISettingsService SettingsService { get; }
    public IPowerPlanService PowerPlanService => _powerPlanService;
    public StartupService StartupService => _startupService;
    public IStartupTaskService StartupTaskService => _startupService;

    public string GetString(string key) => LocalizationService.Get(key);

    public string FormatString(string key, params object[] arguments) => LocalizationService.Format(key, arguments);

    public string GetStringForLanguage(string key, string language) => LocalizationService.GetForLanguage(key, language);

    protected override async void OnLaunched(LaunchActivatedEventArgs e)
    {
        _uiDispatcherQueue ??= DispatcherQueue.GetForCurrentThread();

        if (!await _activationService.InitializeAsync())
        {
            Exit();
            return;
        }

        _packageUpdateService.Initialize();
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

        var launchToTray = SettingsService.Current.TrayEnabled && SettingsService.Current.LaunchToTray;
        if (!launchToTray)
        {
            var window = EnsureWindowAndShellCreated();
            window.Activate();
        }

        await ApplyStartupSettingAsync();
        await EnsureTrayStateAsync();
        _activationService.ShowPendingActivationIfRequested();

        // Startup-task launch with tray enabled creates neither a window nor the Shell/Page tree.
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
            _trayCoordinator?.UpdateStatus();
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
        if (!SettingsService.Current.TrayEnabled)
        {
            if (_trayCoordinator is not null)
            {
                await _trayCoordinator.DisableAsync();
            }

            return;
        }

        if (_uiDispatcherQueue is null)
        {
            AddStatusToVisibleMainPage(LocalizationService.Get("Tray.DispatcherUnavailable"), true);
            return;
        }

        _trayCoordinator ??= new TrayCoordinator(
            _uiDispatcherQueue,
            _powerPlanService,
            SettingsService,
            _startupService,
            new TrayLocalizer(),
            ShowMainWindow,
            ExitApplication,
            ApplyActivePlanFromTrayAsync,
            SyncMainPageAfterPlansRefreshAsync,
            AddStatusToVisibleMainPage);

        try
        {
            await _trayCoordinator.EnsureEnabledAsync();
        }
        catch (Exception ex)
        {
            AddStatusToVisibleMainPage(LocalizationService.Format("App.Status.TrayInitFailed", ex.Message), true);
        }
    }

    public void UpdateTrayPlans(IReadOnlyList<PowerPlanInfo> plans)
    {
        _trayCoordinator?.UpdatePlansSnapshot(plans);
    }

    public Task RefreshTrayPlansAsync()
    {
        return RefreshTrayPlansAsync(forceRefresh: false);
    }

    public async Task RefreshTrayPlansAsync(bool forceRefresh)
    {
        if (_trayCoordinator is not null)
        {
            await _trayCoordinator.RefreshPlansAsync(forceRefresh);
        }
    }

    private async Task ApplyActivePlanFromTrayAsync(string guid)
    {
        var page = GetVisibleMainPage();
        if (page is not null)
        {
            if (!page.TryApplyActivePlanFromExternal(guid))
            {
                await page.RefreshFromExternalAsync(forceRefresh: true);
            }

            return;
        }

        _pendingMainPageRefresh = true;
        _pendingMainPagePlans = null;
    }

    private Task SyncMainPageAfterPlansRefreshAsync(IReadOnlyList<PowerPlanInfo> plans)
    {
        var page = GetVisibleMainPage();
        if (page is not null)
        {
            page.ApplyPlansFromExternalSnapshot(plans);
        }
        else
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

        if (SettingsService.Current.TrayEnabled && _trayCoordinator?.IsEnabled == true)
        {
            args.Handled = true;
            ReleaseMainUiToTray();
            return;
        }

        args.Handled = true;
        ExitApplication();
    }

    private void ReleaseMainUiToTray()
    {
        var window = _windowService.Window;
        if (window is null)
        {
            return;
        }

        _windowService.Hide();
        window.SetTitleBar(null);
        window.Content = null;
        _shellPage = null;
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
        _trayCoordinator?.Dispose();
        _trayCoordinator = null;

        Environment.Exit(0);
    }

    private void ShowMainWindow()
    {
        var window = EnsureWindowAndShellCreated();
        _windowService.Show();

        var page = _shellPage!.EnsureMainPageLoaded();
        _ = RefreshMainPageAfterShowAsync(page);
    }

    private Window EnsureWindowAndShellCreated()
    {
        var window = _windowService.EnsureWindowCreated();
        window.Closed -= OnMainWindowClosed;
        window.Closed += OnMainWindowClosed;

        EnsureShellPageCreated();
        return window;
    }

    private void EnsureShellPageCreated()
    {
        if (_shellPage is not null)
        {
            return;
        }

        _shellPage = new ShellPage(this, navigateToHomeOnStartup: true);
        _windowService.Configure(_shellPage);
    }

    private async Task RefreshMainPageAfterShowAsync(MainPage page)
    {
        if (!ReferenceEquals(page, GetVisibleMainPage()))
        {
            return;
        }

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
