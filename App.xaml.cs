using PowerPlan.Models;
using PowerPlan.Services;
using PowerPlan.Views;
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
        var startInTray = IsTrayStartupLaunch(e?.Arguments);

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
        _window.Closed -= OnMainWindowClosed;
        _window.Closed += OnMainWindowClosed;

        await ApplyStartupSettingAsync();
        await EnsureTrayStateAsync();

        if (startInTray && SettingsService.Current.TrayEnabled && _trayService is not null)
        {
            HideMainWindow();
        }
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
            var effective = await _startupService.SetEnabledAsync(expected, SettingsService.Current.TrayEnabled);
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

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_window);

        _trayService = new TrayService(
            mainWindowHandle: hwnd,
            getPlansAsync: _powerPlanService.GetPlansAsync,
            setActivePlanAsync: async guid =>
            {
                await _powerPlanService.SetActivePlanAsync(guid);
                var page = GetMainPage();
                if (page is not null)
                {
                    await page.RefreshFromExternalAsync();
                    page.AddExternalStatus(LocalizationService.Format("App.Status.TraySwitched", guid));
                }
            },
            isStartupEnabled: () => SettingsService.Current.AutoStart,
            setStartupEnabled: enabled => _ = UpdateAutoStartFromTrayAsync(enabled),
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

    private async Task UpdateAutoStartFromTrayAsync(bool enabled)
    {
        try
        {
            var effective = await _startupService.SetEnabledAsync(enabled, SettingsService.Current.TrayEnabled);
            SettingsService.Current.AutoStart = effective;
            await SettingsService.SaveCurrentAsync();
            var state = LocalizationService.Get(effective ? "App.Status.On" : "App.Status.Off");
            GetMainPage()?.AddExternalStatus(LocalizationService.Format("App.Status.TrayAutoStart", state));
        }
        catch (Exception ex)
        {
            GetMainPage()?.AddExternalStatus(LocalizationService.Format("App.Status.TrayAutoStartFailed", ex.Message), true);
        }
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

    private static bool IsTrayStartupLaunch(string? launchArguments)
    {
        if (!string.IsNullOrWhiteSpace(launchArguments) &&
            launchArguments.Contains(StartupService.TrayStartupArgument, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        foreach (var arg in Environment.GetCommandLineArgs())
        {
            if (arg.Equals(StartupService.TrayStartupArgument, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private const uint WmSetIcon = 0x0080;
    private const int IconSmall = 0;
    private const int IconBig = 1;
    private const uint ImageIcon = 1;
    private const uint LrLoadFromFile = 0x00000010;

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint LoadImage(nint hinst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SendMessage(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint hIcon);
}
