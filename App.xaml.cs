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
            GetMainPage()?.AddExternalStatus($"\u8BBE\u7F6E\u5F00\u673A\u81EA\u542F\u52A8\u5931\u8D25\uFF1A{ex.Message}", true);
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
                    page.AddExternalStatus($"[\u6258\u76D8] \u5DF2\u5207\u6362\u7535\u6E90\u8BA1\u5212\uFF1A{guid}");
                }
            },
            isStartupEnabled: () => SettingsService.Current.AutoStart,
            setStartupEnabled: enabled => _ = UpdateAutoStartFromTrayAsync(enabled),
            showMainWindow: ShowMainWindow,
            exitApplication: ExitApplication,
            log: message => GetMainPage()?.AddExternalStatus(message, message.Contains("\u5931\u8D25", StringComparison.Ordinal)));

        try
        {
            await _trayService.InitializeAsync();
        }
        catch (Exception ex)
        {
            GetMainPage()?.AddExternalStatus($"[\u6258\u76D8] \u521D\u59CB\u5316\u5931\u8D25\uFF1A{ex.Message}", true);
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
            GetMainPage()?.AddExternalStatus($"[\u6258\u76D8] \u5F00\u673A\u81EA\u542F\u52A8\uFF1A{(enabled ? "\u5F00\u542F" : "\u5173\u95ED")}");
        }
        catch (Exception ex)
        {
            GetMainPage()?.AddExternalStatus($"[\u6258\u76D8] \u81EA\u542F\u52A8\u8BBE\u7F6E\u5931\u8D25\uFF1A{ex.Message}", true);
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
