using System.ComponentModel;
using System.Runtime.InteropServices;

namespace PowerPlan.Tray.Services;

internal sealed class TrayNativeHost : IDisposable
{
    private const uint TrayCallbackMessage = WmApp + 1;
    private const string WindowClassName = "PowerPlan.TrayMessageWindow";
    private static readonly Guid TrayIconGuid = new("41C85D02-7EE8-49E4-A0D9-59A83C8FA4F5");
    private static readonly object WindowInstancesLock = new();
    private static readonly Dictionary<nint, TrayNativeHost> WindowInstances = new();
    private static readonly WindowProcedureDelegate WindowProcedure = StaticWindowProcedure;

    private nint _trayWindow;
    private nint _trayIcon;
    private uint _taskbarCreatedMessage;
    private bool _iconAdded;
    private string _tooltipText = string.Empty;
    private bool _disposed;

    public bool IsInitialized => _trayWindow != nint.Zero && _iconAdded && !_disposed;

    public event Action<nint, nint>? MenuRequested;

    public event Action<Exception>? RestoreFailed;

    public void Initialize(string tooltipText)
    {
        if (_trayWindow != nint.Zero || _disposed)
        {
            return;
        }

        _tooltipText = tooltipText;
        try
        {
            CreateTrayWindow();
            LoadTrayIcon();
            AddTrayIcon();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void UpdateTooltip(string tooltipText)
    {
        if (_disposed || !_iconAdded || _trayWindow == nint.Zero || string.Equals(tooltipText, _tooltipText, StringComparison.Ordinal))
        {
            return;
        }

        var data = CreateNotifyIconData(NotifyIconTip, tooltipText);
        if (ShellNotifyIconW(NotifyIconModify, ref data))
        {
            _tooltipText = tooltipText;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
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
        _tooltipText = string.Empty;
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
        var data = CreateNotifyIconData(NotifyIconMessage | NotifyIconIcon | NotifyIconTip | NotifyIconGuid, _tooltipText);
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

        var data = CreateNotifyIconData(NotifyIconGuid, _tooltipText);
        _ = ShellNotifyIconW(NotifyIconDelete, ref data);
        _iconAdded = false;
    }

    private NotifyIconData CreateNotifyIconData(uint flags, string tooltipText)
    {
        return new NotifyIconData
        {
            cbSize = (uint)Marshal.SizeOf<NotifyIconData>(),
            hWnd = _trayWindow,
            uID = 1,
            uFlags = flags,
            uCallbackMessage = TrayCallbackMessage,
            hIcon = _trayIcon,
            szTip = tooltipText,
            szInfo = string.Empty,
            szInfoTitle = string.Empty,
            guidItem = TrayIconGuid
        };
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
                MenuRequested?.Invoke(window, wParam);
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
            RestoreFailed?.Invoke(ex);
        }
    }

    private static nint StaticWindowProcedure(nint window, uint message, nint wParam, nint lParam)
    {
        TrayNativeHost? trayHost;
        lock (WindowInstancesLock)
        {
            WindowInstances.TryGetValue(window, out trayHost);
        }

        return trayHost is null
            ? DefWindowProcW(window, message, wParam, lParam)
            : trayHost.ProcessWindowMessage(window, message, wParam, lParam);
    }

    private static Win32Exception CreateWin32Exception(string message)
    {
        return new Win32Exception(Marshal.GetLastWin32Error(), message);
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

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint WindowProcedureDelegate(nint window, uint message, nint wParam, nint lParam);

    private const uint ErrorClassAlreadyExists = 1410;
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

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint window);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetricsForDpi(int index, uint dpi);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShellNotifyIconW(uint message, ref NotifyIconData data);
}
