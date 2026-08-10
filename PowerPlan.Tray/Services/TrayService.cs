using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using PowerPlan.Models;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace PowerPlan.Tray.Services;

public sealed class TrayService : IDisposable
{
    private const string OpenMainWindowIcon = "\u2302 ";
    private const string PowerPlanIcon = "\u26A1 ";
    private const string RefreshPlansIcon = "\u21BB ";
    private const string StartupIcon = "\u23FB ";
    private const string ExitIcon = "\u2715 ";
    private const uint FirstPlanCommandId = 1000;
    private const uint OpenMainWindowCommandId = 1;
    private const uint RefreshPlansCommandId = 2;
    private const uint ToggleStartupCommandId = 3;
    private const uint ExitCommandId = 4;
    private const uint ActivateHiddenUltimateCommandId = 5;
    private const uint TrayCallbackMessage = WmApp + 1;
    private const string WindowClassName = "PowerPlan.TrayMessageWindow";
    private static readonly Guid TrayIconGuid = new("41C85D02-7EE8-49E4-A0D9-59A83C8FA4F5");
    private static readonly object WindowInstancesLock = new();
    private static readonly Dictionary<nint, TrayService> WindowInstances = new();
    private static readonly WindowProcedureDelegate WindowProcedure = StaticWindowProcedure;

    private readonly Func<bool, Task<IReadOnlyList<PowerPlanInfo>>> _getPlansAsync;
    private readonly Func<string, Task> _setActivePlanAsync;
    private readonly Func<string?> _getHiddenUltimatePlanGuid;
    private readonly Func<string, Task> _activateHiddenUltimatePlanAsync;
    private readonly Func<bool> _isStartupEnabled;
    private readonly Func<bool, Task<bool>> _setStartupEnabled;
    private readonly Func<IReadOnlyList<PowerPlanInfo>, Task> _onPlansRefreshed;
    private readonly Action _showMainWindow;
    private readonly Action _exitApplication;
    private readonly Action<string, InfoBarSeverity> _log;
    private readonly ITrayLocalizer _localizer;
    private readonly DispatcherQueue _uiDispatcherQueue;

    private readonly object _plansLock = new();
    private readonly object _refreshTaskLock = new();
    private IReadOnlyList<PowerPlanInfo> _cachedPlans = Array.Empty<PowerPlanInfo>();
    private Task? _refreshPlansTask;
    private bool _refreshPlansTaskForceRefresh;
    private bool _pendingForceRefresh;

    private nint _trayWindow;
    private nint _trayIcon;
    private uint _taskbarCreatedMessage;
    private bool _iconAdded;
    private string _lastTooltipText = string.Empty;
    private bool _disposed;

    public TrayService(
        DispatcherQueue uiDispatcherQueue,
        Func<bool, Task<IReadOnlyList<PowerPlanInfo>>> getPlansAsync,
        Func<string, Task> setActivePlanAsync,
        Func<string?> getHiddenUltimatePlanGuid,
        Func<string, Task> activateHiddenUltimatePlanAsync,
        Func<bool> isStartupEnabled,
        Func<bool, Task<bool>> setStartupEnabled,
        Func<IReadOnlyList<PowerPlanInfo>, Task> onPlansRefreshed,
        Action showMainWindow,
        Action exitApplication,
        Action<string, InfoBarSeverity> log,
        ITrayLocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(uiDispatcherQueue);
        ArgumentNullException.ThrowIfNull(localizer);
        _uiDispatcherQueue = uiDispatcherQueue;
        _getPlansAsync = getPlansAsync;
        _setActivePlanAsync = setActivePlanAsync;
        _getHiddenUltimatePlanGuid = getHiddenUltimatePlanGuid;
        _activateHiddenUltimatePlanAsync = activateHiddenUltimatePlanAsync;
        _isStartupEnabled = isStartupEnabled;
        _setStartupEnabled = setStartupEnabled;
        _onPlansRefreshed = onPlansRefreshed;
        _showMainWindow = showMainWindow;
        _exitApplication = exitApplication;
        _log = log;
        _localizer = localizer;
    }

    public bool IsInitialized => _trayWindow != nint.Zero && _iconAdded && !_disposed;

