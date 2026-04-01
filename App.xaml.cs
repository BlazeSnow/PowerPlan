using Microsoft.UI.Xaml.Navigation;
using PowerPlan.Services;
using PowerPlan.Views;
using System.Runtime.InteropServices;

namespace PowerPlan;

public partial class App : Application
{
    private Window? _window;
    private TrayService? _trayService;
    private readonly PowerPlanService _powerPlanService = new();
    private readonly StartupService _startupService = new();
    private bool _isExiting;

    public App()
    {
        InitializeComponent();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs e)
    {
        _window ??= new Window();

        if (_window.Content is not Frame rootFrame)
        {
            rootFrame = new Frame();
            rootFrame.NavigationFailed += OnNavigationFailed;
            _window.Content = rootFrame;
        }

        _ = rootFrame.Navigate(typeof(MainPage), e.Arguments);
        _window.Activate();
        _window.Closed -= OnMainWindowClosed;
        _window.Closed += OnMainWindowClosed;

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_window);

        try
        {
            _trayService ??= new TrayService(
                mainWindowHandle: hwnd,
                getPlansAsync: _powerPlanService.GetPlansAsync,
                setActivePlanAsync: async guid =>
                {
                    await _powerPlanService.SetActivePlanAsync(guid);
                    if (rootFrame.Content is MainPage page)
                    {
                        await page.RefreshFromExternalAsync();
                        page.AddExternalStatus($"[\u6258\u76D8] \u5DF2\u5207\u6362\u7535\u6E90\u8BA1\u5212\uFF1A{guid}");
                    }
                },
                isStartupEnabled: _startupService.IsEnabled,
                setStartupEnabled: enabled =>
                {
                    _startupService.SetEnabled(enabled);
                    if (rootFrame.Content is MainPage page)
                    {
                        page.AddExternalStatus($"[\u6258\u76D8] \u5F00\u673A\u81EA\u542F\u52A8\uFF1A{(enabled ? "\u5F00\u542F" : "\u5173\u95ED")}");
                    }
                },
                showMainWindow: ShowMainWindow,
                exitApplication: ExitApplication,
                log: message =>
                {
                    if (rootFrame.Content is MainPage page)
                    {
                        page.AddExternalStatus(message, message.Contains("\u5931\u8D25", StringComparison.Ordinal));
                    }
                });

            await _trayService.InitializeAsync();
        }
        catch (Exception ex)
        {
            if (rootFrame.Content is MainPage page)
            {
                page.AddExternalStatus($"[\u6258\u76D8] \u521D\u59CB\u5316\u5931\u8D25\uFF1A{ex.Message}", true);
            }
        }
    }

    private void ExitApplication()
    {
        _isExiting = true;
        _trayService?.Dispose();
        _trayService = null;
        Exit();
    }

    private void OnMainWindowClosed(object sender, WindowEventArgs args)
    {
        if (_isExiting || _window is null)
        {
            return;
        }

        args.Handled = true;
        HideMainWindow();
    }

    private void ShowMainWindow()
    {
        if (_window is null)
        {
            return;
        }

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_window);
        _ = ShowWindow(hwnd, 5); // SW_SHOW
        _window.Activate();
    }

    private void HideMainWindow()
    {
        if (_window is null)
        {
            return;
        }

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_window);
        _ = ShowWindow(hwnd, 0); // SW_HIDE
    }

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);

    private static void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
    {
        throw new InvalidOperationException("\u9875\u9762\u52A0\u8F7D\u5931\u8D25\uFF1A" + e.SourcePageType.FullName);
    }
}
