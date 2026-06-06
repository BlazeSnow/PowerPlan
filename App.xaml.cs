using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Media;
using Microsoft.Windows.AppLifecycle;
using PowerPlan.Models;
using PowerPlan.Services;
using System.Runtime.InteropServices;
using System.Threading;
using Windows.ApplicationModel;
using Windows.UI.ViewManagement;
using WinRT.Interop;
using WinAppInstance = Microsoft.Windows.AppLifecycle.AppInstance;

namespace PowerPlan;

public partial class App : Application
{
    private Window? _window;
    private ShellPage? _shellPage;
    private TrayService? _trayService;
    private readonly PowerPlanService _powerPlanService = new();
    private readonly StartupService _startupService = new();
    private bool _isExiting;
    private bool _lastKnownAutoStart;
    private bool _lastKnownTrayEnabled;
    private bool _pendingMainPageRefresh;
    private int _pendingActivationShowRequested;
    private int _packageUpdateExitRequested;
    private int _exitRequested;
    private DispatcherQueue? _uiDispatcherQueue;
    private readonly UISettings _uiSettings = new();
    private PackageCatalog? _packageCatalog;

    public App()
    {
        InitializeComponent();

        SettingsService = new SettingsService();
        SettingsService.SettingsChanged += OnSettingsChanged;
        _uiSettings.ColorValuesChanged += OnColorValuesChanged;
    }

    public SettingsService SettingsService { get; }
    public PowerPlanService PowerPlanService => _powerPlanService;
    public StartupService StartupService => _startupService;

    protected override async void OnLaunched(LaunchActivatedEventArgs e)
    {
        var mainInstance = WinAppInstance.FindOrRegisterForKey("PowerPlan.Main");
        if (!mainInstance.IsCurrent)
        {
            var activatedArgs = WinAppInstance.GetCurrent().GetActivatedEventArgs();
            await mainInstance.RedirectActivationToAsync(activatedArgs);
            ExitApplicationCore();
            return;
        }

        WinAppInstance.GetCurrent().Activated -= OnAppActivated;
        WinAppInstance.GetCurrent().Activated += OnAppActivated;
        _uiDispatcherQueue = DispatcherQueue.GetForCurrentThread();
        InitializePackageUpdateWatcher();

        var startupTaskLaunch = IsStartupTaskLaunch();

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

        _window ??= new Window();
        var launchToTray = startupTaskLaunch && SettingsService.Current.TrayEnabled;
        _shellPage ??= new ShellPage(navigateToHomeOnStartup: !launchToTray);
        ConfigureWindowAppearance();

        _window.Activate();
        if (launchToTray)
        {
            HideMainWindow();
        }
        if (_window.Content is FrameworkElement rootElement)
        {
            rootElement.ActualThemeChanged -= OnRootActualThemeChanged;
            rootElement.ActualThemeChanged += OnRootActualThemeChanged;
        }
        if (IsMainWindowVisible())
        {
            ApplySystemTitleBarTheme();
        }

        _window.Closed -= OnMainWindowClosed;
        _window.Closed += OnMainWindowClosed;

        await ApplyStartupSettingAsync();
        await EnsureTrayStateAsync();

        if (Interlocked.Exchange(ref _pendingActivationShowRequested, 0) == 1)
        {
            ShowMainWindow();
        }

        // For startup-task launch with tray enabled, window is already hidden before async initialization.
    }

    private void OnAppActivated(object? sender, AppActivationArguments args)
    {
        if (args.Kind == ExtendedActivationKind.StartupTask)
        {
            return;
        }

        RequestShowMainWindow();
    }

    private void RequestShowMainWindow()
    {
        var dispatcherQueue = _window?.DispatcherQueue;
        if (dispatcherQueue is null)
        {
            MarkPendingActivationShow();
            return;
        }

        if (!dispatcherQueue.TryEnqueue(ShowMainWindow))
        {
            MarkPendingActivationShow();
        }
    }

    private void MarkPendingActivationShow()
    {
        Interlocked.Exchange(ref _pendingActivationShowRequested, 1);
    }

    private void InitializePackageUpdateWatcher()
    {
        try
        {
            _packageCatalog ??= PackageCatalog.OpenForCurrentPackage();
            _packageCatalog.PackageUpdating -= OnPackageUpdating;
            _packageCatalog.PackageUpdating += OnPackageUpdating;
        }
        catch
        {
            // Package catalog is only available when the app has package identity.
        }
    }

