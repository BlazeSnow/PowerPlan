using Microsoft.UI.Dispatching;
using PowerPlan.Models;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace PowerPlan.Services;

public sealed class TrayService : IDisposable
{
    private static readonly string AppTitleText = LocalizationService.Get("App.WindowTitle", "PowerPlan");
    private static readonly string OpenMainWindowText = "\u2302 " + LocalizationService.Get("Tray.Menu.OpenMainWindow");
    private static readonly string OpenHiddenUltimateText = "\u26A1 " + LocalizationService.Get("Tray.Menu.OpenHiddenUltimate");
    private static readonly string RefreshPlansText = "\u21BB " + LocalizationService.Get("Tray.Menu.RefreshPlans");
    private static readonly string EnableAutoStartText = "\u23FB " + LocalizationService.Get("Tray.Menu.EnableAutoStart");
    private static readonly string DisableAutoStartText = "\u23FB " + LocalizationService.Get("Tray.Menu.DisableAutoStart");
    private static readonly string ExitText = "\u2715 " + LocalizationService.Get("Tray.Menu.Exit");

    private const int CommandOpenMainWindow = 1001;
    private const int CommandRefreshPlans = 1002;
    private const int CommandToggleStartup = 1003;
    private const int CommandExit = 1004;
    private const int CommandHiddenUltimate = 1005;
    private const int CommandPlanBase = 2000;

    private readonly Func<bool, Task<IReadOnlyList<PowerPlanInfo>>> _getPlansAsync;
    private readonly Func<string, Task> _setActivePlanAsync;
    private readonly Func<string?> _getHiddenUltimatePlanGuid;
    private readonly Func<string, Task> _activateHiddenUltimatePlanAsync;
    private readonly Func<bool> _isStartupEnabled;
    private readonly Func<bool, Task<bool>> _setStartupEnabled;
    private readonly Func<bool> _isDarkTheme;
    private readonly Func<Task> _onPlansRefreshed;
    private readonly Action _showMainWindow;
    private readonly Action _exitApplication;
    private readonly Action<string, InfoBarSeverity> _log;
    private readonly DispatcherQueue _uiDispatcherQueue;

    private readonly object _plansLock = new();
    private readonly object _refreshTaskLock = new();
    private IReadOnlyList<PowerPlanInfo> _cachedPlans = Array.Empty<PowerPlanInfo>();
    private Task? _refreshPlansTask;
    private bool _refreshPlansTaskForceRefresh;
    private bool _pendingForceRefresh;

    private nint _messageWindowHandle;
    private nint _moduleHandle;
    private nint _trayIconHandle;
    private uint _taskbarCreatedMessage;
    private string _windowClassName = string.Empty;
    private readonly WndProc _windowProc;
    private bool _trayIconAdded;
    private bool _ownsTrayIcon;
    private bool _disposed;

    public TrayService(
        DispatcherQueue uiDispatcherQueue,
        Func<bool, Task<IReadOnlyList<PowerPlanInfo>>> getPlansAsync,
        Func<string, Task> setActivePlanAsync,
        Func<string?> getHiddenUltimatePlanGuid,
        Func<string, Task> activateHiddenUltimatePlanAsync,
        Func<bool> isStartupEnabled,
        Func<bool, Task<bool>> setStartupEnabled,
        Func<bool> isDarkTheme,
        Func<Task> onPlansRefreshed,
        Action showMainWindow,
        Action exitApplication,
        Action<string, InfoBarSeverity> log)
    {
        _uiDispatcherQueue = uiDispatcherQueue ?? throw new ArgumentNullException(nameof(uiDispatcherQueue));
        _getPlansAsync = getPlansAsync;
        _setActivePlanAsync = setActivePlanAsync;
        _getHiddenUltimatePlanGuid = getHiddenUltimatePlanGuid;
        _activateHiddenUltimatePlanAsync = activateHiddenUltimatePlanAsync;
        _isStartupEnabled = isStartupEnabled;
        _setStartupEnabled = setStartupEnabled;
        _isDarkTheme = isDarkTheme;
        _onPlansRefreshed = onPlansRefreshed;
        _showMainWindow = showMainWindow;
        _exitApplication = exitApplication;
        _log = log;
        _windowProc = WindowProc;
    }

    public async Task InitializeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await RunOnUiThreadAsync(() =>
        {
            EnsureMessageWindow();
            AddTrayIcon();
        });

