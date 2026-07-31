using PowerPlan.Models;
using PowerPlan.Services;
using System.Diagnostics;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Core;

namespace PowerPlan.Pages;

public sealed partial class SettingsPage : Page
{
    private const string OfficialWebsiteUrl = "https://www.blazesnow.com/powerplan/";
    private const string RepositoryUrl = "https://github.com/BlazeSnow/PowerPlan";

    private IPageHost? _pageHost;
    private bool _updatingUi;
    private ContentDialog? _operationDialog;

    public SettingsPage()
    {
        InitializeComponent();
        Loaded += SettingsPage_Loaded;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _pageHost = e.Parameter as IPageHost
            ?? throw new InvalidOperationException("SettingsPage requires an IPageHost navigation parameter.");
        ApplyLocalization();
        AppVersionTextBlock.Text = GetAppVersion();
    }

    private IPageHost PageHost => _pageHost
        ?? throw new InvalidOperationException("SettingsPage has not been initialized with an IPageHost.");

    private void ApplyLocalization()
    {
        PageTitleTextBlock.Text = PageHost.GetString("Settings.PageTitle");

        LanguageCard.Header = PageHost.GetString("Settings.Language.Title");
        LanguageCard.Description = PageHost.GetString("Settings.Language.Desc");
        AutomaticLanguageItem.Content = PageHost.GetString("Settings.Language.Automatic");

        AutoStartCard.Header = PageHost.GetString("Settings.AutoStart.Title");
        AutoStartCard.Description = PageHost.GetString("Settings.AutoStart.Desc");

        TrayCard.Header = PageHost.GetString("Settings.Tray.Title");
        TrayCard.Description = PageHost.GetString("Settings.Tray.Desc");

        LaunchToTrayCard.Header = PageHost.GetString("Settings.LaunchToTray.Title");
        LaunchToTrayCard.Description = PageHost.GetString("Settings.LaunchToTray.Desc");

        PowerOptionsCard.Header = PageHost.GetString("Settings.Tools.PowerOptions");
        PowerOptionsCard.Description = PageHost.GetString("Settings.Tools.PowerOptionsDesc");
        OpenPowerOptionsButton.Content = PageHost.GetString("Settings.Tools.OpenButton");

        RestorePowerPlansCard.Header = PageHost.GetString("Settings.Tools.RestorePowerPlans");
        RestorePowerPlansCard.Description = PageHost.GetString("Settings.Tools.RestorePowerPlansDesc");
        RestorePowerPlansButton.Content = PageHost.GetString("Settings.Tools.RestoreButton");

        WebsiteCard.Header = PageHost.GetString("Settings.Tools.Website");
        WebsiteCard.Description = PageHost.GetString("Settings.Tools.WebsiteDesc");
        OpenWebsiteButton.Content = PageHost.GetString("Settings.Tools.OpenButton");

        RepositoryCard.Header = PageHost.GetString("Settings.Tools.Repository");
        RepositoryCard.Description = PageHost.GetString("Settings.Tools.RepositoryDesc");
        OpenRepositoryButton.Content = PageHost.GetString("Settings.Tools.OpenButton");

        AppVersionCard.Header = PageHost.GetString("Settings.AppVersion.Title");
        AppVersionCard.Description = PageHost.GetString("Settings.AppVersion.Desc");
    }

    private static string GetAppVersion()
    {
        PackageVersion version = Package.Current.Id.Version;
        return $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
    }

    private async void SettingsPage_Loaded(object sender, RoutedEventArgs e)
    {
        _updatingUi = true;
        var settings = PageHost.SettingsService.Current;
        var startupSupported = PageHost.StartupTaskService.IsSupported;

        AutoStartToggle.IsEnabled = startupSupported;
        AutoStartToggle.IsOn = startupSupported && settings.AutoStart;
        TrayToggle.IsOn = settings.TrayEnabled;
        LaunchToTrayToggle.IsOn = settings.LaunchToTray;
        LaunchToTrayToggle.IsEnabled = settings.TrayEnabled;
        SelectLanguage(settings.Language);

        if (!startupSupported)
        {
            AutoStartCard.Description = PageHost.GetString("Settings.AutoStart.Unsupported");
        }

        _updatingUi = false;
        if (!startupSupported)
        {
            if (settings.AutoStart)
            {
                PageHost.SettingsService.Current.AutoStart = false;
                try
                {
                    await PageHost.SettingsService.SaveCurrentAsync();
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
            var effective = await PageHost.StartupTaskService.GetEffectiveEnabledAsync();
            if (effective != settings.AutoStart)
            {
                _updatingUi = true;
                AutoStartToggle.IsOn = effective;
                _updatingUi = false;

                PageHost.SettingsService.Current.AutoStart = effective;
                await PageHost.SettingsService.SaveCurrentAsync();
            }
        }
        catch
        {
            // Keep page silent when startup registration is not accessible.
        }
    }

    private async void OnLanguageSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingUi || LanguageComboBox.SelectedItem is not ComboBoxItem item)
        {
            return;
        }

        var previousLanguage = PageHost.SettingsService.Current.Language;
        var selectedLanguage = LanguageSettings.Normalize(item.Tag as string);
        if (selectedLanguage == previousLanguage)
        {
            return;
        }

        try
        {
            PageHost.SettingsService.Current.Language = selectedLanguage;
            await PageHost.SettingsService.SaveCurrentAsync();
            await ShowLanguageRestartDialogAsync(PageHost.SettingsService.ResolveLanguage(selectedLanguage));
        }
        catch (Exception ex)
        {
            PageHost.SettingsService.Current.Language = previousLanguage;
            SelectLanguage(previousLanguage);
            await ShowOperationDialogAsync(
                PageHost.GetString("Settings.PageTitle"),
                PageHost.FormatString("Settings.SaveFailed", ex.Message));
        }
    }

