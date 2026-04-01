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
    private const int WmRButtonUp = 0x0205;
    private const int WmContextMenu = 0x007B;

    private const int GwlWndProc = -4;

    private const uint MfString = 0x00000000;
    private const uint MfSeparator = 0x00000800;
    private const uint MfPopup = 0x00000010;
    private const uint MfChecked = 0x00000008;

    private const uint TpmReturCmd = 0x0100;
    private const uint TpmRightButton = 0x0002;

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
    }

    public async Task InitializeAsync()
    {
        SubclassWindow();
        AddNotifyIcon();
        await RefreshPlansAsync();
        _log("[\u6258\u76D8] \u5DF2\u521D\u59CB\u5316\u3002\u53F3\u952E\u6258\u76D8\u56FE\u6807\u53EF\u6253\u5F00\u83DC\u5355\u3002");
    }

    public async Task RefreshPlansAsync()
    {
        try
        {
            var plans = await _getPlansAsync();
            lock (_plansLock)
            {
                _cachedPlans = plans;
            }
        }
        catch (Exception ex)
        {
            _log($"[\u6258\u76D8] \u5237\u65B0\u5931\u8D25\uFF1A{ex.Message}");
        }
    }

    public void ShowBalloon(string message)
    {
        _log($"[\u6258\u76D8] {message}");
    }

    public void Dispose()
    {
        RemoveNotifyIcon();
        RestoreWindowProc();
    }

    private void SubclassWindow()
    {
        var wndProcPtr = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate);
        _oldWndProc = SetWindowLongPtr(_mainWindowHandle, GwlWndProc, wndProcPtr);
        if (_oldWndProc == IntPtr.Zero)
        {
            _log("[\u6258\u76D8] \u7ED1\u5B9A\u7A97\u53E3\u6D88\u606F\u5931\u8D25\u3002\u6258\u76D8\u4E0D\u53EF\u7528\u3002");
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
            _log("[\u6258\u76D8] \u6DFB\u52A0\u6258\u76D8\u56FE\u6807\u5931\u8D25\u3002");
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
            hIcon = LoadIcon(IntPtr.Zero, (nint)0x7F00),
            szTip = "PowerPlan",
            szInfo = string.Empty,
            szInfoTitle = string.Empty
        };
    }

    private nint WindowProc(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        if (msg == WmTrayIcon)
        {
            var eventMessage = (int)lParam;
            if (eventMessage == WmLButtonDblClk)
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
        var plansMenu = CreatePopupMenu();

        IReadOnlyList<PowerPlanInfo> plans;
        lock (_plansLock)
        {
            plans = _cachedPlans.ToArray();
        }

        if (plans.Count == 0)
        {
            _ = RefreshPlansAsync();
        }

        for (var i = 0; i < plans.Count; i++)
        {
            var id = MenuPlanBase + (uint)i;
            var flags = MfString | (plans[i].IsActive ? MfChecked : 0);
            _ = AppendMenu(plansMenu, flags, (nuint)id, plans[i].Name);
        }

        _ = AppendMenu(menu, MfString, (nuint)MenuOpenMainWindow, "\u6253\u5F00\u4E3B\u754C\u9762");
        _ = AppendMenu(menu, MfPopup, (nuint)plansMenu, "\u5207\u6362\u7535\u6E90\u8BA1\u5212");
        _ = AppendMenu(menu, MfString, (nuint)MenuRefreshPlans, "\u5237\u65B0\u8BA1\u5212");

        var startupText = _isStartupEnabled() ? "\u5173\u95ED\u5F00\u673A\u81EA\u542F\u52A8" : "\u5F00\u542F\u5F00\u673A\u81EA\u542F\u52A8";
        _ = AppendMenu(menu, MfString, (nuint)MenuToggleStartup, startupText);

        _ = AppendMenu(menu, MfSeparator, 0, string.Empty);
        _ = AppendMenu(menu, MfString, (nuint)MenuExit, "\u9000\u51FA");

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

        _ = DestroyMenu(plansMenu);
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
                _log("[\u6258\u76D8] \u5DF2\u5F00\u59CB\u5237\u65B0\u8BA1\u5212\u5217\u8868\u3002");
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
            _log($"[\u6258\u76D8] \u5DF2\u5207\u6362\u5230\uFF1A{planName}");
            await RefreshPlansAsync();
        }
        catch (Exception ex)
        {
            _log($"[\u6258\u76D8] \u5207\u6362\u5931\u8D25\uFF1A{ex.Message}");
        }
    }

    private void ToggleStartup()
    {
        try
        {
            var next = !_isStartupEnabled();
            _setStartupEnabled(next);
            _log($"[\u6258\u76D8] \u5F00\u673A\u81EA\u542F\u52A8\uFF1A{(next ? "\u5F00\u542F" : "\u5173\u95ED")}");
        }
        catch (Exception ex)
        {
            _log($"[\u6258\u76D8] \u81EA\u542F\u52A8\u5207\u6362\u5931\u8D25\uFF1A{ex.Message}");
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
}