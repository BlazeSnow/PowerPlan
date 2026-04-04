using System.Runtime.InteropServices;
using PowerPlan.Models;

namespace PowerPlan.Services;

public sealed class TrayService : IDisposable
{
    private const uint WmTrayIcon = 0x0400 + 100;

    private const uint NifMessage = 0x00000001;
    private const uint NifIcon = 0x00000002;
    private const uint NifTip = 0x00000004;

    private const uint NimAdd = 0x00000000;
    private const uint NimDelete = 0x00000002;

    private const int WmLButtonDblClk = 0x0203;
    private const int WmLButtonUp = 0x0202;
    private const int WmRButtonUp = 0x0205;
    private const int WmContextMenu = 0x007B;

    private const int GwlWndProc = -4;

    private const uint MfString = 0x00000000;
    private const uint MfSeparator = 0x00000800;
    private const uint MfChecked = 0x00000008;
    private const uint MfDisabled = 0x00000002;
    private const uint MfGrayed = 0x00000001;

    private const uint TpmReturCmd = 0x0100;
    private const uint TpmRightButton = 0x0002;
    private const uint ImageIcon = 1;
    private const uint LrLoadFromFile = 0x00000010;

    private const uint MenuOpenMainWindow = 1001;
    private const uint MenuRefreshPlans = 1002;
    private const uint MenuToggleStartup = 1003;
    private const uint MenuExit = 1099;
    private const uint MenuPlanBase = 2000;

    private readonly nint _mainWindowHandle;
    private readonly Func<Task<IReadOnlyList<PowerPlanInfo>>> _getPlansAsync;
    private readonly Func<string, Task> _setActivePlanAsync;
    private readonly Func<bool> _isStartupEnabled;
    private readonly Action<bool> _setStartupEnabled;
    private readonly Action _showMainWindow;
    private readonly Action _exitApplication;
    private readonly Action<string> _log;

    private readonly object _plansLock = new();
    private IReadOnlyList<PowerPlanInfo> _cachedPlans = Array.Empty<PowerPlanInfo>();

    private readonly WndProc _wndProcDelegate;
    private readonly nint _trayIconHandle;
    private nint _oldWndProc;
    private bool _iconAdded;

    public TrayService(
        nint mainWindowHandle,
        Func<Task<IReadOnlyList<PowerPlanInfo>>> getPlansAsync,
        Func<string, Task> setActivePlanAsync,
        Func<bool> isStartupEnabled,
        Action<bool> setStartupEnabled,
        Action showMainWindow,
        Action exitApplication,
        Action<string> log)
    {
        _mainWindowHandle = mainWindowHandle;
        _getPlansAsync = getPlansAsync;
        _setActivePlanAsync = setActivePlanAsync;
        _isStartupEnabled = isStartupEnabled;
        _setStartupEnabled = setStartupEnabled;
        _showMainWindow = showMainWindow;
        _exitApplication = exitApplication;
        _log = log;

        _wndProcDelegate = WindowProc;
        _trayIconHandle = LoadTrayIcon();
    }

    public async Task InitializeAsync()
    {
        SubclassWindow();
        AddNotifyIcon();
        await RefreshPlansAsync();
        _log(LocalizationService.Get("Tray.Init"));
    }

    public async Task RefreshPlansAsync()
    {
        try
        {
            var plans = await _getPlansAsync();
            UpdatePlansSnapshot(plans);
        }
        catch (Exception ex)
        {
            _log(LocalizationService.Format("Tray.RefreshFailed", ex.Message));
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
    }
    public void ShowBalloon(string message)
    {
        _log(message);
    }

    public void Dispose()
    {
        RemoveNotifyIcon();
        RestoreWindowProc();
        if (_trayIconHandle != IntPtr.Zero)
        {
            _ = DestroyIcon(_trayIconHandle);
        }
    }

    private void SubclassWindow()
    {
        var wndProcPtr = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate);
        _oldWndProc = SetWindowLongPtr(_mainWindowHandle, GwlWndProc, wndProcPtr);
        if (_oldWndProc == IntPtr.Zero)
        {
            _log(LocalizationService.Get("Tray.BindWindowFailed"));
        }
    }

    private void RestoreWindowProc()
    {
        if (_oldWndProc != IntPtr.Zero)
        {
            _ = SetWindowLongPtr(_mainWindowHandle, GwlWndProc, _oldWndProc);
            _oldWndProc = IntPtr.Zero;
        }
    }

    private void AddNotifyIcon()
    {
        var data = CreateNotifyIconData();
        _iconAdded = Shell_NotifyIcon(NimAdd, ref data);
        if (!_iconAdded)
        {
            _log(LocalizationService.Get("Tray.AddIconFailed"));
        }
    }

    private void RemoveNotifyIcon()
    {
        if (!_iconAdded)
        {
            return;
        }

        var data = CreateNotifyIconData();
        _ = Shell_NotifyIcon(NimDelete, ref data);
        _iconAdded = false;
    }

    private NotifyIconData CreateNotifyIconData()
    {
        return new NotifyIconData
        {
            cbSize = (uint)Marshal.SizeOf<NotifyIconData>(),
            hWnd = _mainWindowHandle,
            uID = 1,
            uFlags = NifMessage | NifIcon | NifTip,
            uCallbackMessage = WmTrayIcon,
            hIcon = _trayIconHandle != IntPtr.Zero ? _trayIconHandle : LoadIcon(IntPtr.Zero, (nint)0x7F00),
            szTip = "PowerPlan",
            szInfo = string.Empty,
            szInfoTitle = string.Empty
        };
    }