    private async Task ShowLanguageRestartDialogAsync(string language)
    {
        var dialog = new ContentDialog
        {
            Title = PageHost.GetStringForLanguage("Settings.Language.RestartTitle", language),
            Content = PageHost.GetStringForLanguage("Settings.Language.RestartMessage", language),
            PrimaryButtonText = PageHost.GetStringForLanguage("Settings.Language.RestartNow", language),
            CloseButtonText = PageHost.GetStringForLanguage("Settings.Language.RestartLater", language),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        try
        {
            var failureReason = Microsoft.Windows.AppLifecycle.AppInstance.Restart(string.Empty);
            await ShowRestartFailureAsync(failureReason, language);
        }
        catch
        {
            await ShowRestartFailureAsync(AppRestartFailureReason.Other, language);
        }
    }

    private Task ShowRestartFailureAsync(AppRestartFailureReason failureReason, string language)
    {
        var messageKey = failureReason switch
        {
            AppRestartFailureReason.RestartPending => "Settings.Language.RestartFailed.RestartPending",
            AppRestartFailureReason.InvalidUser => "Settings.Language.RestartFailed.InvalidUser",
            AppRestartFailureReason.NotInForeground => "Settings.Language.RestartFailed.NotInForeground",
            _ => "Settings.Language.RestartFailed.Other"
        };

        return ShowOperationDialogAsync(
            PageHost.GetStringForLanguage("Settings.Language.RestartFailed.Title", language),
            PageHost.GetStringForLanguage(messageKey, language),
            PageHost.GetStringForLanguage("Common.Ok", language));
    }

    private void SelectLanguage(string language)
    {
        var wasUpdatingUi = _updatingUi;
        _updatingUi = true;
        try
        {
            var normalized = LanguageSettings.Normalize(language);
            LanguageComboBox.SelectedIndex = normalized switch
            {
                LanguageSettings.ChineseLanguage => 1,
                LanguageSettings.TraditionalChineseLanguage => 2,
                LanguageSettings.EnglishLanguage => 3,
                LanguageSettings.FrenchLanguage => 4,
                LanguageSettings.ItalianLanguage => 5,
                LanguageSettings.GermanLanguage => 6,
                LanguageSettings.SpanishLanguage => 7,
                _ => 0
            };
        }
        finally
        {
            _updatingUi = wasUpdatingUi;
        }
    }

    private async void OnAutoStartToggled(object sender, RoutedEventArgs e)
    {
        if (!_updatingUi)
        {
            await SaveSettingsAsync();
        }
    }

    private async void OnTrayToggled(object sender, RoutedEventArgs e)
    {
        if (_updatingUi)
        {
            return;
        }

        UpdateLaunchToTrayEnabledState();
        await SaveSettingsAsync();
    }

    private async void OnLaunchToTrayToggled(object sender, RoutedEventArgs e)
    {
        if (!_updatingUi)
        {
            await SaveSettingsAsync();
        }
    }

    private async Task SaveSettingsAsync()
    {
        var previousAutoStart = PageHost.SettingsService.Current.AutoStart;
        var autoStartChanged = AutoStartToggle.IsOn != previousAutoStart;
        var startupStateChanged = false;

        try
        {
            var desiredAutoStart = AutoStartToggle.IsOn;
            var trayEnabled = TrayToggle.IsOn;
            var effectiveAutoStart = previousAutoStart;

            if (autoStartChanged)
            {
                try
                {
                    effectiveAutoStart = await EnsureStartupStateAsync(desiredAutoStart);
                    startupStateChanged = true;
                }
                catch (Exception ex)
                {
                    RestoreSettingsToggles();
                    await ShowOperationDialogAsync(
                        PageHost.GetString("Settings.PageTitle"),
                        PageHost.FormatString("App.Status.StartupSettingFailed", ex.Message));
                    return;
                }
            }

            var settings = new AppSettings
            {
                AutoStart = effectiveAutoStart,
                TrayEnabled = trayEnabled,
                LaunchToTray = LaunchToTrayToggle.IsOn,
                Language = PageHost.SettingsService.Current.Language,
                UltimatePerformancePlanGuid = PageHost.SettingsService.Current.UltimatePerformancePlanGuid
            };

            await PageHost.SettingsService.SaveAsync(settings);
        }
        catch (Exception ex)
        {
            if (startupStateChanged)
            {
                try
                {
                    _ = await PageHost.StartupTaskService.SetEnabledAsync(previousAutoStart);
                }
                catch
                {
                    // Prefer showing the original settings failure instead of masking it.
                }
            }

            RestoreSettingsToggles();
            await ShowOperationDialogAsync(
                PageHost.GetString("Settings.PageTitle"),
                PageHost.FormatString("Settings.SaveFailed", ex.Message));
        }
    }

    private void RestoreSettingsToggles()
    {
        _updatingUi = true;
        try
        {
            AutoStartToggle.IsOn = PageHost.StartupTaskService.IsSupported && PageHost.SettingsService.Current.AutoStart;
            TrayToggle.IsOn = PageHost.SettingsService.Current.TrayEnabled;
            LaunchToTrayToggle.IsOn = PageHost.SettingsService.Current.LaunchToTray;
            UpdateLaunchToTrayEnabledState();
        }
        finally
        {
            _updatingUi = false;
        }
    }

    private void UpdateLaunchToTrayEnabledState()
    {
        LaunchToTrayToggle.IsEnabled = TrayToggle.IsOn;
    }

    private async Task<bool> EnsureStartupStateAsync(bool enabled)
    {
        var effective = await PageHost.StartupTaskService.SetEnabledAsync(enabled);
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
        if (!await ShowRestoreConfirmationDialogAsync())
        {
            return;
        }

        try
        {
            await PageHost.PowerPlanService.RestoreDefaultSchemesAsync();
            PageHost.SettingsService.Current.UltimatePerformancePlanGuid = string.Empty;
            await PageHost.SettingsService.SaveCurrentAsync();
            await PageHost.RefreshTrayPlansAsync(forceRefresh: true);

            await ShowOperationDialogAsync(
                PageHost.GetString("Settings.RestoreDialog.SuccessTitle"),
                PageHost.GetString("Settings.RestoreDialog.SuccessMessage"));
        }
        catch (Exception ex)
        {
            await ShowOperationDialogAsync(
                PageHost.GetString("Settings.RestoreDialog.FailedTitle"),
                PageHost.FormatString("Settings.RestoreDialog.FailedMessage", ex.Message));
        }
    }

    private async Task<bool> ShowRestoreConfirmationDialogAsync()
    {
        var dialog = new ContentDialog
        {
            Title = PageHost.GetString("Settings.RestoreConfirmDialog.Title"),
            Content = PageHost.GetString("Settings.RestoreConfirmDialog.Message"),
            PrimaryButtonText = PageHost.GetString("Settings.RestoreConfirmDialog.Confirm"),
            CloseButtonText = PageHost.GetString("Settings.RestoreConfirmDialog.Cancel"),
            DefaultButton = ContentDialogButton.Close,
            PrimaryButtonStyle = CreateDangerButtonStyle(),
            XamlRoot = XamlRoot
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private void OnOpenWebsiteClicked(object sender, RoutedEventArgs e)
    {
        OpenExternal(OfficialWebsiteUrl);
    }

    private void OnOpenRepositoryClicked(object sender, RoutedEventArgs e)
    {
        OpenExternal(RepositoryUrl);
    }

    private Task ShowOperationDialogAsync(string title, string message)
    {
        return ShowOperationDialogAsync(title, message, PageHost.GetString("Common.Ok"));
    }

    private async Task ShowOperationDialogAsync(string title, string message, string closeButtonText)
    {
        if (_operationDialog is null)
        {
            _operationDialog = new ContentDialog { XamlRoot = XamlRoot };
        }

        _operationDialog.Title = title;
        _operationDialog.Content = message;
        _operationDialog.CloseButtonText = closeButtonText;
        await _operationDialog.ShowAsync();
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
            var startInfo = new ProcessStartInfo(target) { UseShellExecute = true };
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
