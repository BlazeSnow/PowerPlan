using System.Diagnostics;
using PowerPlan.Models;
using PowerPlan.Services;
using Windows.ApplicationModel.DataTransfer;

namespace PowerPlan.Views;

public sealed partial class SettingsPage : Page
{
    private const string FeedbackMail = "powerplan@blazesnow.com";

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

        AutoStartTitleText.Text = LocalizationService.Get("Settings.AutoStart.Title");
        AutoStartDescText.Text = LocalizationService.Get("Settings.AutoStart.Desc");

        TrayTitleText.Text = LocalizationService.Get("Settings.Tray.Title");
        TrayDescText.Text = LocalizationService.Get("Settings.Tray.Desc");

        PowerOptionsTitleText.Text = LocalizationService.Get("Settings.Tools.PowerOptions");
        PowerOptionsDescText.Text = LocalizationService.Get("Settings.Tools.PowerOptionsDesc");
        OpenPowerOptionsButton.Content = LocalizationService.Get("Settings.Tools.OpenButton");

        WebsiteTitleText.Text = LocalizationService.Get("Settings.Tools.Website");
        WebsiteDescText.Text = LocalizationService.Get("Settings.Tools.WebsiteDesc");
        OpenWebsiteButton.Content = LocalizationService.Get("Settings.Tools.OpenButton");

        FeedbackTitleText.Text = LocalizationService.Get("Settings.Tools.Feedback");
        FeedbackDescText.Text = LocalizationService.Get("Settings.Tools.FeedbackDesc");
        SendFeedbackButton.Content = LocalizationService.Get("Settings.Tools.FeedbackCopy");
    }

    private async void SettingsPage_Loaded(object sender, RoutedEventArgs e)
    {
        _updatingUi = true;
        var settings = _settingsService.Current;

        AutoStartToggle.IsOn = settings.AutoStart;
        TrayToggle.IsOn = settings.TrayEnabled;

        _updatingUi = false;
        try
        {
            var effective = await _startupService.GetEffectiveEnabledAsync();
            if (effective != settings.AutoStart)
            {
                _updatingUi = true;
                AutoStartToggle.IsOn = effective;
                _updatingUi = false;

                _settingsService.Current.AutoStart = effective;
                await _settingsService.SaveCurrentAsync();
            }
        }
        catch
        {
            // Keep page silent when startup registration is not accessible.
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
        try
        {
            var desiredAutoStart = AutoStartToggle.IsOn;
            var trayEnabled = TrayToggle.IsOn;
            var effectiveAutoStart = await EnsureStartupStateAsync(desiredAutoStart);

            var settings = new AppSettings
            {
                AutoStart = effectiveAutoStart,
                TrayEnabled = trayEnabled
            };

            await _settingsService.SaveAsync(settings);
        }
        catch
        {
            // Keep page silent when persistence/startup update fails.
        }
    }

    private async Task<bool> EnsureStartupStateAsync(bool enabled)
    {
        var effective = await _startupService.SetEnabledAsync(enabled);
        if (effective != enabled)
        {
            _updatingUi = true;
            AutoStartToggle.IsOn = effective;
            _updatingUi = false;
        }

        return effective;
    }

    private void OnOpenPowerOptionsClicked(object sender, RoutedEventArgs e)
    {
        OpenExternal("control.exe", "/name Microsoft.PowerOptions");
    }

    private void OnOpenWebsiteClicked(object sender, RoutedEventArgs e)
    {
        OpenExternal("https://github.com/BlazeSnow/PowerPlan");
    }

    private void OnSendFeedbackClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            var dataPackage = new DataPackage();
            dataPackage.SetText(FeedbackMail);
            Clipboard.SetContent(dataPackage);
        }
        catch
        {
            // Keep page silent when clipboard is unavailable.
        }
    }

    private static void OpenExternal(string target, string? args = null)
    {
        try
        {
            var startInfo = new ProcessStartInfo(target)
            {
                UseShellExecute = true
            };

            if (!string.IsNullOrWhiteSpace(args))
            {
                startInfo.Arguments = args;
            }

            _ = Process.Start(startInfo);
        }
        catch
        {
            // Keep page silent when external process launch fails.
        }
    }
}
