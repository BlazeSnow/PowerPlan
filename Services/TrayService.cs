using PowerPlan.Models;

namespace PowerPlan.Services;

// NOTE:
// WinUI 3 single-project MSIX cannot directly use WinForms NotifyIcon in this setup.
// This service keeps the tray API surface so the app can compile and continue evolving.
public sealed class TrayService : IDisposable
{
    private readonly Func<Task<IReadOnlyList<PowerPlanInfo>>> _getPlansAsync;
    private readonly Func<string, Task> _setActivePlanAsync;
    private readonly Func<bool> _isStartupEnabled;
    private readonly Action<bool> _setStartupEnabled;
    private readonly Action _showMainWindow;
    private readonly Action _exitApplication;
    private readonly Action<string> _log;

    public TrayService(
        Func<Task<IReadOnlyList<PowerPlanInfo>>> getPlansAsync,
        Func<string, Task> setActivePlanAsync,
        Func<bool> isStartupEnabled,
        Action<bool> setStartupEnabled,
        Action showMainWindow,
        Action exitApplication,
        Action<string> log)
    {
        _getPlansAsync = getPlansAsync;
        _setActivePlanAsync = setActivePlanAsync;
        _isStartupEnabled = isStartupEnabled;
        _setStartupEnabled = setStartupEnabled;
        _showMainWindow = showMainWindow;
        _exitApplication = exitApplication;
        _log = log;
    }

    public async Task InitializeAsync()
    {
        await RefreshPlansAsync();
        _log("[Tray] Disabled in current build configuration.");
    }

    public async Task RefreshPlansAsync()
    {
        try
        {
            _ = await _getPlansAsync();
        }
        catch (Exception ex)
        {
            _log($"[Tray] Refresh failed: {ex.Message}");
        }
    }

    public void ShowBalloon(string message)
    {
        _log($"[Tray] {message}");
    }

    public void Dispose()
    {
        // no-op
    }
}