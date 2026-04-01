using PowerPlan.Models;
using PowerPlan.Services;

namespace PowerPlan.Views;

public sealed partial class SettingsPage : Page
{
    private readonly SettingsService _settingsService;
    private readonly StartupService _startupService = new();
    private bool _updatingUi;

    public SettingsPage()
    {
        InitializeComponent();
        _settingsService = ((App)Application.Current).SettingsService;
        ApplyLocalization();

        Loaded += SettingsPage_Loaded;
    }

    private void ApplyLocalization()
    {
        PageTitleText.Text = LocalizationService.Get("Settings.PageTitle");
        PageDescText.Text = LocalizationService.Get("Settings.PageDesc");

        AutoStartTitleText.Text = LocalizationService.Get("Settings.AutoStart.Title");
        AutoStartDescText.Text = LocalizationService.Get("Settings.AutoStart.Desc");

        TrayTitleText.Text = LocalizationService.Get("Settings.Tray.Title");
        TrayDescText.Text = LocalizationService.Get("Settings.Tray.Desc");

        SettingsStatusBar.Title = LocalizationService.Get("Settings.Status.Title");
        SettingsStatusBar.Message = LocalizationService.Get("Settings.Status.Loaded");
    }

    private async void SettingsPage_Loaded(object sender, RoutedEventArgs e)
    {
        _updatingUi = true;
        var settings = _settingsService.Current;

        AutoStartToggle.IsOn = settings.AutoStart;
        TrayToggle.IsOn = settings.TrayEnabled;
        PathText.Text = LocalizationService.Format("Settings.PathLabel", _settingsService.GetSettingsPath());

        _updatingUi = false;
        try
        {
            await EnsureStartupStateAsync(settings.AutoStart);
        }
        catch (Exception ex)
        {
            SettingsStatusBar.Severity = InfoBarSeverity.Warning;
            SettingsStatusBar.Message = LocalizationService.Format("Settings.Status.StartupApplyFailed", ex.Message);
        }
    }

    private async void OnAutoStartToggled(object sender, RoutedEventArgs e)
    {
        if (_updatingUi)
        {
            return;
        }

        await SaveSettingsAsync();
    }

    private async void OnTrayToggled(object sender, RoutedEventArgs e)
    {
        if (_updatingUi)
        {
            return;
        }

        await SaveSettingsAsync();
    }

    private async Task SaveSettingsAsync()
    {
        var settings = new AppSettings
        {
            AutoStart = AutoStartToggle.IsOn,
            TrayEnabled = TrayToggle.IsOn
        };

        try
        {
            await _settingsService.SaveAsync(settings);
            await EnsureStartupStateAsync(settings.AutoStart);
            SettingsStatusBar.Severity = InfoBarSeverity.Success;
            SettingsStatusBar.Title = LocalizationService.Get("Settings.Status.Title");
            SettingsStatusBar.Message = LocalizationService.Get("Settings.Status.SaveSuccess");
        }
        catch (Exception ex)
        {
            SettingsStatusBar.Severity = InfoBarSeverity.Error;
            SettingsStatusBar.Title = LocalizationService.Get("Settings.Status.Title");
            SettingsStatusBar.Message = LocalizationService.Format("Settings.Status.SaveFailed", ex.Message);
        }
    }

    private Task EnsureStartupStateAsync(bool enabled)
    {
        return Task.Run(() => _startupService.SetEnabled(enabled));
    }
}