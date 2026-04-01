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

    public App()
    {
        InitializeComponent();

        SettingsService = new SettingsService();
        SettingsService.SettingsChanged += OnSettingsChanged;
    }

    public SettingsService SettingsService { get; }

    protected override async void OnLaunched(LaunchActivatedEventArgs e)
    {
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

        _window.Activate();
        _window.Closed -= OnMainWindowClosed;
        _window.Closed += OnMainWindowClosed;

        await ApplyStartupSettingAsync();
        await EnsureTrayStateAsync();
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
            await Task.Run(() => _startupService.SetEnabled(SettingsService.Current.AutoStart));
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
            _startupService.SetEnabled(enabled);
            SettingsService.Current.AutoStart = enabled;
            await SettingsService.SaveCurrentAsync();
            var state = LocalizationService.Get(enabled ? "App.Status.On" : "App.Status.Off");
            GetMainPage()?.AddExternalStatus(LocalizationService.Format("App.Status.TrayAutoStart", state));
        }
        catch (Exception ex)
        {
            GetMainPage()?.AddExternalStatus(LocalizationService.Format("App.Status.TrayAutoStartFailed", ex.Message), true);
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
        }
    }

    private void ExitApplication()
    {
        _isExiting = true;
        _trayService?.Dispose();
        _trayService = null;
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

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);
}
