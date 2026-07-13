using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using PowerPlan.Models;
using PowerPlan.Services;

namespace PowerPlan.Tray.Services;

public sealed class TrayCoordinator : IDisposable
{
    private readonly DispatcherQueue _uiDispatcherQueue;
    private readonly IPowerPlanService _powerPlanService;
    private readonly ISettingsService _settingsService;
    private readonly IStartupTaskService _startupTaskService;
    private readonly ITrayLocalizer _localizer;
    private readonly Action _showMainWindow;
    private readonly Action _exitApplication;
    private readonly Func<string, Task> _applyActivePlanToShellAsync;
    private readonly Func<IReadOnlyList<PowerPlanInfo>, Task> _publishPlansToShellAsync;
    private readonly Action<string, InfoBarSeverity> _reportVisibleStatus;
    private readonly SemaphoreSlim _stateSemaphore = new(1, 1);

    private TrayService? _trayService;
    private bool _disposed;

    public TrayCoordinator(
        DispatcherQueue uiDispatcherQueue,
        IPowerPlanService powerPlanService,
        ISettingsService settingsService,
        IStartupTaskService startupTaskService,
        ITrayLocalizer localizer,
        Action showMainWindow,
        Action exitApplication,
        Func<string, Task> applyActivePlanToShellAsync,
        Func<IReadOnlyList<PowerPlanInfo>, Task> publishPlansToShellAsync,
        Action<string, InfoBarSeverity> reportVisibleStatus)
    {
        _uiDispatcherQueue = uiDispatcherQueue;
        _powerPlanService = powerPlanService;
        _settingsService = settingsService;
        _startupTaskService = startupTaskService;
        _localizer = localizer;
        _showMainWindow = showMainWindow;
        _exitApplication = exitApplication;
        _applyActivePlanToShellAsync = applyActivePlanToShellAsync;
        _publishPlansToShellAsync = publishPlansToShellAsync;
        _reportVisibleStatus = reportVisibleStatus;
    }

    public bool IsEnabled => _trayService is not null;

    public async Task EnsureEnabledAsync()
    {
        if (_disposed)
        {
            return;
        }

        await _stateSemaphore.WaitAsync();
        try
        {
            if (_trayService is not null)
            {
                return;
            }

            var trayService = new TrayService(
                _uiDispatcherQueue,
                getPlansAsync: forceRefresh => _powerPlanService.GetPlansAsync(forceRefresh),
                setActivePlanAsync: SetActivePlanAsync,
                getHiddenUltimatePlanGuid: GetHiddenUltimatePlanGuid,
                activateHiddenUltimatePlanAsync: ActivateHiddenUltimatePlanAsync,
                isStartupEnabled: () => _settingsService.Current.AutoStart,
                setStartupEnabled: SetStartupEnabledAsync,
                onPlansRefreshed: _publishPlansToShellAsync,
                showMainWindow: _showMainWindow,
                exitApplication: _exitApplication,
                log: _reportVisibleStatus,
                localizer: _localizer);

            _trayService = trayService;
            try
            {
                await trayService.InitializeAsync();
            }
            catch
            {
                trayService.Dispose();
                _trayService = null;
                throw;
            }
        }
        finally
        {
            _stateSemaphore.Release();
        }
    }

    public async Task DisableAsync()
    {
        await _stateSemaphore.WaitAsync();
        try
        {
            _trayService?.Dispose();
            _trayService = null;
        }
        finally
        {
            _stateSemaphore.Release();
        }
    }

    public void UpdatePlansSnapshot(IReadOnlyList<PowerPlanInfo> plans)
    {
        _trayService?.UpdatePlansSnapshot(plans);
    }

    public void UpdateStatus()
    {
        _trayService?.UpdateStatus();
    }

    public async Task RefreshPlansAsync(bool forceRefresh)
    {
        var trayService = _trayService;
        if (trayService is not null)
        {
            await trayService.RefreshPlansAsync(forceRefresh);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _trayService?.Dispose();
        _trayService = null;
    }

    private async Task SetActivePlanAsync(string guid)
    {
        await _powerPlanService.SetActivePlanAsync(guid);
        await _applyActivePlanToShellAsync(guid);
        _reportVisibleStatus(_localizer.Format("App.Status.TraySwitched", guid), InfoBarSeverity.Success);
    }

    private string? GetHiddenUltimatePlanGuid()
    {
        var guid = _settingsService.Current.UltimatePerformancePlanGuid;
        return string.IsNullOrWhiteSpace(guid) ? null : guid;
    }

    private async Task ActivateHiddenUltimatePlanAsync(string guid)
    {
        try
        {
            await _powerPlanService.SetActivePlanAsync(guid);
            await RefreshPlansAsync(forceRefresh: true);
        }
        catch
        {
            _settingsService.Current.UltimatePerformancePlanGuid = string.Empty;
            try
            {
                await _settingsService.SaveCurrentAsync();
            }
            catch
            {
                // Keep tray activation failure focused on the power plan operation.
            }

            await RefreshPlansAsync(forceRefresh: true);
            throw;
        }
    }

    private async Task<bool> SetStartupEnabledAsync(bool enabled)
    {
        try
        {
            var effective = await _startupTaskService.SetEnabledAsync(enabled);
            _settingsService.Current.AutoStart = effective;
            await _settingsService.SaveCurrentAsync();
            var state = _localizer.Get(effective ? "App.Status.On" : "App.Status.Off");
            _reportVisibleStatus(_localizer.Format("App.Status.TrayAutoStart", state), InfoBarSeverity.Success);
            return effective;
        }
        catch (Exception ex)
        {
            _reportVisibleStatus(_localizer.Format("App.Status.TrayAutoStartFailed", ex.Message), InfoBarSeverity.Error);
            return _settingsService.Current.AutoStart;
        }
    }
}
