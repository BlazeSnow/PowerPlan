using Microsoft.UI.Dispatching;
using PowerPlan.Models;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PowerPlan.Pages;

public sealed partial class MainPage : Page
{
    private static readonly TimeSpan DuplicateStatusSuppressionWindow = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan StatusDisplayDuration = TimeSpan.FromSeconds(5);
    private const string HiddenUltimateIconGlyph = "\uE890";
    private const string MissingUltimateIconGlyph = "\uE945";
    private readonly SemaphoreSlim _refreshSemaphore = new(1, 1);
    private IPageHost? _pageHost;
    private DispatcherQueueTimer? _statusDismissTimer;
    private bool _isUpdatingSelection;
    private bool _isLoaded;
    private bool _hasLoadedPlans;
    private DateTimeOffset _lastStatusAt;
    private string _lastStatusMessage = string.Empty;
    private InfoBarSeverity _lastStatusSeverity;

    public ObservableCollection<PowerPlanItemViewModel> Plans { get; } = new();

    public MainPage()
    {
        InitializeComponent();
        DataContext = this;
        Loaded += MainPage_Loaded;
        Unloaded += MainPage_Unloaded;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _pageHost = e.Parameter as IPageHost
            ?? throw new InvalidOperationException("MainPage requires an IPageHost navigation parameter.");
        ApplyLocalization();
    }

    private IPageHost PageHost => _pageHost
        ?? throw new InvalidOperationException("MainPage has not been initialized with an IPageHost.");