        await RefreshPlansAsync();
        _log(LocalizationService.Get("Tray.Init"), InfoBarSeverity.Success);
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

    private async Task RefreshPlansCoreAsync(bool forceRefresh)
    {
        try
        {
            var plans = await _getPlansAsync(forceRefresh);
            UpdatePlansSnapshot(plans);
            await _onPlansRefreshed();
        }
        catch (Exception ex)
        {
            _log(LocalizationService.Format("Tray.RefreshFailed", ex.Message), InfoBarSeverity.Error);
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

    public void UpdatePlansSnapshot(IReadOnlyList<PowerPlanInfo> plans)
    {
        lock (_plansLock)
        {
            _cachedPlans = plans
                .Select(plan => new PowerPlanInfo
                {
                    Guid = plan.Guid,
                    Name = plan.Name,
                    IsActive = plan.IsActive
                })
                .ToArray();
        }

        UpdateTrayTooltip();
    }

    public void ShowBalloon(string message)
    {
        _log(message, InfoBarSeverity.Informational);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        RunOnUiThread(() =>
        {
            RemoveTrayIcon();
            if (_messageWindowHandle != IntPtr.Zero)
            {
                _ = DestroyWindow(_messageWindowHandle);
                _messageWindowHandle = IntPtr.Zero;
            }

            if (_moduleHandle != IntPtr.Zero && !string.IsNullOrWhiteSpace(_windowClassName))
            {
                _ = UnregisterClass(_windowClassName, _moduleHandle);
                _moduleHandle = IntPtr.Zero;
                _windowClassName = string.Empty;
            }

            if (_ownsTrayIcon && _trayIconHandle != IntPtr.Zero)
            {
                _ = DestroyIcon(_trayIconHandle);
            }

            _trayIconHandle = IntPtr.Zero;
            _ownsTrayIcon = false;
        });
    }

    private void EnsureMessageWindow()
    {
        if (_messageWindowHandle != IntPtr.Zero)
        {
            return;
        }

        _windowClassName = "PowerPlan.TrayWindow." + Guid.NewGuid().ToString("N");
        _moduleHandle = GetModuleHandle(null);
        _taskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");

        var windowClass = new WndClassEx
        {
            cbSize = (uint)Marshal.SizeOf<WndClassEx>(),
            lpfnWndProc = _windowProc,
            hInstance = _moduleHandle,
            lpszClassName = _windowClassName
        };

        if (RegisterClassEx(ref windowClass) == 0)
        {
            throw CreateWin32Exception("RegisterClassEx");
        }

        _messageWindowHandle = CreateWindowEx(
            0,
            _windowClassName,
            AppTitleText,
            0,
            0,
            0,
            0,
            0,
            IntPtr.Zero,
            IntPtr.Zero,
            _moduleHandle,
            IntPtr.Zero);

        if (_messageWindowHandle == IntPtr.Zero)
        {
            throw CreateWin32Exception("CreateWindowEx");
        }
    }

    private void AddTrayIcon()
    {
        if (_trayIconAdded)
        {
            return;
        }

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "powerplan.ico");
        if (File.Exists(iconPath))
        {
            _trayIconHandle = LoadImage(IntPtr.Zero, iconPath, ImageIcon, 0, 0, LrLoadFromFile);
            _ownsTrayIcon = _trayIconHandle != IntPtr.Zero;
        }

        if (_trayIconHandle == IntPtr.Zero)
        {
            _trayIconHandle = LoadIcon(IntPtr.Zero, IdiApplication);
            _ownsTrayIcon = false;
        }

        var iconData = CreateNotifyIconData();
        iconData.uFlags = NifMessage | NifIcon | NifTip | NifShowTip;
        iconData.hIcon = _trayIconHandle;
        iconData.szTip = BuildTooltipText();

        if (!ShellNotifyIcon(NimAdd, ref iconData))
        {
            throw CreateWin32Exception("Shell_NotifyIcon");
        }

        _trayIconAdded = true;

        iconData.uVersion = NotifyIconVersion4;
        _ = ShellNotifyIcon(NimSetVersion, ref iconData);
    }

    private void RemoveTrayIcon()
    {
        if (!_trayIconAdded || _messageWindowHandle == IntPtr.Zero)
        {
            return;
        }

        var iconData = CreateNotifyIconData();
        _ = ShellNotifyIcon(NimDelete, ref iconData);
        _trayIconAdded = false;
    }

    private void UpdateTrayTooltip()
    {
        RunOnUiThread(() =>
        {
            if (!_trayIconAdded || _messageWindowHandle == IntPtr.Zero)
            {
                return;
            }

            var iconData = CreateNotifyIconData();
            iconData.uFlags = NifTip | NifShowTip;
            iconData.szTip = BuildTooltipText();
            _ = ShellNotifyIcon(NimModify, ref iconData);
        });
    }

    private string BuildTooltipText()
    {
        string? activePlanName;
        lock (_plansLock)
        {
            activePlanName = _cachedPlans.FirstOrDefault(plan => plan.IsActive)?.Name;
        }

        var planText = string.IsNullOrWhiteSpace(activePlanName)
            ? LocalizationService.Get("Tray.Tooltip.PlanUnavailable")
            : LocalizationService.Format("Tray.Tooltip.Plan", activePlanName);
        var startupState = LocalizationService.Get(_isStartupEnabled() ? "App.Status.On" : "App.Status.Off");
        var startupText = LocalizationService.Format("Tray.Tooltip.AutoStart", startupState);
        return TruncateTooltip($"{AppTitleText}\n{planText}\n{startupText}");
    }

    private static string TruncateTooltip(string text)
    {
        const int maxTooltipLength = 127;
        return text.Length <= maxTooltipLength
            ? text
            : text[..maxTooltipLength];
    }

    private NotifyIconData CreateNotifyIconData()
    {
        return new NotifyIconData
        {
            cbSize = (uint)Marshal.SizeOf<NotifyIconData>(),
            hWnd = _messageWindowHandle,
            uID = 1,
            uCallbackMessage = TrayCallbackMessage,
            szTip = string.Empty,
            szInfo = string.Empty,
            szInfoTitle = string.Empty
        };
    }

    private nint WindowProc(nint hWnd, uint message, nint wParam, nint lParam)
    {
        if (_disposed)
        {
            return DefWindowProc(hWnd, message, wParam, lParam);
        }

        if (_taskbarCreatedMessage != 0 && message == _taskbarCreatedMessage)
        {
            _trayIconAdded = false;
            AddTrayIcon();
            return IntPtr.Zero;
        }

        if (message == TrayCallbackMessage)
        {
            var mouseMessage = unchecked((uint)lParam.ToInt64()) & 0xFFFF;
            if (mouseMessage is WmLButtonUp or WmRButtonUp or WmContextMenu or NinSelect or NinKeySelect)
            {
                ShowContextMenu();
            }

            return IntPtr.Zero;
        }

        return DefWindowProc(hWnd, message, wParam, lParam);
    }

    private void ShowContextMenu()
    {
        if (_disposed || _messageWindowHandle == IntPtr.Zero)
        {
            return;
        }

        var context = BuildMenuContext();
        if (context.Menu == IntPtr.Zero)
        {
            return;
        }

        try
        {
            if (!GetCursorPos(out var point))
            {
                point = default;
            }

            ApplyNativeMenuTheme(_isDarkTheme());
            _ = SetForegroundWindow(_messageWindowHandle);
            var command = TrackPopupMenu(
                context.Menu,
                TpmReturnCmd | TpmRightButton,
                point.X,
                point.Y,
                0,
                _messageWindowHandle,
                IntPtr.Zero);
            _ = PostMessage(_messageWindowHandle, WmNull, IntPtr.Zero, IntPtr.Zero);

            if (command != 0)
            {
                HandleMenuCommand(command, context);
            }
        }
        finally
        {
            _ = DestroyMenu(context.Menu);
        }
    }

    private static void ApplyNativeMenuTheme(bool useDarkTheme)
    {
        try
        {
            _ = SetPreferredAppMode(useDarkTheme ? PreferredAppMode.ForceDark : PreferredAppMode.ForceLight);
            FlushMenuThemes();
        }
        catch
        {
            // Let Windows fall back to its default native menu rendering when this API is unavailable.
        }
    }

    private TrayMenuContext BuildMenuContext()
    {
        var menu = CreatePopupMenu();
        if (menu == IntPtr.Zero)
        {
            return new TrayMenuContext(IntPtr.Zero, new Dictionary<int, PowerPlanInfo>(), null);
        }

        IReadOnlyList<PowerPlanInfo> plans;
        lock (_plansLock)
        {
            plans = _cachedPlans.ToArray();
        }

        var planCommands = new Dictionary<int, PowerPlanInfo>();
        AppendMenuText(menu, MfDisabled | MfGrayed, 0, AppTitleText);
        AppendMenuText(menu, MfString, CommandOpenMainWindow, OpenMainWindowText);
        AppendMenuSeparator(menu);

        for (var i = 0; i < plans.Count; i++)
        {
            var plan = plans[i];
            var commandId = CommandPlanBase + i;
            var flags = MfString | (plan.IsActive ? MfChecked : 0);
            planCommands[commandId] = CopyPlan(plan);
            AppendMenuText(menu, flags, commandId, "\u26A1 " + plan.Name);
        }

        var hiddenUltimatePlanGuid = _getHiddenUltimatePlanGuid();
        var hasHiddenUltimate = !string.IsNullOrWhiteSpace(hiddenUltimatePlanGuid)
            && !plans.Any(plan => string.Equals(plan.Guid, hiddenUltimatePlanGuid, StringComparison.OrdinalIgnoreCase));
        if (hasHiddenUltimate)
        {
            AppendMenuText(menu, MfString, CommandHiddenUltimate, OpenHiddenUltimateText);
        }

        AppendMenuSeparator(menu);
        AppendMenuText(menu, MfString, CommandRefreshPlans, RefreshPlansText);

        var startupText = _isStartupEnabled()
            ? DisableAutoStartText
            : EnableAutoStartText;
        AppendMenuText(menu, MfString, CommandToggleStartup, startupText);

        AppendMenuSeparator(menu);
        AppendMenuText(menu, MfString, CommandExit, ExitText);

        return new TrayMenuContext(menu, planCommands, hasHiddenUltimate ? hiddenUltimatePlanGuid : null);
    }

    private static PowerPlanInfo CopyPlan(PowerPlanInfo plan)
    {
        return new PowerPlanInfo
        {
            Guid = plan.Guid,
            Name = plan.Name,
            IsActive = plan.IsActive
        };
    }

    private static void AppendMenuText(nint menu, uint flags, int commandId, string text)
    {
        _ = AppendMenu(menu, flags, (UIntPtr)commandId, text);
    }

    private static void AppendMenuSeparator(nint menu)
    {
        _ = AppendMenu(menu, MfSeparator, UIntPtr.Zero, null);
    }

    private void HandleMenuCommand(int command, TrayMenuContext context)
    {
        switch (command)
        {
            case CommandOpenMainWindow:
                _showMainWindow();
                return;
            case CommandRefreshPlans:
                OnRefreshPlansRequested();
                return;
            case CommandToggleStartup:
                _ = ToggleStartupAsync();
                return;
            case CommandExit:
                _ = _uiDispatcherQueue.TryEnqueue(() => _exitApplication());
                return;
            case CommandHiddenUltimate:
                if (!string.IsNullOrWhiteSpace(context.HiddenUltimatePlanGuid))
                {
                    _ = OnActivateHiddenUltimateAsync(context.HiddenUltimatePlanGuid);
                }

                return;
        }

        if (command < CommandPlanBase)
        {
            return;
        }

        if (context.PlanCommands.TryGetValue(command, out var selectedPlan))
        {
            _ = OnSwitchPlanAsync(selectedPlan.Guid, selectedPlan.Name);
        }
    }

    private async Task OnSwitchPlanAsync(string planGuid, string planName)
    {
        try
        {
            await _setActivePlanAsync(planGuid);
            SetActivePlanInCache(planGuid);
            UpdateTrayTooltip();
            _log(LocalizationService.Format("Tray.SwitchTo", planName), InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            _log(LocalizationService.Format("Tray.SwitchFailed", ex.Message), InfoBarSeverity.Error);
        }
    }

    private sealed record TrayMenuContext(
        nint Menu,
        IReadOnlyDictionary<int, PowerPlanInfo> PlanCommands,
        string? HiddenUltimatePlanGuid);

    private async Task OnActivateHiddenUltimateAsync(string planGuid)
    {
        try
        {
            await _activateHiddenUltimatePlanAsync(planGuid);
            _log(LocalizationService.Get("Tray.HiddenUltimateActivated"), InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            _log(LocalizationService.Format("Tray.HiddenUltimateActivateFailed", ex.Message), InfoBarSeverity.Error);
        }
    }

    private void SetActivePlanInCache(string activePlanGuid)
    {
        lock (_plansLock)
        {
            _cachedPlans = _cachedPlans
                .Select(plan => new PowerPlanInfo
                {
                    Guid = plan.Guid,
                    Name = plan.Name,
                    IsActive = string.Equals(plan.Guid, activePlanGuid, StringComparison.OrdinalIgnoreCase)
                })
                .ToArray();
        }
    }

    private async Task ToggleStartupAsync()
    {
        try
        {
            var next = !_isStartupEnabled();
            _ = await _setStartupEnabled(next);
            UpdateTrayTooltip();
        }
        catch (Exception ex)
        {
            _log(LocalizationService.Format("Tray.AutoStartToggleFailed", ex.Message), InfoBarSeverity.Error);
        }
    }

    private void OnRefreshPlansRequested()
    {
        _ = RefreshPlansAsync(forceRefresh: true);
        _log(LocalizationService.Get("Tray.RefreshStarted"), InfoBarSeverity.Informational);
    }

    private static InvalidOperationException CreateWin32Exception(string operation)
    {
        var error = Marshal.GetLastWin32Error();
        var message = error == 0
            ? operation
            : $"{operation}: {new Win32Exception(error).Message}";
        return new InvalidOperationException(message);
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
            _log(LocalizationService.Get("Tray.DispatcherUnavailable"), InfoBarSeverity.Error);
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
            var message = LocalizationService.Get("Tray.DispatcherUnavailable");
            completion.SetException(new InvalidOperationException(message));
        }

        return completion.Task;
    }

    private const uint TrayCallbackMessage = WmApp + 1;
    private const uint WmNull = 0x0000;
    private const uint WmLButtonUp = 0x0202;
    private const uint WmRButtonUp = 0x0205;
    private const uint WmContextMenu = 0x007B;
    private const uint WmUser = 0x0400;
    private const uint WmApp = 0x8000;
    private const uint NinSelect = WmUser;
    private const uint NinKeySelect = WmUser + 1;

    private const uint NimAdd = 0x00000000;
    private const uint NimModify = 0x00000001;
    private const uint NimDelete = 0x00000002;
    private const uint NimSetVersion = 0x00000004;
    private const uint NifMessage = 0x00000001;
    private const uint NifIcon = 0x00000002;
    private const uint NifTip = 0x00000004;
    private const uint NifShowTip = 0x00000080;
    private const uint NotifyIconVersion4 = 4;

    private const uint MfString = 0x00000000;
    private const uint MfGrayed = 0x00000001;
    private const uint MfDisabled = 0x00000002;
    private const uint MfChecked = 0x00000008;
    private const uint MfSeparator = 0x00000800;

    private const uint TpmRightButton = 0x0002;
    private const uint TpmReturnCmd = 0x0100;

    private const uint ImageIcon = 1;
    private const uint LrLoadFromFile = 0x00000010;
    private static readonly nint IdiApplication = new(32512);

    private enum PreferredAppMode
    {
        Default,
        AllowDark,
        ForceDark,
        ForceLight,
        Max
    }

    private delegate nint WndProc(nint hWnd, uint message, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClassEx
    {
        public uint cbSize;
        public uint style;
        [MarshalAs(UnmanagedType.FunctionPtr)]
        public WndProc lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public nint hIconSm;
    }

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
        public uint uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public nint hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShellNotifyIcon(uint dwMessage, ref NotifyIconData lpData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WndClassEx lpWndClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterClass(string lpClassName, nint hInstance);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint RegisterWindowMessage(string lpString);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowEx(
        uint dwExStyle,
        string lpClassName,
        string lpWindowName,
        uint dwStyle,
        int x,
        int y,
        int nWidth,
        int nHeight,
        nint hWndParent,
        nint hMenu,
        nint hInstance,
        nint lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(nint hWnd);

    [DllImport("user32.dll")]
    private static extern nint DefWindowProc(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint LoadImage(nint hinst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint LoadIcon(nint hInstance, nint lpIconName);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint hIcon);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenu(nint hMenu, uint uFlags, UIntPtr uIDNewItem, string? lpNewItem);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(nint hMenu);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int TrackPopupMenu(nint hMenu, uint uFlags, int x, int y, int nReserved, nint hWnd, nint prcRect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Point lpPoint);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("uxtheme.dll", EntryPoint = "#135", ExactSpelling = true)]
    private static extern PreferredAppMode SetPreferredAppMode(PreferredAppMode appMode);

    [DllImport("uxtheme.dll", EntryPoint = "#136", ExactSpelling = true)]
    private static extern void FlushMenuThemes();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint GetModuleHandle(string? lpModuleName);
}
