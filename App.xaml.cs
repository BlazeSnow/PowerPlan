using PowerPlan.Models;
using PowerPlan.Services;
using PowerPlan.Views;
using Microsoft.Windows.AppLifecycle;
using System.Runtime.InteropServices;

namespace PowerPlan;

public partial class App : Application
{
    private Window? _window;
    private ShellPage? _shellPage;
    private TrayService? _trayService;
    private readonly PowerPlanService _powerPlanService = new();
    private readonly StartupService _startupService = new();
    private bool _isExiting;
    private nint _windowIconHandle;

    public App()
    {
        InitializeComponent();

        SettingsService = new SettingsService();
        SettingsService.SettingsChanged += OnSettingsChanged;
    }

    public SettingsService SettingsService { get; }

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

        _window ??= new Window();
        _shellPage ??= new ShellPage();
        _window.Content = _shellPage;
        ConfigureWindowAppearance();

        _window.Activate();
        if (startupTaskLaunch && SettingsService.Current.TrayEnabled)
        {
            HideMainWindow();
        }
        if (_window.Content is FrameworkElement rootElement)
        {
            rootElement.ActualThemeChanged -= OnRootActualThemeChanged;
            rootElement.ActualThemeChanged += OnRootActualThemeChanged;
        }
        ApplySystemTitleBarTheme();
        _window.Closed -= OnMainWindowClosed;
        _window.Closed += OnMainWindowClosed;

        await ApplyStartupSettingAsync();
        await EnsureTrayStateAsync();

        // For startup-task launch with tray enabled, window is already hidden before async initialization.
    }

    private async void OnSettingsChanged(object? sender, AppSettings e)
    {
        await ApplyStartupSettingAsync();
        await EnsureTrayStateAsync();
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
                await SettingsService.SaveCurrentAsync();
            }
        }
        catch (Exception ex)
        {
            GetMainPage()?.AddExternalStatus(LocalizationService.Format("App.Status.StartupSettingFailed", ex.Message), true);
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

        _trayService = new TrayService(
            getPlansAsync: _powerPlanService.GetPlansAsync,
            setActivePlanAsync: async guid =>
            {
                await _powerPlanService.SetActivePlanAsync(guid);
                var page = GetMainPage();
                if (page is not null)
                {
                    if (!page.TryApplyActivePlanFromExternal(guid))
                    {
                        await page.RefreshFromExternalAsync();
                    }

                    page.AddExternalStatus(LocalizationService.Format("App.Status.TraySwitched", guid));
                }
            },
            isStartupEnabled: () => SettingsService.Current.AutoStart,
            setStartupEnabled: UpdateAutoStartFromTrayAsync,
            showMainWindow: ShowMainWindow,
            exitApplication: ExitApplication,
            log: message => GetMainPage()?.AddExternalStatus(
                message,
                message.Contains(LocalizationService.Get("Common.FailedKeyword"), StringComparison.Ordinal)));

        try
        {
            await _trayService.InitializeAsync();
        }
        catch (Exception ex)
        {
            GetMainPage()?.AddExternalStatus(LocalizationService.Format("App.Status.TrayInitFailed", ex.Message), true);
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
            await SettingsService.SaveCurrentAsync();
            var state = LocalizationService.Get(effective ? "App.Status.On" : "App.Status.Off");
            GetMainPage()?.AddExternalStatus(LocalizationService.Format("App.Status.TrayAutoStart", state));
            return effective;
        }
        catch (Exception ex)
        {
            GetMainPage()?.AddExternalStatus(LocalizationService.Format("App.Status.TrayAutoStartFailed", ex.Message), true);
            return SettingsService.Current.AutoStart;
        }
    }


    public void UpdateTrayPlans(IReadOnlyList<PowerPlanInfo> plans)
    {
        _trayService?.UpdatePlansSnapshot(plans);
    }

    public async Task RefreshTrayPlansAsync()
    {
        if (_trayService is null)
        {
            return;
        }

        await _trayService.RefreshPlansAsync();
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
        }
    }

    private void ExitApplication()
    {
        _isExiting = true;
        _trayService?.Dispose();
        _trayService = null;
        if (_windowIconHandle != IntPtr.Zero)
        {
            _ = DestroyIcon(_windowIconHandle);
            _windowIconHandle = IntPtr.Zero;
        }
        Exit();
    }

    private void ShowMainWindow()
    {
        if (_window is null)
        {
            return;
        }

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_window);
        _ = ShowWindow(hwnd, 5);
        _window.Activate();
    }

    private void HideMainWindow()
    {
        if (_window is null)
        {
            return;
        }

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_window);
        _ = ShowWindow(hwnd, 0);
    }

    private void ConfigureWindowAppearance()
    {
        if (_window is null)
        {
            return;
        }

        _window.Title = LocalizationService.Get("App.WindowTitle", "PowerPlan");

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_window);
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "powerplan.ico");
        if (!File.Exists(iconPath))
        {
            return;
        }

        _windowIconHandle = LoadImage(IntPtr.Zero, iconPath, ImageIcon, 0, 0, LrLoadFromFile);
        if (_windowIconHandle == IntPtr.Zero)
        {
            return;
        }

        _ = SendMessage(hwnd, WmSetIcon, (nint)IconSmall, _windowIconHandle);
        _ = SendMessage(hwnd, WmSetIcon, (nint)IconBig, _windowIconHandle);
    }

    private void OnRootActualThemeChanged(FrameworkElement sender, object args)
    {
        ApplySystemTitleBarTheme();
    }

    private void ApplySystemTitleBarTheme()
    {
        if (_window is null)
        {
            return;
        }

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_window);
        var useDarkMode = _window.Content is FrameworkElement root && root.ActualTheme == ElementTheme.Dark ? 1 : 0;
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

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);

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
