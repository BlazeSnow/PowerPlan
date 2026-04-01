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

    public ObservableCollection<string> Logs { get; } = new();
    public ObservableCollection<PowerPlanItemViewModel> Plans { get; } = new();

    public MainPage()
    {
        InitializeComponent();

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
            AddLog("App initialization completed.");
        }
        catch (Exception ex)
        {
            AddLog($"Initialization failed: {ex.Message}");
        }
    }

    private async Task RefreshPlansAsync()
    {
        var plans = await _powerPlanService.GetPlansAsync();

        Plans.Clear();
        foreach (var plan in plans)
        {
            Plans.Add(new PowerPlanItemViewModel(plan));
        }

        var hasUltimate = plans.Any(static p => p.Guid.Equals(PowerPlanService.UltimatePerformanceGuid, StringComparison.OrdinalIgnoreCase));
        UltimateMissingInfoBar.IsOpen = !hasUltimate;
        CreateUltimateButton.Visibility = hasUltimate ? Visibility.Collapsed : Visibility.Visible;

        AddLog($"Power plans loaded: {plans.Count}.");
    }

    private void RefreshAdminStatus()
    {
        var isAdmin = PrivilegeService.IsRunningAsAdministrator();
        AdminStatusText.Text = isAdmin
            ? "Running with administrator permission. Plan switch is available."
            : "Running without administrator permission. Read-only mode.";
        ElevateButton.Visibility = isAdmin ? Visibility.Collapsed : Visibility.Visible;
    }

    private void AddLog(string message)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
        Logs.Insert(0, line);
        while (Logs.Count > 200)
        {
            Logs.RemoveAt(Logs.Count - 1);
        }
    }

    private async void OnRefreshClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            await RefreshPlansAsync();
        }
        catch (Exception ex)
        {
            AddLog($"Refresh failed: {ex.Message}");
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
            AddLog($"Switched power plan: {planGuid}");
            await RefreshPlansAsync();
        }
        catch (Exception ex)
        {
            AddLog($"Switch failed: {ex.Message}");
        }
    }

    private async void OnCreateUltimateClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            await _powerPlanService.CreateUltimatePerformancePlanAsync();
            AddLog("Ultimate Performance plan created.");
            await RefreshPlansAsync();
        }
        catch (Exception ex)
        {
            AddLog($"Create Ultimate Performance failed: {ex.Message}");
        }
    }

    private void OnElevateClicked(object sender, RoutedEventArgs e)
    {
        var started = PrivilegeService.TryRestartAsAdministrator();
        if (started)
        {
            AddLog("Requested restart with administrator permission.");
            Application.Current.Exit();
            return;
        }

        AddLog("Administrator permission request was cancelled or failed.");
    }

    private void OnStartupToggled(object sender, RoutedEventArgs e)
    {
        try
        {
            _startupService.SetEnabled(StartupToggle.IsOn);
            AddLog($"Launch at startup: {(StartupToggle.IsOn ? "On" : "Off")}");
        }
        catch (Exception ex)
        {
            AddLog($"Set launch-at-startup failed: {ex.Message}");
            StartupToggle.Toggled -= OnStartupToggled;
            StartupToggle.IsOn = _startupService.IsEnabled();
            StartupToggle.Toggled += OnStartupToggled;
        }
    }

    public void AddExternalLog(string message)
    {
        AddLog(message);
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
    public Visibility IsActiveVisibility => IsActive ? Visibility.Visible : Visibility.Collapsed;
}