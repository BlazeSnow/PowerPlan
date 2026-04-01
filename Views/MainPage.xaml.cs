using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PowerPlan.Models;
using PowerPlan.Services;

namespace PowerPlan.Views;

public sealed partial class MainPage : Page
{
    private readonly PowerPlanService _powerPlanService = new();
    private readonly StartupService _startupService = new();

    public ObservableCollection<PowerPlanItemViewModel> Plans { get; } = new();

    public MainPage()
    {
        InitializeComponent();
        ApplyLocalization();

        PlansListView.ItemsSource = Plans;
        Loaded += MainPage_Loaded;
    }

    private async void MainPage_Loaded(object sender, RoutedEventArgs e)
    {
        RefreshAdminStatus();

        try
        {
            StartupToggle.IsOn = _startupService.IsEnabled();
            await RefreshPlansAsync();
            SetStatus("\u521D\u59CB\u5316\u5B8C\u6210", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            SetStatus($"\u521D\u59CB\u5316\u5931\u8D25\uFF1A{ex.Message}", InfoBarSeverity.Error);
        }
    }

    private void ApplyLocalization()
    {
        SubtitleText.Text = "\u5FEB\u901F\u5207\u6362 Windows \u7535\u6E90\u8BA1\u5212";

        UnlockTitleText.Text = "\u89E3\u9501\u533A";
        ElevateButton.Content = "\u8BF7\u6C42\u7BA1\u7406\u5458\u6743\u9650";

        PlansTitleText.Text = "\u8868\u683C\u533A";
        RefreshPlansButton.Content = "\u5237\u65B0\u8BA1\u5212";
        UltimateMissingInfoBar.Title = "\u672A\u53D1\u73B0\u5353\u8D8A\u6027\u80FD\u8BA1\u5212";
        UltimateMissingInfoBar.Message = "\u53EF\u70B9\u51FB\u4E0B\u65B9\u6309\u94AE\u521B\u5EFA\u5353\u8D8A\u6027\u80FD\u8BA1\u5212\u3002";
        CreateUltimateButton.Content = "\u521B\u5EFA\u5353\u8D8A\u6027\u80FD\u8BA1\u5212";

        StatusAreaTitleText.Text = "\u72B6\u6001\u533A";
        StatusInfoBar.Title = "\u72B6\u6001";
        StatusInfoBar.Message = "\u7B49\u5F85\u64CD\u4F5C";

        SettingsTitleText.Text = "\u8BBE\u7F6E\u533A";
        StartupToggle.Header = "\u5F00\u673A\u81EA\u542F\u52A8";
        StartupToggle.OnContent = "\u5F00\u542F";
        StartupToggle.OffContent = "\u5173\u95ED";
    }

    private async Task RefreshPlansAsync()
    {
        var plans = await _powerPlanService.GetPlansAsync();

        Plans.Clear();
        foreach (var plan in plans)
        {
            Plans.Add(new PowerPlanItemViewModel(plan));
        }

        var hasUltimate = plans.Any(_powerPlanService.IsUltimatePerformancePlan);
        UltimateMissingInfoBar.IsOpen = !hasUltimate;
        CreateUltimateButton.Visibility = hasUltimate ? Visibility.Collapsed : Visibility.Visible;

        SetStatus($"\u5DF2\u8BFB\u53D6\u7535\u6E90\u8BA1\u5212\uFF1A{plans.Count} \u4E2A", InfoBarSeverity.Success);
    }

    private void RefreshAdminStatus()
    {
        var isAdmin = PrivilegeService.IsRunningAsAdministrator();
        AdminStatusText.Text = isAdmin
            ? "\u5F53\u524D\u5DF2\u5177\u5907\u7BA1\u7406\u5458\u6743\u9650\uFF0C\u53EF\u4FEE\u6539\u7535\u6E90\u8BA1\u5212\u3002"
            : "\u5F53\u524D\u975E\u7BA1\u7406\u5458\u6743\u9650\uFF0C\u4EC5\u53EF\u8BFB\u53D6\u7535\u6E90\u8BA1\u5212\u3002";
        ElevateButton.Visibility = isAdmin ? Visibility.Collapsed : Visibility.Visible;
    }

    private void SetStatus(string message, InfoBarSeverity severity)
    {
        StatusInfoBar.IsOpen = true;
        StatusInfoBar.Severity = severity;
        StatusInfoBar.Title = $"\u72B6\u6001\uFF08{DateTime.Now:HH:mm:ss}\uFF09";
        StatusInfoBar.Message = message;
    }

    private async void OnRefreshClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            await RefreshPlansAsync();
        }
        catch (Exception ex)
        {
            SetStatus($"\u5237\u65B0\u5931\u8D25\uFF1A{ex.Message}", InfoBarSeverity.Error);
        }
    }

    private async void OnSwitchPlanClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string planGuid)
        {
            return;
        }

        try
        {
            await _powerPlanService.SetActivePlanAsync(planGuid);
            SetStatus($"\u5207\u6362\u7535\u6E90\u8BA1\u5212\u6210\u529F\uFF1A{planGuid}", InfoBarSeverity.Success);
            await RefreshPlansAsync();
        }
        catch (Exception ex)
        {
            SetStatus($"\u5207\u6362\u5931\u8D25\uFF1A{ex.Message}", InfoBarSeverity.Error);
        }
    }

    private async void OnCreateUltimateClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            if (await _powerPlanService.HasUltimatePerformancePlanAsync())
            {
                SetStatus("\u7CFB\u7EDF\u4E2D\u5DF2\u5B58\u5728\u5353\u8D8A\u6027\u80FD\u8BA1\u5212", InfoBarSeverity.Informational);
                await RefreshPlansAsync();
                return;
            }

            await _powerPlanService.CreateUltimatePerformancePlanAsync();
            SetStatus("\u5DF2\u521B\u5EFA\u5353\u8D8A\u6027\u80FD\u8BA1\u5212", InfoBarSeverity.Success);
            await RefreshPlansAsync();
        }
        catch (Exception ex)
        {
            SetStatus($"\u521B\u5EFA\u5353\u8D8A\u6027\u80FD\u8BA1\u5212\u5931\u8D25\uFF1A{ex.Message}", InfoBarSeverity.Error);
        }
    }

    private void OnElevateClicked(object sender, RoutedEventArgs e)
    {
        var started = PrivilegeService.TryRestartAsAdministrator();
        if (started)
        {
            SetStatus("\u5DF2\u53D1\u8D77\u7BA1\u7406\u5458\u6743\u9650\u91CD\u542F\u8BF7\u6C42", InfoBarSeverity.Informational);
            Application.Current.Exit();
            return;
        }

        SetStatus("\u7BA1\u7406\u5458\u6743\u9650\u8BF7\u6C42\u88AB\u53D6\u6D88\u6216\u5931\u8D25", InfoBarSeverity.Warning);
    }

    private void OnStartupToggled(object sender, RoutedEventArgs e)
    {
        try
        {
            _startupService.SetEnabled(StartupToggle.IsOn);
            SetStatus($"\u5F00\u673A\u81EA\u542F\u52A8\uFF1A{(StartupToggle.IsOn ? "\u5F00\u542F" : "\u5173\u95ED")}", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            SetStatus($"\u8BBE\u7F6E\u5F00\u673A\u81EA\u542F\u52A8\u5931\u8D25\uFF1A{ex.Message}", InfoBarSeverity.Error);
            StartupToggle.Toggled -= OnStartupToggled;
            StartupToggle.IsOn = _startupService.IsEnabled();
            StartupToggle.Toggled += OnStartupToggled;
        }
    }

    public void AddExternalStatus(string message, bool isError = false)
    {
        SetStatus(message, isError ? InfoBarSeverity.Error : InfoBarSeverity.Informational);
    }

    public async Task RefreshFromExternalAsync()
    {
        await RefreshPlansAsync();
    }
}

public sealed class PowerPlanItemViewModel
{
    public PowerPlanItemViewModel(PowerPlanInfo model)
    {
        Guid = model.Guid;
        Name = model.Name;
        IsActive = model.IsActive;
    }

    public string Guid { get; }
    public string Name { get; }
    public bool IsActive { get; }
    public bool CanSwitch => !IsActive;
    public string CurrentTagText => "\uFF08\u5F53\u524D\uFF09";
    public string SwitchButtonText => "\u5207\u6362";
    public Visibility IsActiveVisibility => IsActive ? Visibility.Visible : Visibility.Collapsed;
}