    private void OnPackageUpdating(PackageCatalog sender, PackageUpdatingEventArgs args)
    {
        if (Interlocked.Exchange(ref _packageUpdateExitRequested, 1) == 1)
        {
            return;
        }

        RequestExitForPackageUpdate();
    }

    private void RequestExitForPackageUpdate()
    {
        RequestExitApplication();
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

        if (_trayService is not null || _window is null)
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

                    await RefreshTrayPlansAsync();
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
        if (_trayService is null)
        {
            return;
        }

        await _trayService.RefreshPlansAsync(forceRefresh);
    }

    private async Task SyncMainPageAfterPlansRefreshAsync()
    {
        var page = GetVisibleMainPage();
        if (page is not null)
        {
            await page.RefreshFromExternalAsync(forceRefresh: true);
        }
        else if (GetMainPage() is not null)
        {
            _pendingMainPageRefresh = true;
        }

    }

    private MainPage? GetMainPage() => _shellPage?.GetMainPage();

    private void OnMainWindowClosed(object sender, WindowEventArgs args)
    {
        if (_isExiting || _window is null)
        {
            return;
        }

        if (SettingsService.Current.TrayEnabled && _trayService is not null)
        {
            args.Handled = true;
            HideMainWindow();
            return;
        }

        args.Handled = true;
        RequestExitApplication();
    }

    private void ExitApplication()
    {
        RequestExitApplication();
    }

    private void RequestExitApplication()
    {
        if (Interlocked.Exchange(ref _exitRequested, 1) == 1)
        {
            return;
        }

        _isExiting = true;
        var dispatcherQueue = _uiDispatcherQueue ?? _window?.DispatcherQueue;
        if (dispatcherQueue is not null && dispatcherQueue.TryEnqueue(async () =>
            {
                await Task.Delay(100);
                ExitApplicationCore();
            }))
        {
            return;
        }

        ExitApplicationCore();
    }

    private void ExitApplicationCore()
    {
        CleanupBeforeExit();
        Exit();
    }

    private void CleanupBeforeExit()
    {
        _isExiting = true;
        _uiSettings.ColorValuesChanged -= OnColorValuesChanged;
        if (_packageCatalog is not null)
        {
            _packageCatalog.PackageUpdating -= OnPackageUpdating;
            _packageCatalog = null;
        }

        _trayService?.Dispose();
        _trayService = null;
    }

    private void ShowMainWindow()
    {
        if (_window is null)
        {
            return;
        }

        if (_shellPage is null)
        {
            return;
        }

        var hwnd = WindowNative.GetWindowHandle(_window);
        _ = ShowWindow(hwnd, 5);
        _window.Activate();
        ApplySystemTitleBarTheme();

        var page = _shellPage.EnsureMainPageLoaded();
        _ = RefreshMainPageAfterShowAsync(page);
    }

    private void HideMainWindow()
    {
        if (_window is null)
        {
            return;
        }

        var hwnd = WindowNative.GetWindowHandle(_window);
        _ = ShowWindow(hwnd, 0);
    }

    private async Task RefreshMainPageAfterShowAsync(MainPage page)
    {
        if (_pendingMainPageRefresh)
        {
            _pendingMainPageRefresh = false;
            await page.RefreshFromExternalAsync(forceRefresh: true);
        }
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
        return page is not null && IsMainWindowVisible() ? page : null;
    }

    private bool IsMainWindowVisible()
    {
        if (_window is null)
        {
            return false;
        }

        var hwnd = WindowNative.GetWindowHandle(_window);
        return IsWindowVisible(hwnd);
    }

    private void ConfigureWindowAppearance()
    {
        if (_window is null)
        {
            return;
        }

        _window.Title = LocalizationService.Get("App.WindowTitle", "PowerPlan");
        ConfigureWindowContent();
        ApplySystemBackdrop();
        SetWindowIcon();
    }

    private void ConfigureWindowContent()
    {
        if (_window is null || _shellPage is null)
        {
            return;
        }

        _window.ExtendsContentIntoTitleBar = true;
        _window.Content = _shellPage;
        _window.SetTitleBar(_shellPage.AppTitleBarElement);
    }

    private void SetWindowIcon()
    {
        if (_window is null)
        {
            return;
        }

        try
        {
            var hwnd = WindowNative.GetWindowHandle(_window);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);

            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "powerplan.ico");
            if (!File.Exists(iconPath))
            {
                return;
            }