    private static nint LoadTrayIcon()
    {
        try
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "powerplan.ico");
            if (!File.Exists(iconPath))
            {
                return IntPtr.Zero;
            }

            return LoadImage(IntPtr.Zero, iconPath, ImageIcon, 0, 0, LrLoadFromFile);
        }
        catch
        {
            return IntPtr.Zero;
        }
    }

    private nint WindowProc(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        if (msg == WmTrayIcon)
        {
            var eventMessage = (int)lParam;
            if (eventMessage == WmLButtonUp || eventMessage == WmLButtonDblClk)
            {
                _showMainWindow();
                return IntPtr.Zero;
            }

            if (eventMessage == WmRButtonUp || eventMessage == WmContextMenu)
            {
                ShowContextMenu();
                return IntPtr.Zero;
            }
        }

        return _oldWndProc != IntPtr.Zero
            ? CallWindowProc(_oldWndProc, hWnd, msg, wParam, lParam)
            : DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private void ShowContextMenu()
    {
        var menu = CreatePopupMenu();

        IReadOnlyList<PowerPlanInfo> plans;
        lock (_plansLock)
        {
            plans = _cachedPlans.ToArray();
        }

        if (plans.Count == 0)
        {
            _ = RefreshPlansAsync();
        }

        _ = AppendMenu(menu, MfString | MfDisabled | MfGrayed, 0, LocalizationService.Get("App.WindowTitle", "PowerPlan"));
        _ = AppendMenu(menu, MfString, (nuint)MenuOpenMainWindow, "\u2302 " + LocalizationService.Get("Tray.Menu.OpenMainWindow"));
        _ = AppendMenu(menu, MfSeparator, 0, string.Empty);

        for (var i = 0; i < plans.Count; i++)
        {
            var id = MenuPlanBase + (uint)i;
            var flags = MfString | (plans[i].IsActive ? MfChecked : 0);
            _ = AppendMenu(menu, flags, (nuint)id, "\u26A1 " + plans[i].Name);
        }

        _ = AppendMenu(menu, MfSeparator, 0, string.Empty);
        _ = AppendMenu(menu, MfString, (nuint)MenuRefreshPlans, "\u21BB " + LocalizationService.Get("Tray.Menu.RefreshPlans"));

        var startupText = _isStartupEnabled()
            ? LocalizationService.Get("Tray.Menu.DisableAutoStart")
            : LocalizationService.Get("Tray.Menu.EnableAutoStart");
        _ = AppendMenu(menu, MfString, (nuint)MenuToggleStartup, "\u23FB " + startupText);

        _ = AppendMenu(menu, MfSeparator, 0, string.Empty);
        _ = AppendMenu(menu, MfString, (nuint)MenuExit, "\u2715 " + LocalizationService.Get("Tray.Menu.Exit"));

        _ = SetForegroundWindow(_mainWindowHandle);
        _ = GetCursorPos(out var point);

        var command = TrackPopupMenu(
            menu,
            TpmReturCmd | TpmRightButton,
            point.X,
            point.Y,
            0,
            _mainWindowHandle,
            IntPtr.Zero);

        _ = DestroyMenu(menu);

        HandleMenuCommand(command, plans);
    }

    private void HandleMenuCommand(uint command, IReadOnlyList<PowerPlanInfo> plans)
    {
        if (command == 0)
        {
            return;
        }

        if (command >= MenuPlanBase)
        {
            var index = (int)(command - MenuPlanBase);
            if (index >= 0 && index < plans.Count)
            {
                var selected = plans[index];
                _ = OnSwitchPlanAsync(selected.Guid, selected.Name);
            }

            return;
        }

        switch (command)
        {
            case MenuOpenMainWindow:
                _showMainWindow();
                break;
            case MenuRefreshPlans:
                _ = RefreshPlansAsync();
                _log(LocalizationService.Get("Tray.RefreshStarted"));
                break;
            case MenuToggleStartup:
                ToggleStartup();
                break;
            case MenuExit:
                _exitApplication();
                break;
        }
    }

    private async Task OnSwitchPlanAsync(string planGuid, string planName)
    {
        try
        {
            await _setActivePlanAsync(planGuid);
            SetActivePlanInCache(planGuid);
            _log(LocalizationService.Format("Tray.SwitchTo", planName));
        }
        catch (Exception ex)
        {
            _log(LocalizationService.Format("Tray.SwitchFailed", ex.Message));
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
    private void ToggleStartup()
    {
        try
        {
            var next = !_isStartupEnabled();
            _setStartupEnabled(next);
            var state = LocalizationService.Get(next ? "App.Status.On" : "App.Status.Off");
            _log(LocalizationService.Format("Tray.AutoStartState", state));
        }
        catch (Exception ex)
        {
            _log(LocalizationService.Format("Tray.AutoStartToggleFailed", ex.Message));
        }
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
        public uint uTimeoutOrVersion;
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

    private delegate nint WndProc(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Shell_NotifyIcon(uint dwMessage, ref NotifyIconData lpData);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

    [DllImport("user32.dll")]
    private static extern nint CallWindowProc(nint lpPrevWndFunc, nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern nint DefWindowProc(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenu(nint hMenu, uint uFlags, nuint uIDNewItem, string lpNewItem);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint TrackPopupMenu(nint hMenu, uint uFlags, int x, int y, int nReserved, nint hWnd, nint prcRect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(nint hMenu);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Point lpPoint);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint LoadIcon(nint hInstance, nint lpIconName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint LoadImage(nint hinst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint hIcon);
}
