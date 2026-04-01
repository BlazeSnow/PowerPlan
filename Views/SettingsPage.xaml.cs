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
        PageTitleText.Text = "\u8BBE\u7F6E";
        PageDescText.Text = "\u6309 Windows 11 \u98CE\u683C\u96C6\u4E2D\u7BA1\u7406\u5E94\u7528\u884C\u4E3A";

        AutoStartTitleText.Text = "\u5F00\u673A\u81EA\u542F\u52A8";
        AutoStartDescText.Text = "\u9ED8\u8BA4\u5173\u95ED\uFF0C\u53EF\u5728\u767B\u5F55\u540E\u81EA\u52A8\u542F\u52A8 PowerPlan";

        TrayTitleText.Text = "\u542F\u7528\u6258\u76D8";
        TrayDescText.Text = "\u9ED8\u8BA4\u5F00\u542F\uFF0C\u5173\u95ED\u540E\u70B9\u51FB\u7A97\u53E3\u5173\u95ED\u5C06\u76F4\u63A5\u9000\u51FA";

        SettingsStatusBar.Title = "\u72B6\u6001";
        SettingsStatusBar.Message = "\u8BBE\u7F6E\u5DF2\u52A0\u8F7D";
    }

    private async void SettingsPage_Loaded(object sender, RoutedEventArgs e)
    {
        _updatingUi = true;
        var settings = _settingsService.Current;

        AutoStartToggle.IsOn = settings.AutoStart;
        TrayToggle.IsOn = settings.TrayEnabled;
        PathText.Text = $"settings.json: {_settingsService.GetSettingsPath()}";

        _updatingUi = false;
        try
        {
            await EnsureStartupStateAsync(settings.AutoStart);
        }
        catch (Exception ex)
        {
            SettingsStatusBar.Severity = InfoBarSeverity.Warning;
            SettingsStatusBar.Message = $"\u5E94\u7528\u5F00\u673A\u81EA\u542F\u52A8\u72B6\u6001\u5931\u8D25\uFF1A{ex.Message}";
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
            SettingsStatusBar.Title = "\u72B6\u6001";
            SettingsStatusBar.Message = "\u8BBE\u7F6E\u5DF2\u4FDD\u5B58";
        }
        catch (Exception ex)
        {
            SettingsStatusBar.Severity = InfoBarSeverity.Error;
            SettingsStatusBar.Title = "\u72B6\u6001";
            SettingsStatusBar.Message = $"\u8BBE\u7F6E\u4FDD\u5B58\u5931\u8D25\uFF1A{ex.Message}";
        }
    }

    private Task EnsureStartupStateAsync(bool enabled)
    {
        return Task.Run(() => _startupService.SetEnabled(enabled));
    }
}