            appWindow.SetIcon(iconPath);
        }
        catch
        {
            // Ignore icon setup failures to avoid affecting startup flow.
        }
    }

    private void ApplySystemBackdrop()
    {
        if (_window is null)
        {
            return;
        }

        try
        {
            _window.SystemBackdrop = new MicaBackdrop();
        }
        catch
        {
            // Keep window creation resilient if the current system does not support Mica.
        }
    }

    private void OnRootActualThemeChanged(FrameworkElement sender, object args)
    {
        if (IsMainWindowVisible())
        {
            ApplySystemTitleBarTheme();
        }
    }

    private void OnColorValuesChanged(UISettings sender, object args)
    {
        if (_window is null)
        {
            return;
        }

        var dispatcherQueue = _window.DispatcherQueue;
        if (dispatcherQueue.HasThreadAccess)
        {
            if (IsMainWindowVisible())
            {
                ApplySystemTitleBarTheme();
            }

            return;
        }

        _ = dispatcherQueue.TryEnqueue(() =>
        {
            if (IsMainWindowVisible())
            {
                ApplySystemTitleBarTheme();
            }
        });
    }

    private ElementTheme GetEffectiveTheme()
    {
        if (_window?.Content is FrameworkElement root && root.ActualTheme != ElementTheme.Default)
        {
            return root.ActualTheme;
        }

        return IsSystemUsingDarkTheme() ? ElementTheme.Dark : ElementTheme.Light;
    }

    private bool IsSystemUsingDarkTheme()
    {
        try
        {
            var background = _uiSettings.GetColorValue(UIColorType.Background);
            return background.R < 128 && background.G < 128 && background.B < 128;
        }
        catch
        {
            return false;
        }
    }

    private void ApplySystemTitleBarTheme()
    {
        if (_window is null)
        {
            return;
        }

        var hwnd = WindowNative.GetWindowHandle(_window);
        var useDarkMode = GetEffectiveTheme() == ElementTheme.Dark ? 1 : 0;
        var size = Marshal.SizeOf<int>();

        var result = DwmSetWindowAttribute(hwnd, DwmaUseImmersiveDarkMode, ref useDarkMode, size);
        if (result != 0)
        {
            _ = DwmSetWindowAttribute(hwnd, DwmaUseImmersiveDarkModeBefore20H1, ref useDarkMode, size);
        }

        ApplyCaptionButtonTheme(useDarkMode == 1);
    }

    private void ApplyCaptionButtonTheme(bool isDark)
    {
        if (_window is null)
        {
            return;
        }

        try
        {
            if (!AppWindowTitleBar.IsCustomizationSupported())
            {
                return;
            }

            var hwnd = WindowNative.GetWindowHandle(_window);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);

            var foreground = isDark
                ? Windows.UI.Color.FromArgb(255, 255, 255, 255)
                : Windows.UI.Color.FromArgb(255, 0, 0, 0);
            var inactiveForeground = isDark
                ? Windows.UI.Color.FromArgb(160, 255, 255, 255)
                : Windows.UI.Color.FromArgb(160, 0, 0, 0);
            var hoverBackground = isDark
                ? Windows.UI.Color.FromArgb(32, 255, 255, 255)
                : Windows.UI.Color.FromArgb(24, 0, 0, 0);
            var pressedBackground = isDark
                ? Windows.UI.Color.FromArgb(48, 255, 255, 255)
                : Windows.UI.Color.FromArgb(36, 0, 0, 0);

            appWindow.TitleBar.ButtonBackgroundColor = Windows.UI.Color.FromArgb(0, 0, 0, 0);
            appWindow.TitleBar.ButtonInactiveBackgroundColor = Windows.UI.Color.FromArgb(0, 0, 0, 0);
            appWindow.TitleBar.ButtonHoverBackgroundColor = hoverBackground;
            appWindow.TitleBar.ButtonPressedBackgroundColor = pressedBackground;
            appWindow.TitleBar.ButtonForegroundColor = foreground;
            appWindow.TitleBar.ButtonInactiveForegroundColor = inactiveForeground;
        }
        catch
        {
            // Ignore title bar button theme failures to avoid affecting startup flow.
        }
    }

    private static bool IsStartupTaskLaunch()
    {
        try
        {
            return WinAppInstance.GetCurrent().GetActivatedEventArgs().Kind == ExtendedActivationKind.StartupTask;
        }
        catch
        {
            return false;
        }
    }

    private const uint DwmaUseImmersiveDarkMode = 20;
    private const uint DwmaUseImmersiveDarkModeBefore20H1 = 19;

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint hWnd);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, uint dwAttribute, ref int pvAttribute, int cbAttribute);

}
