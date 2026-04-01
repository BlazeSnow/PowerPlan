using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PowerPlan.Models;
using PowerPlan.Services;

namespace PowerPlan.Views;

public sealed partial class MainPage : Page
{
    private readonly PowerPlanService _powerPlanService = new();

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
            await RefreshPlansAsync();
            SetStatus(LocalizationService.Get("Main.Status.InitSuccess"), InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            SetStatus(LocalizationService.Format("Main.Status.InitFailed", ex.Message), InfoBarSeverity.Error);
        }
    }

    private void ApplyLocalization()
    {
        SubtitleText.Text = LocalizationService.Get("Main.Subtitle");

        UnlockTitleText.Text = LocalizationService.Get("Main.UnlockTitle");
        ElevateButton.Content = LocalizationService.Get("Main.RequestAdminButton");

        PlansTitleText.Text = LocalizationService.Get("Main.PlansTitle");
        RefreshPlansButton.Content = LocalizationService.Get("Main.RefreshPlansButton");
        UltimateMissingInfoBar.Title = LocalizationService.Get("Main.UltimateMissingTitle");
        UltimateMissingInfoBar.Message = LocalizationService.Get("Main.UltimateMissingMessage");
        CreateUltimateButton.Content = LocalizationService.Get("Main.CreateUltimateButton");

        StatusAreaTitleText.Text = LocalizationService.Get("Main.StatusAreaTitle");
        StatusInfoBar.Title = LocalizationService.Get("Main.StatusTitle");
        StatusInfoBar.Message = LocalizationService.Get("Main.StatusWaiting");
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

        SetStatus(LocalizationService.Format("Main.Status.PlansLoaded", plans.Count), InfoBarSeverity.Success);
    }

    private void RefreshAdminStatus()
    {
        var isAdmin = PrivilegeService.IsRunningAsAdministrator();
        AdminStatusText.Text = isAdmin
            ? LocalizationService.Get("Main.Admin.Yes")
            : LocalizationService.Get("Main.Admin.No");
        ElevateButton.Visibility = isAdmin ? Visibility.Collapsed : Visibility.Visible;
    }

    private void SetStatus(string message, InfoBarSeverity severity)
    {
        StatusInfoBar.IsOpen = true;
        StatusInfoBar.Severity = severity;
        StatusInfoBar.Title = LocalizationService.Format("Main.Status.TitleWithTime", DateTime.Now.ToString("HH:mm:ss"));
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
            SetStatus(LocalizationService.Format("Main.Status.RefreshFailed", ex.Message), InfoBarSeverity.Error);
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
            SetStatus(LocalizationService.Format("Main.Status.SwitchSuccess", planGuid), InfoBarSeverity.Success);
            await RefreshPlansAsync();
        }
        catch (Exception ex)
        {
            SetStatus(LocalizationService.Format("Main.Status.SwitchFailed", ex.Message), InfoBarSeverity.Error);
        }
    }

    private async void OnCreateUltimateClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            if (await _powerPlanService.HasUltimatePerformancePlanAsync())
            {
                SetStatus(LocalizationService.Get("Main.Status.UltimateExists"), InfoBarSeverity.Informational);
                await RefreshPlansAsync();
                return;
            }

            await _powerPlanService.CreateUltimatePerformancePlanAsync();
            SetStatus(LocalizationService.Get("Main.Status.UltimateCreated"), InfoBarSeverity.Success);
            await RefreshPlansAsync();
        }
        catch (Exception ex)
        {
            SetStatus(LocalizationService.Format("Main.Status.UltimateCreateFailed", ex.Message), InfoBarSeverity.Error);
        }
    }

    private void OnElevateClicked(object sender, RoutedEventArgs e)
    {
        var started = PrivilegeService.TryRestartAsAdministrator();
        if (started)
        {
            SetStatus(LocalizationService.Get("Main.Status.ElevateRequested"), InfoBarSeverity.Informational);
            Application.Current.Exit();
            return;
        }

        SetStatus(LocalizationService.Get("Main.Status.ElevateFailed"), InfoBarSeverity.Warning);
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
    public string CurrentTagText => LocalizationService.Get("Main.CurrentTag");
    public string SwitchButtonText => LocalizationService.Get("Main.SwitchButton");
    public Visibility IsActiveVisibility => IsActive ? Visibility.Visible : Visibility.Collapsed;
}