    public async Task InitializeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await RunOnUiThreadAsync(InitializeNativeTray);
        await RefreshPlansAsync();
        _log(_localizer.Get("Tray.Init"), InfoBarSeverity.Success);
    }

    public async Task RefreshPlansAsync(bool forceRefresh = false)
    {
        var nextForceRefresh = forceRefresh;
        while (true)
        {
            Task refreshTask;

            lock (_refreshTaskLock)
            {
                if (_refreshPlansTask is null)
                {
                    if (nextForceRefresh)
                    {
                        _pendingForceRefresh = false;
                    }

                    _refreshPlansTask = RefreshPlansCoreAsync(nextForceRefresh);
                    _refreshPlansTaskForceRefresh = nextForceRefresh;
                }
                else if (nextForceRefresh && !_refreshPlansTaskForceRefresh)
                {
                    _pendingForceRefresh = true;
                }

                refreshTask = _refreshPlansTask
                    ?? throw new InvalidOperationException("Refresh task was not created.");
            }

            await refreshTask;

            lock (_refreshTaskLock)
            {
                if (!forceRefresh || !_pendingForceRefresh)
                {
                    return;
                }

                nextForceRefresh = true;
            }
        }
    }

    public void UpdatePlansSnapshot(IReadOnlyList<PowerPlanInfo> plans)
    {
        lock (_plansLock)
        {
            _cachedPlans = plans.ToArray();
        }

        UpdateTrayIcon();
    }

    public void UpdateStatus()
    {
        UpdateTrayIcon();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        RunOnUiThreadSynchronously(DisposeNativeTray);
    }

    private async Task RefreshPlansCoreAsync(bool forceRefresh)
    {
        try
        {
            var plans = await _getPlansAsync(forceRefresh);
            UpdatePlansSnapshot(plans);
            await _onPlansRefreshed(plans);
        }
        catch (Exception ex)
        {
            _log(_localizer.Format("Tray.RefreshFailed", ex.Message), InfoBarSeverity.Error);
        }
        finally
        {
            lock (_refreshTaskLock)
            {
                _refreshPlansTask = null;
                _refreshPlansTaskForceRefresh = false;
            }
        }
    }

    private void InitializeNativeTray()
    {
        if (_trayWindow != nint.Zero || _disposed)
        {
            return;
        }

        try
        {
            CreateTrayWindow();
            LoadTrayIcon();
            AddTrayIcon();
        }
        catch
        {
            DisposeNativeTray();
            throw;
        }
    }

    private void CreateTrayWindow()
    {
        var instance = GetModuleHandleW(null);
        var windowClass = new WindowClassEx
        {
            cbSize = (uint)Marshal.SizeOf<WindowClassEx>(),
            lpfnWndProc = WindowProcedure,
            hInstance = instance,
            lpszClassName = WindowClassName
        };

        if (RegisterClassExW(ref windowClass) == 0 && Marshal.GetLastWin32Error() != ErrorClassAlreadyExists)
        {
            throw CreateWin32Exception("Unable to register the tray message window class.");
        }

        _trayWindow = CreateWindowExW(
            0,
            WindowClassName,
            null,
            0,
            0,
            0,
            0,
            0,
            nint.Zero,
            nint.Zero,
            instance,
            nint.Zero);
        if (_trayWindow == nint.Zero)
        {
            throw CreateWin32Exception("Unable to create the tray message window.");
        }

        lock (WindowInstancesLock)
        {
            WindowInstances[_trayWindow] = this;
        }

        _taskbarCreatedMessage = RegisterWindowMessageW("TaskbarCreated");
        if (_taskbarCreatedMessage == 0)
        {
            throw CreateWin32Exception("Unable to register the TaskbarCreated message.");
        }
    }

    private void LoadTrayIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "powerplan.ico");
        if (!File.Exists(iconPath))
        {
            throw new FileNotFoundException("The tray icon resource was not found.", iconPath);
        }

        var dpi = GetDpiForWindow(_trayWindow);
        var width = GetSystemMetricsForDpi(SystemMetricCxSmallIcon, dpi);
        var height = GetSystemMetricsForDpi(SystemMetricCySmallIcon, dpi);
        _trayIcon = LoadImageW(
            nint.Zero,
            iconPath,
            ImageIcon,
            width > 0 ? width : 16,
            height > 0 ? height : 16,
            LoadImageFromFile);
        if (_trayIcon == nint.Zero)
        {
            throw CreateWin32Exception("Unable to load the tray icon resource.");
        }
    }

    private void AddTrayIcon()
    {
        var data = CreateNotifyIconData(NotifyIconMessage | NotifyIconIcon | NotifyIconTip | NotifyIconGuid);
        if (!ShellNotifyIconW(NotifyIconAdd, ref data))
        {
            throw CreateWin32Exception("Unable to add the tray icon.");
        }

        _iconAdded = true;
        data.uTimeoutOrVersion = NotifyIconVersion4;
        if (!ShellNotifyIconW(NotifyIconSetVersion, ref data))
        {
            RemoveTrayIcon();
            throw CreateWin32Exception("Unable to set the tray icon protocol version.");
        }
    }

    private void RemoveTrayIcon()
    {
        if (!_iconAdded || _trayWindow == nint.Zero)
        {
            return;
        }

        var data = CreateNotifyIconData(NotifyIconGuid);
        _ = ShellNotifyIconW(NotifyIconDelete, ref data);
        _iconAdded = false;
    }

    private void DisposeNativeTray()
    {
        RemoveTrayIcon();

        if (_trayIcon != nint.Zero)
        {
            _ = DestroyIcon(_trayIcon);
            _trayIcon = nint.Zero;
        }

        if (_trayWindow != nint.Zero)
        {
            lock (WindowInstancesLock)
            {
                WindowInstances.Remove(_trayWindow);
            }

            _ = DestroyWindow(_trayWindow);
            _trayWindow = nint.Zero;
        }

        _taskbarCreatedMessage = 0;
        _lastTooltipText = string.Empty;
    }

    private void UpdateTrayIcon()
    {
        _ = RunOnUiThread(() =>
        {
            if (_disposed || !_iconAdded || _trayWindow == nint.Zero)
            {
                return;
            }

            var tooltipText = BuildTooltipText();
            if (string.Equals(tooltipText, _lastTooltipText, StringComparison.Ordinal))
            {
                return;
            }

            var data = CreateNotifyIconData(NotifyIconTip);
            if (ShellNotifyIconW(NotifyIconModify, ref data))
            {
                _lastTooltipText = tooltipText;
            }
        });
    }

    private NotifyIconData CreateNotifyIconData(uint flags)
    {
        return new NotifyIconData
        {
            cbSize = (uint)Marshal.SizeOf<NotifyIconData>(),
            hWnd = _trayWindow,
            uID = 1,
            uFlags = flags,
            uCallbackMessage = TrayCallbackMessage,
            hIcon = _trayIcon,
            szTip = BuildTooltipText(),
            szInfo = string.Empty,
            szInfoTitle = string.Empty,
            guidItem = TrayIconGuid
        };
    }

    private string BuildTooltipText()
    {
        string? activePlanName;
        lock (_plansLock)
        {
            activePlanName = _cachedPlans.FirstOrDefault(plan => plan.IsActive)?.Name;
        }

        var planText = string.IsNullOrWhiteSpace(activePlanName)
            ? _localizer.Get("Tray.Tooltip.PlanUnavailable")
            : _localizer.Format("Tray.Tooltip.Plan", activePlanName);
        var startupState = _localizer.Get(_isStartupEnabled() ? "App.Status.On" : "App.Status.Off");
        var startupText = _localizer.Format("Tray.Tooltip.AutoStart", startupState);
        return TruncateTooltip($"{_localizer.Get("App.WindowTitle")}\n{planText}\n{startupText}");
    }

    private static string TruncateTooltip(string text)
    {
        const int maxTooltipLength = 127;
        return text.Length <= maxTooltipLength
            ? text
            : text[..maxTooltipLength];
    }

    private nint ProcessWindowMessage(nint window, uint message, nint wParam, nint lParam)
    {
        if (message == _taskbarCreatedMessage)
        {
            RestoreTrayIconAfterExplorerRestart();
            return nint.Zero;
        }

        if (message == TrayCallbackMessage)
        {
            var notification = (uint)((nuint)lParam & ushort.MaxValue);
            if (notification is WmLeftButtonUp or WmRightButtonUp or WmContextMenu or NotifyIconSelect)
            {
                ShowNativeMenu(window, wParam);
            }

            return nint.Zero;
        }

        return DefWindowProcW(window, message, wParam, lParam);
    }

    private void RestoreTrayIconAfterExplorerRestart()
    {
        if (_disposed || _trayWindow == nint.Zero || _trayIcon == nint.Zero)
        {
            return;
        }

        _iconAdded = false;
        try
        {
            AddTrayIcon();
        }
        catch (Exception ex)
        {
            _log(_localizer.Format("Tray.RefreshFailed", ex.Message), InfoBarSeverity.Error);
        }
    }

    private void ShowNativeMenu(nint window, nint packedPosition)
    {
        if (_disposed || !_iconAdded)
        {
            return;
        }

        var menu = CreatePopupMenu();
        if (menu == nint.Zero)
        {
            _log(_localizer.Get("Tray.DispatcherUnavailable"), InfoBarSeverity.Error);
            return;
        }

        try
        {
            var commands = BuildNativeMenu(menu);
            var position = GetMenuPosition(packedPosition);
            _ = SetForegroundWindow(window);
            var commandId = TrackPopupMenuEx(
                menu,
                TrackPopupReturnCommand | TrackPopupRightButton,
                position.X,
                position.Y,
                window,
                nint.Zero);
            _ = PostMessageW(window, WmNull, nint.Zero, nint.Zero);

            if (commandId != 0 && commands.TryGetValue(commandId, out var command))
            {
                DispatchMenuCommand(command);
            }
        }
        finally
        {
            _ = DestroyMenu(menu);
        }
    }

    private Dictionary<uint, TrayMenuCommand> BuildNativeMenu(nint menu)
    {
        var commands = new Dictionary<uint, TrayMenuCommand>();
        AppendMenuW(menu, MenuDisabled | MenuGrayed | MenuString, 0, _localizer.Get("App.WindowTitle"));
        AppendCommand(menu, commands, OpenMainWindowCommandId, OpenMainWindowIcon + _localizer.Get("Tray.Menu.OpenMainWindow"), new TrayMenuCommand(TrayMenuAction.OpenMainWindow));
        AppendMenuW(menu, MenuSeparator, 0, null);

        IReadOnlyList<PowerPlanInfo> plans;
        lock (_plansLock)
        {
            plans = _cachedPlans.ToArray();
        }

        var commandId = FirstPlanCommandId;
        foreach (var plan in plans)
        {
            var flags = MenuString | (plan.IsActive ? MenuChecked : MenuUnchecked);
            AppendMenuW(menu, flags, commandId, PowerPlanIcon + plan.Name);
            commands[commandId] = new TrayMenuCommand(TrayMenuAction.SwitchPlan, plan.Guid, plan.Name);
            commandId++;
        }

        var hiddenUltimatePlanGuid = _getHiddenUltimatePlanGuid();
        if (!string.IsNullOrWhiteSpace(hiddenUltimatePlanGuid)
            && !plans.Any(plan => string.Equals(plan.Guid, hiddenUltimatePlanGuid, StringComparison.OrdinalIgnoreCase)))
        {
            AppendCommand(
                menu,
                commands,
                ActivateHiddenUltimateCommandId,
                PowerPlanIcon + _localizer.Get("Tray.Menu.OpenHiddenUltimate"),
                new TrayMenuCommand(TrayMenuAction.ActivateHiddenUltimate, hiddenUltimatePlanGuid));
        }

        AppendMenuW(menu, MenuSeparator, 0, null);
        AppendCommand(menu, commands, RefreshPlansCommandId, RefreshPlansIcon + _localizer.Get("Tray.Menu.RefreshPlans"), new TrayMenuCommand(TrayMenuAction.RefreshPlans));
        AppendCommand(
            menu,
            commands,
            ToggleStartupCommandId,
            StartupIcon + (_isStartupEnabled()
                ? _localizer.Get("Tray.Menu.DisableAutoStart")
                : _localizer.Get("Tray.Menu.EnableAutoStart")),
            new TrayMenuCommand(TrayMenuAction.ToggleStartup));
        AppendMenuW(menu, MenuSeparator, 0, null);
        AppendCommand(menu, commands, ExitCommandId, ExitIcon + _localizer.Get("Tray.Menu.Exit"), new TrayMenuCommand(TrayMenuAction.Exit));
        return commands;
    }

    private static void AppendCommand(
        nint menu,
        IDictionary<uint, TrayMenuCommand> commands,
        uint commandId,
        string text,
        TrayMenuCommand command)
    {
        AppendMenuW(menu, MenuString, commandId, text);
        commands[commandId] = command;
    }

    private static NativePoint GetMenuPosition(nint packedPosition)
    {
        var x = (short)((nuint)packedPosition & ushort.MaxValue);
        var y = (short)(((nuint)packedPosition >> 16) & ushort.MaxValue);
        if (x != -1 || y != -1)
        {
            return new NativePoint { X = x, Y = y };
        }

        return GetCursorPos(out var cursorPosition)
            ? cursorPosition
            : default;
    }

    private void DispatchMenuCommand(TrayMenuCommand command)
    {
        if (!_uiDispatcherQueue.TryEnqueue(() => _ = ExecuteMenuCommandAsync(command)))
        {
            _log(_localizer.Get("Tray.DispatcherUnavailable"), InfoBarSeverity.Error);
        }
    }

    private async Task ExecuteMenuCommandAsync(TrayMenuCommand command)
    {
        switch (command.Action)
        {
            case TrayMenuAction.OpenMainWindow:
                _showMainWindow();
                break;
            case TrayMenuAction.SwitchPlan:
                if (command.PlanGuid is not null && command.PlanName is not null)
                {
                    await OnSwitchPlanAsync(command.PlanGuid, command.PlanName);
                }

                break;
            case TrayMenuAction.ActivateHiddenUltimate:
                if (command.PlanGuid is not null)
                {
                    await OnActivateHiddenUltimateAsync(command.PlanGuid);
                }

                break;
            case TrayMenuAction.RefreshPlans:
                OnRefreshPlansRequested();
                break;
            case TrayMenuAction.ToggleStartup:
                await ToggleStartupAsync();
                break;
            case TrayMenuAction.Exit:
                _exitApplication();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(command));
        }
    }

    private async Task OnSwitchPlanAsync(string planGuid, string planName)
    {
        try
        {
            await _setActivePlanAsync(planGuid);
            SetActivePlanInCache(planGuid);
            UpdateTrayIcon();
            _log(_localizer.Format("Tray.SwitchTo", planName), InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            _log(_localizer.Format("Tray.SwitchFailed", ex.Message), InfoBarSeverity.Error);
        }
    }

    private async Task OnActivateHiddenUltimateAsync(string planGuid)
    {
        try
        {
            await _activateHiddenUltimatePlanAsync(planGuid);
            _log(_localizer.Get("Tray.HiddenUltimateActivated"), InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            _log(_localizer.Format("Tray.HiddenUltimateActivateFailed", ex.Message), InfoBarSeverity.Error);
        }
    }

    private void SetActivePlanInCache(string activePlanGuid)
    {
        lock (_plansLock)
        {
            _cachedPlans = _cachedPlans
                .Select(plan => plan with { IsActive = string.Equals(plan.Guid, activePlanGuid, StringComparison.OrdinalIgnoreCase) })
                .ToArray();
        }
    }

    private async Task ToggleStartupAsync()
    {
        try
        {
            var next = !_isStartupEnabled();
            _ = await _setStartupEnabled(next);
            UpdateTrayIcon();
        }
        catch (Exception ex)
        {
            _log(_localizer.Format("Tray.AutoStartToggleFailed", ex.Message), InfoBarSeverity.Error);
        }
    }

    private void OnRefreshPlansRequested()
    {
        _ = RefreshPlansAsync(forceRefresh: true);
        _log(_localizer.Get("Tray.RefreshStarted"), InfoBarSeverity.Informational);
    }

    private bool RunOnUiThread(Action action)
    {
        if (_uiDispatcherQueue.HasThreadAccess)
        {
            action();
            return true;
        }

        if (!_uiDispatcherQueue.TryEnqueue(() => action()))
        {
            _log(_localizer.Get("Tray.DispatcherUnavailable"), InfoBarSeverity.Error);
            return false;
        }

        return true;
    }

    private Task RunOnUiThreadAsync(Action action)
    {
        if (_uiDispatcherQueue.HasThreadAccess)
        {
            action();
            return Task.CompletedTask;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var enqueued = _uiDispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                action();
                completion.SetResult();
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        });

        if (!enqueued)
        {
            completion.SetException(new InvalidOperationException(_localizer.Get("Tray.DispatcherUnavailable")));
        }

        return completion.Task;
    }

    private void RunOnUiThreadSynchronously(Action action)
    {
        if (_uiDispatcherQueue.HasThreadAccess)
        {
            action();
            return;
        }

        using var completion = new ManualResetEventSlim();
        Exception? exception = null;
        if (!_uiDispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    exception = ex;
                }
                finally
                {
                    completion.Set();
                }
            }))
        {
            return;
        }

        completion.Wait();
        if (exception is not null)
        {
            throw new InvalidOperationException("Unable to dispose the tray icon.", exception);
        }
    }

    private static nint StaticWindowProcedure(nint window, uint message, nint wParam, nint lParam)
    {
        TrayService? trayService;
        lock (WindowInstancesLock)
        {
            WindowInstances.TryGetValue(window, out trayService);
        }

        return trayService is null
            ? DefWindowProcW(window, message, wParam, lParam)
            : trayService.ProcessWindowMessage(window, message, wParam, lParam);
    }

    private static Win32Exception CreateWin32Exception(string message)
    {
        return new Win32Exception(Marshal.GetLastWin32Error(), message);
    }

    private enum TrayMenuAction
    {
        OpenMainWindow,
        SwitchPlan,
        ActivateHiddenUltimate,
        RefreshPlans,
        ToggleStartup,
        Exit
    }

    private readonly record struct TrayMenuCommand(TrayMenuAction Action, string? PlanGuid = null, string? PlanName = null);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint cbSize;
        public nint hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public nint hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public nint hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClassEx
    {
        public uint cbSize;
        public uint style;
        public WindowProcedureDelegate? lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        public string? lpszMenuName;
        public string? lpszClassName;
        public nint hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint WindowProcedureDelegate(nint window, uint message, nint wParam, nint lParam);

    private const uint ErrorClassAlreadyExists = 1410;
    private const uint WmNull = 0x0000;
    private const uint WmContextMenu = 0x007B;
    private const uint WmLeftButtonUp = 0x0202;
    private const uint WmRightButtonUp = 0x0205;
    private const uint WmApp = 0x8000;
    private const uint NotifyIconSelect = 0x0400;
    private const uint NotifyIconAdd = 0x00000000;
    private const uint NotifyIconModify = 0x00000001;
    private const uint NotifyIconDelete = 0x00000002;
    private const uint NotifyIconSetVersion = 0x00000004;
    private const uint NotifyIconMessage = 0x00000001;
    private const uint NotifyIconIcon = 0x00000002;
    private const uint NotifyIconTip = 0x00000004;
    private const uint NotifyIconGuid = 0x00000020;
    private const uint NotifyIconVersion4 = 4;
    private const uint ImageIcon = 1;
    private const uint LoadImageFromFile = 0x00000010;
    private const uint MenuString = 0x00000000;
    private const uint MenuDisabled = 0x00000002;
    private const uint MenuGrayed = 0x00000001;
    private const uint MenuChecked = 0x00000008;
    private const uint MenuUnchecked = 0x00000000;
    private const uint MenuSeparator = 0x00000800;
    private const uint TrackPopupReturnCommand = 0x0100;
    private const uint TrackPopupRightButton = 0x0002;
    private const int SystemMetricCxSmallIcon = 49;
    private const int SystemMetricCySmallIcon = 50;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint GetModuleHandleW(string? moduleName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassExW(ref WindowClassEx windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowExW(
        uint extendedStyle,
        string className,
        string? windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        nint parent,
        nint menu,
        nint instance,
        nint parameter);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint DefWindowProcW(nint window, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(nint window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint LoadImageW(nint instance, string name, uint type, int width, int height, uint loadFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint icon);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint RegisterWindowMessageW(string message);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenuW(nint menu, uint flags, uint itemId, string? text);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(nint menu);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint TrackPopupMenuEx(nint menu, uint flags, int x, int y, nint window, nint parameters);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessageW(nint window, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint window);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetricsForDpi(int index, uint dpi);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShellNotifyIconW(uint message, ref NotifyIconData data);
}
