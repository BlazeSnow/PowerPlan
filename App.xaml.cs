using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Media;
using Microsoft.Windows.AppLifecycle;
using PowerPlan.Models;
using PowerPlan.Services;
using System.Runtime.InteropServices;
using Windows.UI.ViewManagement;

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
    private nint _windowIconHandle;
    private readonly UISettings _uiSettings = new();

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

        await ApplyStartupSettingAsync();
        if (startupTaskLaunch && SettingsService.Current.TrayEnabled)
        {
            await EnsureTrayStateAsync();
            return;
        }

        ShowMainWindow();
        await EnsureTrayStateAsync();
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
                var effectiveEnabled = await _startupService.GetEffectiveEnabledAsync();
                if (!effectiveEnabled)
                {
                    SettingsService.Current.AutoStart = false;
                    _lastKnownAutoStart = false;
                    await SettingsService.SaveCurrentAsync();
                }

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

        if (_trayService is not null)
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
                    return;
                }

                _pendingMainPageRefresh = true;
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
            isDarkTheme: () => GetEffectiveTheme() == ElementTheme.Dark,
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
            return;
        }

        _pendingMainPageRefresh = true;
    }

    private MainPage? GetMainPage() => _shellPage?.GetMainPage();

    private void OnMainWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_isExiting || _window is null)
        {
            return;
        }

        if (SettingsService.Current.TrayEnabled && _trayService is not null)
        {
            args.Cancel = true;
            HideMainWindowIfCreated();
            _pendingMainPageRefresh = true;
            return;
        }

        args.Cancel = true;
        HideMainWindowIfCreated();
        _ = _window.DispatcherQueue.TryEnqueue(ExitApplication);
    }

    private void ExitApplication()
    {
        _isExiting = true;
        HideMainWindowIfCreated();
        _uiSettings.ColorValuesChanged -= OnColorValuesChanged;
        _trayService?.Dispose();
        _trayService = null;

        CloseMainWindowForExit();
        DestroyWindowIcon();
        Exit();
    }

    private void ShowMainWindow()
    {
        var created = EnsureMainWindowCreated(navigateToHomeOnStartup: true);

        if (_window is null || _shellPage is null)
        {
            return;
        }

        if (!created)
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_window);
            _ = ShowWindow(hwnd, SwShow);
        }

        _window.Activate();
        ApplySystemTitleBarTheme();

        if (!created || _pendingMainPageRefresh)
        {
            var page = _shellPage.EnsureMainPageLoaded();
            _ = RefreshMainPageAfterShowAsync(page);
        }
    }

    private bool EnsureMainWindowCreated(bool navigateToHomeOnStartup)
    {
        if (_window is not null && _shellPage is not null)
        {
            return false;
        }

        _window ??= new Window();
        _shellPage ??= new ShellPage(navigateToHomeOnStartup: navigateToHomeOnStartup);
        _window.Content = _shellPage;
        ConfigureWindowAppearance();

        if (_window.Content is FrameworkElement rootElement)
        {
            rootElement.ActualThemeChanged -= OnRootActualThemeChanged;
            rootElement.ActualThemeChanged += OnRootActualThemeChanged;
        }

        _window.AppWindow.Closing -= OnMainWindowClosing;
        _window.AppWindow.Closing += OnMainWindowClosing;
        return true;
    }

    private void CloseMainWindowForExit()
    {
        if (_window is null)
        {
            return;
        }

        if (_window.Content is FrameworkElement rootElement)
        {
            rootElement.ActualThemeChanged -= OnRootActualThemeChanged;
        }

        _window.AppWindow.Closing -= OnMainWindowClosing;
        _window.Close();
        _window = null;
        _shellPage = null;
    }

    private void HideMainWindowIfCreated()
    {
        if (_window is null)
        {
            return;
        }

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_window);
        _ = ShowWindow(hwnd, SwHide);
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

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_window);
        return IsWindowVisible(hwnd);
    }

    private void ConfigureWindowAppearance()
    {
        if (_window is null)
        {
            return;
        }

        _window.Title = LocalizationService.Get("App.WindowTitle", "PowerPlan");
        ApplySystemBackdrop();

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_window);
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "powerplan.ico");
        if (!File.Exists(iconPath))
        {
            return;
        }

        DestroyWindowIcon();
        _windowIconHandle = LoadImage(IntPtr.Zero, iconPath, ImageIcon, 0, 0, LrLoadFromFile);
        if (_windowIconHandle == IntPtr.Zero)
        {
            return;
        }

        _ = SendMessage(hwnd, WmSetIcon, (nint)IconSmall, _windowIconHandle);
        _ = SendMessage(hwnd, WmSetIcon, (nint)IconBig, _windowIconHandle);
    }

    private void DestroyWindowIcon()
    {
        if (_windowIconHandle == IntPtr.Zero)
        {
            return;
        }

        _ = DestroyIcon(_windowIconHandle);
        _windowIconHandle = IntPtr.Zero;
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

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_window);
        var useDarkMode = GetEffectiveTheme() == ElementTheme.Dark ? 1 : 0;
        var size = Marshal.SizeOf<int>();

        var result = DwmSetWindowAttribute(hwnd, DwmaUseImmersiveDarkMode, ref useDarkMode, size);
        if (result != 0)
        {
            _ = DwmSetWindowAttribute(hwnd, DwmaUseImmersiveDarkModeBefore20H1, ref useDarkMode, size);
        }
    }

    private static bool IsStartupTaskLaunch()
    {
        try
        {
            return AppInstance.GetCurrent().GetActivatedEventArgs().Kind == ExtendedActivationKind.StartupTask;
        }
        catch
        {
            return false;
        }
    }

    private const uint WmSetIcon = 0x0080;
    private const int IconSmall = 0;
    private const int IconBig = 1;
    private const uint ImageIcon = 1;
    private const uint LrLoadFromFile = 0x00000010;
    private const uint DwmaUseImmersiveDarkMode = 20;
    private const uint DwmaUseImmersiveDarkModeBefore20H1 = 19;
    private const int SwHide = 0;
    private const int SwShow = 5;

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint LoadImage(nint hinst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SendMessage(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint hIcon);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, uint dwAttribute, ref int pvAttribute, int cbAttribute);

}
