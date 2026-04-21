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
    private static readonly TimeSpan DuplicateStatusSuppressionWindow = TimeSpan.FromMilliseconds(400);
    private readonly PowerPlanService _powerPlanService;
    private readonly SettingsService _settingsService;
    private readonly SemaphoreSlim _refreshSemaphore = new(1, 1);
    private bool _isUpdatingSelection;
    private DateTimeOffset _lastStatusAt;
    private string _lastStatusMessage = string.Empty;
    private InfoBarSeverity _lastStatusSeverity;

    public ObservableCollection<PowerPlanItemViewModel> Plans { get; } = new();

    public MainPage()
    {
        InitializeComponent();
        var app = (App)Application.Current;
        _powerPlanService = app.PowerPlanService;
        _settingsService = app.SettingsService;
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
        UltimateCard.Header = LocalizationService.Get("Main.UltimateMissingTitle");
        UltimateCard.Description = LocalizationService.Get("Main.UltimateMissingMessage");
        RefreshPlansButton.Content = LocalizationService.Get("Main.RefreshPlansButton");
        PlanPickerTitleText.Text = LocalizationService.Get("Main.PlanPickerTitle");
        CreateUltimateButton.Content = LocalizationService.Get("Main.CreateUltimateButton");
        StatusInfoBar.Title = LocalizationService.Get("Main.StatusTitle");
        StatusInfoBar.Message = LocalizationService.Get("Main.StatusWaiting");
        DeletePlanHintText.Text = LocalizationService.Get("Main.DeletePlanHint");
    }

    private Task RefreshPlansAsync(bool updateStatus = true, bool forceRefresh = false)
    {
        return RefreshPlansCoreAsync(updateStatus, forceRefresh);
    }

    private async Task RefreshPlansCoreAsync(bool updateStatus, bool forceRefresh)
    {
        await _refreshSemaphore.WaitAsync();
        try
        {
            var plans = await _powerPlanService.GetPlansAsync(forceRefresh);

            SynchronizePlans(plans);

            var hasUltimate = plans.Any(_powerPlanService.IsUltimatePerformancePlan);
            var savedUltimatePlanGuid = _settingsService.Current.UltimatePerformancePlanGuid;
            var hasHiddenUltimate = !string.IsNullOrWhiteSpace(savedUltimatePlanGuid)
                && !plans.Any(plan => string.Equals(plan.Guid, savedUltimatePlanGuid, StringComparison.OrdinalIgnoreCase));

            UltimateCard.Visibility = hasUltimate ? Visibility.Collapsed : Visibility.Visible;
            CreateUltimateButton.Visibility = hasUltimate ? Visibility.Collapsed : Visibility.Visible;

            if (!hasUltimate)
            {
                UltimateCard.Header = LocalizationService.Get(hasHiddenUltimate ? "Main.UltimateHiddenTitle" : "Main.UltimateMissingTitle");
                UltimateCard.Description = LocalizationService.Get(hasHiddenUltimate ? "Main.UltimateHiddenMessage" : "Main.UltimateMissingMessage");
                CreateUltimateButton.Content = LocalizationService.Get(hasHiddenUltimate ? "Main.ActivateUltimateButton" : "Main.CreateUltimateButton");
            }

            _isUpdatingSelection = true;
            PlansListView.SelectedItem = Plans.FirstOrDefault(x => x.IsActive);
            _isUpdatingSelection = false;

            if (Application.Current is App app)
            {
                app.UpdateTrayPlans(plans);
            }

            if (updateStatus)
            {
                SetStatus(LocalizationService.Format("Main.Status.PlansLoaded", plans.Count), InfoBarSeverity.Success);
            }
        }
        finally
        {
            _refreshSemaphore.Release();
        }
    }

    private void SetStatus(string message, InfoBarSeverity severity)
    {
        var now = DateTimeOffset.UtcNow;
        if (severity == _lastStatusSeverity
            && string.Equals(message, _lastStatusMessage, StringComparison.Ordinal)
            && now - _lastStatusAt <= DuplicateStatusSuppressionWindow)
        {
            return;
        }

        _lastStatusAt = now;
        _lastStatusMessage = message;
        _lastStatusSeverity = severity;
        StatusInfoBar.IsOpen = true;
        StatusInfoBar.Severity = severity;
        StatusInfoBar.Title = LocalizationService.Format("Main.Status.TitleWithTime", DateTime.Now.ToString("HH:mm:ss"));
        StatusInfoBar.Message = message;
    }

    private async void OnRefreshClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            await RefreshPlansAsync(forceRefresh: true);
        }
        catch (Exception ex)
        {
            SetStatus(LocalizationService.Format("Main.Status.RefreshFailed", ex.Message), InfoBarSeverity.Error);
        }
    }

    private async void OnCopyPlanClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string planGuid })
        {
            return;
        }

        try
        {
            var targetPlan = Plans.FirstOrDefault(x => string.Equals(x.Guid, planGuid, StringComparison.OrdinalIgnoreCase));
            var inputBox = new TextBox
            {
                Text = BuildCopyPlanName(targetPlan?.Name),
                PlaceholderText = LocalizationService.Get("Main.CopyDialogPlaceholder")
            };

            var dialog = new ContentDialog
            {
                Title = LocalizationService.Get("Main.CopyDialogTitle"),
                PrimaryButtonText = LocalizationService.Get("Main.CopyDialogConfirm"),
                CloseButtonText = LocalizationService.Get("Main.CopyDialogCancel"),
                DefaultButton = ContentDialogButton.Primary,
                Content = inputBox,
                XamlRoot = XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
            {
                return;
            }

            var newName = inputBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(newName))
            {
                SetStatus(LocalizationService.Get("Main.Status.CopyNameEmpty"), InfoBarSeverity.Error);
                return;
            }

            await _powerPlanService.CopyPlanAsync(planGuid, newName);
            await RefreshPlansAsync(false);
            SetStatus(LocalizationService.Format("Main.Status.CopySuccess", newName), InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            SetStatus(LocalizationService.Format("Main.Status.CopyFailed", ex.Message), InfoBarSeverity.Error);
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
                app.UpdateTrayPlans(BuildPlanSnapshot());
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
        var isActivatingSavedUltimate = false;

        try
        {
            var savedUltimatePlanGuid = _settingsService.Current.UltimatePerformancePlanGuid;
            if (!string.IsNullOrWhiteSpace(savedUltimatePlanGuid))
            {
                isActivatingSavedUltimate = true;
                await _powerPlanService.SetActivePlanAsync(savedUltimatePlanGuid);
                await RefreshPlansAsync(false);
                SetStatus(LocalizationService.Get("Main.Status.UltimateActivated"), InfoBarSeverity.Success);
                return;
            }

            var createdGuid = await _powerPlanService.CreateUltimatePerformancePlanAsync();
            _settingsService.Current.UltimatePerformancePlanGuid = createdGuid;
            await _settingsService.SaveCurrentAsync();

            await RefreshPlansAsync(false);
            SetStatus(LocalizationService.Get("Main.Status.UltimateCreated"), InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            if (isActivatingSavedUltimate)
            {
                _settingsService.Current.UltimatePerformancePlanGuid = string.Empty;
                try
                {
                    await _settingsService.SaveCurrentAsync();
                }
                catch
                {
                    // Keep failure handling focused on the power plan operation.
                }

                await RefreshPlansAsync(false);
                SetStatus(LocalizationService.Format("Main.Status.UltimateActivateFailed", ex.Message), InfoBarSeverity.Error);
                return;
            }

            SetStatus(LocalizationService.Format("Main.Status.UltimateCreateFailed", ex.Message), InfoBarSeverity.Error);
        }
    }

    private void SynchronizePlans(IReadOnlyList<PowerPlanInfo> plans)
    {
        var existingPlans = Plans.ToDictionary(plan => plan.Guid, StringComparer.OrdinalIgnoreCase);
        var incomingGuids = new HashSet<string>(plans.Select(plan => plan.Guid), StringComparer.OrdinalIgnoreCase);
        for (var i = Plans.Count - 1; i >= 0; i--)
        {
            if (!incomingGuids.Contains(Plans[i].Guid))
            {
                Plans.RemoveAt(i);
            }
        }

        for (var i = 0; i < plans.Count; i++)
        {
            var plan = plans[i];
            if (!existingPlans.TryGetValue(plan.Guid, out var existing))
            {
                Plans.Insert(i, new PowerPlanItemViewModel(plan));
                continue;
            }

            var existingIndex = Plans.IndexOf(existing);
            existing.UpdateFrom(plan);
            if (existingIndex != i)
            {
                Plans.Move(existingIndex, i);
            }
        }
    }

    public void AddExternalStatus(string message, bool isError = false)
    {
        SetStatus(message, isError ? InfoBarSeverity.Error : InfoBarSeverity.Informational);
    }

    public void AddExternalStatus(string message, InfoBarSeverity severity)
    {
        SetStatus(message, severity);
    }

    public async Task RefreshFromExternalAsync(bool forceRefresh = false)
    {
        await RefreshPlansAsync(forceRefresh: forceRefresh);
    }

    public bool TryApplyActivePlanFromExternal(string activePlanGuid)
    {
        if (!Plans.Any(plan => string.Equals(plan.Guid, activePlanGuid, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        ApplyActivePlan(activePlanGuid);
        return true;
    }

    private static string BuildCopyPlanName(string? planName)
    {
        var baseName = string.IsNullOrWhiteSpace(planName)
            ? LocalizationService.Get("Main.DefaultPlanName")
            : planName.Trim();
        var suffix = LocalizationService.Get("Main.CopySuffix");
        return $"{baseName} - {suffix}";
    }

    private IReadOnlyList<PowerPlanInfo> BuildPlanSnapshot()
    {
        return Plans
            .Select(plan => new PowerPlanInfo
            {
                Guid = plan.Guid,
                Name = plan.Name,
                IsActive = plan.IsActive
            })
            .ToArray();
    }
}

public sealed class PowerPlanItemViewModel : INotifyPropertyChanged
{
    private static readonly string CopyPlanButtonTextValue = LocalizationService.Get("Main.CopyPlanButton");
    private string _name;
    private bool _isActive;

    public PowerPlanItemViewModel(PowerPlanInfo model)
    {
        Guid = model.Guid;
        _name = model.Name;
        _isActive = model.IsActive;
    }

    public string Guid { get; }
    public string Name
    {
        get => _name;
        private set
        {
            if (string.Equals(_name, value, StringComparison.Ordinal))
            {
                return;
            }

            _name = value;
            OnPropertyChanged();
        }
    }

    public string CopyButtonText => CopyPlanButtonTextValue;

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

    public void UpdateFrom(PowerPlanInfo model)
    {
        Name = model.Name;
        IsActive = model.IsActive;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
