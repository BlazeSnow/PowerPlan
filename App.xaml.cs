using Microsoft.UI.Xaml.Navigation;
using PowerPlan.Services;
using PowerPlan.Views;

namespace PowerPlan;

public partial class App : Application
{
    private Window? _window;
    private TrayService? _trayService;
    private readonly PowerPlanService _powerPlanService = new();
    private readonly StartupService _startupService = new();

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

        _trayService ??= new TrayService(
            getPlansAsync: _powerPlanService.GetPlansAsync,
            setActivePlanAsync: async guid =>
            {
                await _powerPlanService.SetActivePlanAsync(guid);
                if (rootFrame.Content is MainPage page)
                {
                    await page.RefreshFromExternalAsync();
                    page.AddExternalLog($"[Tray] Switched power plan: {guid}");
                }
            },
            isStartupEnabled: _startupService.IsEnabled,
            setStartupEnabled: enabled =>
            {
                _startupService.SetEnabled(enabled);
                if (rootFrame.Content is MainPage page)
                {
                    page.AddExternalLog($"[Tray] Launch at startup: {(enabled ? "On" : "Off")}");
                }
            },
            showMainWindow: () => _window.Activate(),
            exitApplication: ExitApplication,
            log: message =>
            {
                if (rootFrame.Content is MainPage page)
                {
                    page.AddExternalLog(message);
                }
            });

        await _trayService.InitializeAsync();
    }

    private void ExitApplication()
    {
        _trayService?.Dispose();
        _trayService = null;
        Exit();
    }

    private static void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
    {
        throw new InvalidOperationException("Failed to load Page " + e.SourcePageType.FullName);
    }
}