    private void MainPage_Unloaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = false;
        DisposeStatusDismissTimer();
        StatusInfoBar.IsOpen = false;
    }

    private void DisposeStatusDismissTimer()
    {
        if (_statusDismissTimer is null)
        {
            return;
        }

        _statusDismissTimer.Stop();
        _statusDismissTimer.Tick -= OnStatusDismissTimerTick;
        _statusDismissTimer = null;
    }

    private async void MainPage_Loaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = true;
        if (_hasLoadedPlans)
        {
            return;
        }

        try
        {
            await RefreshPlansAsync();
            if (!_isLoaded)
            {
                return;
            }

            _hasLoadedPlans = true;
            SetStatus(PageHost.GetString("Main.Status.InitSuccess"), InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            if (_isLoaded)
            {
                SetStatus(PageHost.FormatString("Main.Status.InitFailed", ex.Message), InfoBarSeverity.Error);
            }
        }
    }

    private void ApplyLocalization()
    {
        UltimateCard.Header = PageHost.GetString("Main.UltimateMissingTitle");
        UltimateCard.Description = PageHost.GetString("Main.UltimateMissingMessage");
        RefreshPlansButton.Content = PageHost.GetString("Main.RefreshPlansButton");
        PlanPickerTitleText.Text = PageHost.GetString("Main.PlanPickerTitle");
        CreateUltimateButton.Content = PageHost.GetString("Main.CreateUltimateButton");
        DeletePlanHintText.Text = PageHost.GetString("Main.DeletePlanHint");
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
            var plans = await PageHost.PowerPlanService.GetPlansAsync(forceRefresh);
            if (!_isLoaded)
            {
                return;
            }

            ApplyPlansToView(plans);
            PageHost.UpdateTrayPlans(plans);

            if (updateStatus)
            {
                SetStatus(PageHost.FormatString("Main.Status.PlansLoaded", plans.Count), InfoBarSeverity.Success);
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

        EnsureStatusDismissTimer();
        _statusDismissTimer!.Stop();
        StatusInfoBar.Severity = severity;
        StatusInfoBar.Title = DateTime.Now.ToString("HH:mm:ss");
        StatusInfoBar.Message = message;
        StatusInfoBar.IsOpen = true;
        _statusDismissTimer.Start();
    }

    private void EnsureStatusDismissTimer()
    {
        if (_statusDismissTimer is not null)
        {
            return;
        }

        _statusDismissTimer = DispatcherQueue.CreateTimer();
        _statusDismissTimer.Interval = StatusDisplayDuration;
        _statusDismissTimer.IsRepeating = false;
        _statusDismissTimer.Tick += OnStatusDismissTimerTick;
    }

    private void OnStatusDismissTimerTick(DispatcherQueueTimer sender, object args)
    {
        sender.Stop();
        StatusInfoBar.IsOpen = false;
    }

    private void OnStatusInfoBarClosed(InfoBar sender, InfoBarClosedEventArgs args)
    {
        _statusDismissTimer?.Stop();
    }

    private async void OnRefreshClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            await RefreshPlansAsync(forceRefresh: true);
        }
        catch (Exception ex)
        {
            SetStatus(PageHost.FormatString("Main.Status.RefreshFailed", ex.Message), InfoBarSeverity.Error);
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
                PlaceholderText = PageHost.GetString("Main.CopyDialogPlaceholder")
            };

            var dialog = new ContentDialog
            {
                Title = PageHost.GetString("Main.CopyDialogTitle"),
                PrimaryButtonText = PageHost.GetString("Main.CopyDialogConfirm"),
                CloseButtonText = PageHost.GetString("Main.CopyDialogCancel"),
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
                SetStatus(PageHost.GetString("Main.Status.CopyNameEmpty"), InfoBarSeverity.Error);
                return;
            }

            await PageHost.PowerPlanService.CopyPlanAsync(planGuid, newName);
            await RefreshPlansAsync(false);
            SetStatus(PageHost.FormatString("Main.Status.CopySuccess", newName), InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            SetStatus(PageHost.FormatString("Main.Status.CopyFailed", ex.Message), InfoBarSeverity.Error);
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
            await PageHost.PowerPlanService.SetActivePlanAsync(selectedPlan.Guid);
            SetStatus(PageHost.FormatString("Main.Status.SwitchSuccess", selectedPlan.Name), InfoBarSeverity.Success);
            ApplyActivePlan(selectedPlan.Guid);
            PageHost.UpdateTrayPlans(BuildPlanSnapshot());
        }
        catch (Exception ex)
        {
            SetStatus(PageHost.FormatString("Main.Status.SwitchFailed", ex.Message), InfoBarSeverity.Error);
            var activePlan = Plans.FirstOrDefault(x => x.IsActive);
            if (activePlan is not null)
            {
                SelectPlan(activePlan);
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
            SelectPlan(selected);
        }
    }

    private void SelectPlan(PowerPlanItemViewModel? plan)
    {
        try
        {
            _isUpdatingSelection = true;
            PlansListView.SelectedItem = plan;
        }
        finally
        {
            _isUpdatingSelection = false;
        }
    }

    private async void OnCreateUltimateClicked(object sender, RoutedEventArgs e)
    {
        var isActivatingSavedUltimate = false;

        try
        {
            var savedUltimatePlanGuid = PageHost.SettingsService.Current.UltimatePerformancePlanGuid;
            if (!string.IsNullOrWhiteSpace(savedUltimatePlanGuid))
            {
                isActivatingSavedUltimate = true;
                await PageHost.PowerPlanService.SetActivePlanAsync(savedUltimatePlanGuid);
                await RefreshPlansAsync(false);
                SetStatus(PageHost.GetString("Main.Status.UltimateActivated"), InfoBarSeverity.Success);
                return;
            }

            var createdGuid = await PageHost.PowerPlanService.CreateUltimatePerformancePlanAsync();
            PageHost.SettingsService.Current.UltimatePerformancePlanGuid = createdGuid;
            await PageHost.SettingsService.SaveCurrentAsync();

            await RefreshPlansAsync(false);
            SetStatus(PageHost.GetString("Main.Status.UltimateCreated"), InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            if (isActivatingSavedUltimate)
            {
                PageHost.SettingsService.Current.UltimatePerformancePlanGuid = string.Empty;
                try
                {
                    await PageHost.SettingsService.SaveCurrentAsync();
                }
                catch
                {
                    // Keep failure handling focused on the power plan operation.
                }

                await RefreshPlansAsync(false);
                SetStatus(PageHost.FormatString("Main.Status.UltimateActivateFailed", ex.Message), InfoBarSeverity.Error);
                return;
            }

            SetStatus(PageHost.FormatString("Main.Status.UltimateCreateFailed", ex.Message), InfoBarSeverity.Error);
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
                Plans.Insert(i, new PowerPlanItemViewModel(plan, PageHost.GetString("Main.CopyPlanButton")));
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

    private bool IsVisibleUltimatePerformancePlan(PowerPlanInfo plan, string? savedUltimatePlanGuid)
    {
        return PageHost.PowerPlanService.IsUltimatePerformancePlan(plan)
            || (!string.IsNullOrWhiteSpace(savedUltimatePlanGuid)
                && string.Equals(plan.Guid, savedUltimatePlanGuid, StringComparison.OrdinalIgnoreCase));
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

    public void ApplyPlansFromExternalSnapshot(IReadOnlyList<PowerPlanInfo> plans)
    {
        ApplyPlansToView(plans);
        _hasLoadedPlans = true;
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

    private string BuildCopyPlanName(string? planName)
    {
        var baseName = string.IsNullOrWhiteSpace(planName)
            ? PageHost.GetString("Main.DefaultPlanName")
            : planName.Trim();
        var suffix = PageHost.GetString("Main.CopySuffix");
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

    private void ApplyPlansToView(IReadOnlyList<PowerPlanInfo> plans)
    {
        SynchronizePlans(plans);

        var savedUltimatePlanGuid = PageHost.SettingsService.Current.UltimatePerformancePlanGuid;
        var hasUltimate = plans.Any(plan => IsVisibleUltimatePerformancePlan(plan, savedUltimatePlanGuid));
        var hasHiddenUltimate = !string.IsNullOrWhiteSpace(savedUltimatePlanGuid)
            && !plans.Any(plan => string.Equals(plan.Guid, savedUltimatePlanGuid, StringComparison.OrdinalIgnoreCase));

        UltimateCard.Visibility = hasUltimate ? Visibility.Collapsed : Visibility.Visible;
        CreateUltimateButton.Visibility = hasUltimate ? Visibility.Collapsed : Visibility.Visible;

        if (!hasUltimate)
        {
            UltimateCardIcon.Glyph = hasHiddenUltimate ? HiddenUltimateIconGlyph : MissingUltimateIconGlyph;
            UltimateCard.Header = PageHost.GetString(hasHiddenUltimate ? "Main.UltimateHiddenTitle" : "Main.UltimateMissingTitle");
            UltimateCard.Description = PageHost.GetString(hasHiddenUltimate ? "Main.UltimateHiddenMessage" : "Main.UltimateMissingMessage");
            CreateUltimateButton.Content = PageHost.GetString(hasHiddenUltimate ? "Main.ActivateUltimateButton" : "Main.CreateUltimateButton");
        }

        SelectPlan(Plans.FirstOrDefault(x => x.IsActive));
    }
}

public sealed class PowerPlanItemViewModel : INotifyPropertyChanged
{
    private readonly string _copyButtonText;
    private string _name;
    private bool _isActive;

    public PowerPlanItemViewModel(PowerPlanInfo model, string copyButtonText)
    {
        Guid = model.Guid;
        _name = model.Name;
        _isActive = model.IsActive;
        _copyButtonText = copyButtonText;
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

    public string CopyButtonText => _copyButtonText;

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
