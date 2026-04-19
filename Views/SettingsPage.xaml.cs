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
    private readonly PowerPlanService _powerPlanService = new();
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

        AutoStartCard.Header = LocalizationService.Get("Settings.AutoStart.Title");
        AutoStartCard.Description = LocalizationService.Get("Settings.AutoStart.Desc");

        TrayCard.Header = LocalizationService.Get("Settings.Tray.Title");
        TrayCard.Description = LocalizationService.Get("Settings.Tray.Desc");

        PowerOptionsCard.Header = LocalizationService.Get("Settings.Tools.PowerOptions");
        PowerOptionsCard.Description = LocalizationService.Get("Settings.Tools.PowerOptionsDesc");
        OpenPowerOptionsButton.Content = LocalizationService.Get("Settings.Tools.OpenButton");

        RestorePowerPlansCard.Header = LocalizationService.Get("Settings.Tools.RestorePowerPlans");
        RestorePowerPlansCard.Description = LocalizationService.Get("Settings.Tools.RestorePowerPlansDesc");
        RestorePowerPlansButton.Content = LocalizationService.Get("Settings.Tools.RestoreButton");

        WebsiteCard.Header = LocalizationService.Get("Settings.Tools.Website");
        WebsiteCard.Description = LocalizationService.Get("Settings.Tools.WebsiteDesc");
        OpenWebsiteButton.Content = LocalizationService.Get("Settings.Tools.OpenButton");

        FeedbackCard.Header = LocalizationService.Get("Settings.Tools.Feedback");
        FeedbackCard.Description = LocalizationService.Get("Settings.Tools.FeedbackDesc");
        SendFeedbackButton.Content = LocalizationService.Get("Settings.Tools.FeedbackCopy");
    }

    private async void SettingsPage_Loaded(object sender, RoutedEventArgs e)
    {
        _updatingUi = true;
        var settings = _settingsService.Current;
        var startupSupported = _startupService.IsSupported;

        AutoStartToggle.IsEnabled = startupSupported;
        AutoStartToggle.IsOn = startupSupported && settings.AutoStart;
        TrayToggle.IsOn = settings.TrayEnabled;

        if (!startupSupported)
        {
            AutoStartCard.Description = LocalizationService.Get("Settings.AutoStart.Unsupported");
        }

        _updatingUi = false;
        if (!startupSupported)
        {
            if (settings.AutoStart)
            {
                _settingsService.Current.AutoStart = false;
                try
                {
                    await _settingsService.SaveCurrentAsync();
                }
                catch
                {
                    // Keep page silent when persistence is unavailable.
                }
            }

            return;
        }

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
                TrayEnabled = trayEnabled,
                UltimatePerformancePlanGuid = _settingsService.Current.UltimatePerformancePlanGuid
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

    private async void OnRestorePowerPlansClicked(object sender, RoutedEventArgs e)
    {
        var confirmed = await ShowRestoreConfirmationDialogAsync();
        if (!confirmed)
        {
            return;
        }

        try
        {
            await _powerPlanService.RestoreDefaultSchemesAsync();
            _settingsService.Current.UltimatePerformancePlanGuid = string.Empty;
            await _settingsService.SaveCurrentAsync();

            if (Application.Current is App app)
            {
                await app.RefreshTrayPlansAsync();
            }

            await ShowOperationDialogAsync(
                LocalizationService.Get("Settings.RestoreDialog.SuccessTitle"),
                LocalizationService.Get("Settings.RestoreDialog.SuccessMessage"));
        }
        catch (Exception ex)
        {
            await ShowOperationDialogAsync(
                LocalizationService.Get("Settings.RestoreDialog.FailedTitle"),
                LocalizationService.Format("Settings.RestoreDialog.FailedMessage", ex.Message));
        }
    }

    private async Task<bool> ShowRestoreConfirmationDialogAsync()
    {
        var dialog = new ContentDialog
        {
            Title = LocalizationService.Get("Settings.RestoreConfirmDialog.Title"),
            Content = LocalizationService.Get("Settings.RestoreConfirmDialog.Message"),
            PrimaryButtonText = LocalizationService.Get("Settings.RestoreConfirmDialog.Confirm"),
            CloseButtonText = LocalizationService.Get("Settings.RestoreConfirmDialog.Cancel"),
            DefaultButton = ContentDialogButton.Close,
            PrimaryButtonStyle = CreateDangerButtonStyle(),
            XamlRoot = XamlRoot
        };

        var result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary;
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

    private async Task ShowOperationDialogAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            CloseButtonText = LocalizationService.Get("Main.CopyDialogCancel"),
            Content = message,
            XamlRoot = XamlRoot
        };

        await dialog.ShowAsync();
    }

    private static Style CreateDangerButtonStyle()
    {
        var style = new Style(typeof(Button));
        if (Application.Current.Resources.TryGetValue("DefaultButtonStyle", out var baseStyle)
            && baseStyle is Style defaultButtonStyle)
        {
            style.BasedOn = defaultButtonStyle;
        }

        style.Setters.Add(new Setter(Control.BackgroundProperty, Application.Current.Resources["SystemFillColorCriticalBrush"]));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, Application.Current.Resources["SystemFillColorCriticalBrush"]));
        style.Setters.Add(new Setter(Control.ForegroundProperty, Application.Current.Resources["TextOnAccentFillColorPrimaryBrush"]));
        return style;
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
