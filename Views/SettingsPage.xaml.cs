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

        AdminTitleText.Text = LocalizationService.Get("Settings.Admin.Title", "管理员权限");
        AdminDescText.Text = LocalizationService.Get("Settings.Admin.Desc", "创建卓越性能计划等操作需要管理员权限。");
        RequestAdminButton.Content = LocalizationService.Get("Settings.Admin.Button", "请求管理员权限");
        UpdateAdminStateText();

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
            await EnsureStartupStateAsync(settings.AutoStart);
            UpdateAdminStateText();
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
        var settings = new AppSettings
        {
            AutoStart = AutoStartToggle.IsOn,
            TrayEnabled = TrayToggle.IsOn
        };

        try
        {
            await _settingsService.SaveAsync(settings);
            await EnsureStartupStateAsync(settings.AutoStart);
        }
        catch
        {
            // Keep page silent when persistence/startup update fails.
        }
    }

    private async Task EnsureStartupStateAsync(bool enabled)
    {
        var effective = await _startupService.SetEnabledAsync(enabled);
        if (effective != enabled)
        {
            _updatingUi = true;
            AutoStartToggle.IsOn = effective;
            _updatingUi = false;
        }
    }

    private void OnOpenPowerOptionsClicked(object sender, RoutedEventArgs e)
    {
        OpenExternal("control.exe", "/name Microsoft.PowerOptions");
    }

    private void OnOpenWebsiteClicked(object sender, RoutedEventArgs e)
    {
        OpenExternal("https://www.blazesnow.com");
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

    private async void OnRequestAdminClicked(object sender, RoutedEventArgs e)
    {
        using var currentProcess = Process.GetCurrentProcess();
        var processName = currentProcess.ProcessName;
        var existingIds = Process.GetProcessesByName(processName).Select(p => p.Id).ToHashSet();

        var started = PrivilegeService.TryRestartAsAdministrator(out var error);
        if (started)
        {
            for (var i = 0; i < 25; i++)
            {
                await Task.Delay(200);
                var hasNewInstance = Process.GetProcessesByName(processName).Any(p => !existingIds.Contains(p.Id));
                if (hasNewInstance)
                {
                    ((App)Application.Current).ExitForRestart();
                    return;
                }
            }

            error = "未检测到新的管理员实例启动。";
        }

        var prefix = LocalizationService.Get("Settings.Admin.StartFailed", "管理员提权启动失败");
        var content = string.IsNullOrWhiteSpace(error)
            ? prefix
            : $"{prefix}：{error}";
        await ShowMessageAsync(prefix, content);
    }

    private void UpdateAdminStateText()
    {
        var isAdmin = PrivilegeService.IsRunningAsAdministrator();
        AdminStateText.Text = isAdmin
            ? LocalizationService.Get("Settings.Admin.State.Yes", "当前已管理员运行")
            : LocalizationService.Get("Settings.Admin.State.No", "当前未以管理员运行");
        RequestAdminButton.Visibility = isAdmin ? Visibility.Collapsed : Visibility.Visible;
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

    private async Task ShowMessageAsync(string title, string content)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = content,
            CloseButtonText = "确定",
            XamlRoot = this.XamlRoot
        };

        _ = await dialog.ShowAsync();
    }
}
