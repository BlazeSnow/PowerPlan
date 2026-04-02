using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PowerPlan.Models;
using PowerPlan.Services;

namespace PowerPlan.Views;

public sealed partial class MainPage : Page
{
    private readonly PowerPlanService _powerPlanService = new();
    private bool _isUpdatingSelection;

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
        RefreshPlansButton.Content = LocalizationService.Get("Main.RefreshPlansButton");
        PlanPickerTitleText.Text = LocalizationService.Get("Main.PlanPickerTitle", "选择要使用的电源计划");
        UltimateMissingInfoBar.Title = LocalizationService.Get("Main.UltimateMissingTitle");
        UltimateMissingInfoBar.Message = LocalizationService.Get("Main.UltimateMissingMessage");
        CreateUltimateButton.Content = LocalizationService.Get("Main.CreateUltimateButton");
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

        _isUpdatingSelection = true;
        PlansListView.SelectedItem = Plans.FirstOrDefault(x => x.IsActive);
        _isUpdatingSelection = false;

        if (Application.Current is App app)
        {
            await app.RefreshTrayPlansAsync();
        }

        SetStatus(LocalizationService.Format("Main.Status.PlansLoaded", plans.Count), InfoBarSeverity.Success);
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

    private async void OnPlansSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingSelection)
        {
            return;
        }

        if (sender is not ListView listView || listView.SelectedItem is not PowerPlanItemViewModel selectedPlan)
        {
            return;
        }

        if (selectedPlan.IsActive)
        {
            return;
        }

        try
        {
            await _powerPlanService.SetActivePlanAsync(selectedPlan.Guid);
            SetStatus(LocalizationService.Format("Main.Status.SwitchSuccess", selectedPlan.Guid), InfoBarSeverity.Success);
            ApplyActivePlan(selectedPlan.Guid);
            if (Application.Current is App app)
            {
                await app.RefreshTrayPlansAsync();
            }
        }
        catch (Exception ex)
        {
            SetStatus(LocalizationService.Format("Main.Status.SwitchFailed", ex.Message), InfoBarSeverity.Error);
            var activePlan = Plans.FirstOrDefault(x => x.IsActive);
            if (activePlan is not null)
            {
                _isUpdatingSelection = true;
                PlansListView.SelectedItem = activePlan;
                _isUpdatingSelection = false;
            }
        }
    }

    private void ApplyActivePlan(string activePlanGuid)
    {
        foreach (var plan in Plans)
        {
            plan.IsActive = string.Equals(plan.Guid, activePlanGuid, StringComparison.OrdinalIgnoreCase);
        }

        var selected = Plans.FirstOrDefault(x => x.IsActive);
        if (selected is not null)
        {
            _isUpdatingSelection = true;
            PlansListView.SelectedItem = selected;
            _isUpdatingSelection = false;
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

    public void AddExternalStatus(string message, bool isError = false)
    {
        SetStatus(message, isError ? InfoBarSeverity.Error : InfoBarSeverity.Informational);
    }

    public async Task RefreshFromExternalAsync()
    {
        await RefreshPlansAsync();
    }
}

public sealed class PowerPlanItemViewModel : INotifyPropertyChanged
{
    private bool _isActive;

    public PowerPlanItemViewModel(PowerPlanInfo model)
    {
        Guid = model.Guid;
        Name = model.Name;
        _isActive = model.IsActive;
    }

    public string Guid { get; }
    public string Name { get; }
    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive == value)
            {
                return;
            }

            _isActive = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
