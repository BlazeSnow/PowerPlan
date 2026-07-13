using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Media;
using System.Runtime.InteropServices;
using WinRT.Interop;

namespace PowerPlan.Services;

public sealed class WindowService
{
    private Window? _window;

    public Window? Window => _window;
    public DispatcherQueue? DispatcherQueue => _window?.DispatcherQueue;
    public bool IsVisible => _window is not null && IsWindowVisible(WindowNative.GetWindowHandle(_window));

    public Window EnsureWindowCreated()
    {
        return _window ??= new Window();
    }

    public void Configure(ShellPage shellPage)
    {
        var window = EnsureWindowCreated();
        window.Title = LocalizationService.Get("App.WindowTitle", "PowerPlan");
        window.ExtendsContentIntoTitleBar = true;
        window.Content = shellPage;
        window.SetTitleBar(shellPage.AppTitleBarElement);
        ApplySystemBackdrop(window);
        SetWindowIcon(window);
    }

    public void Show()
    {
        if (_window is null)
        {
            return;
        }

        _ = ShowWindow(WindowNative.GetWindowHandle(_window), 5);
        _window.Activate();
    }

    public void Hide()
    {
        if (_window is not null)
        {
            _ = ShowWindow(WindowNative.GetWindowHandle(_window), 0);
        }
    }

    private static void SetWindowIcon(Window window)
    {
        try
        {
            var hwnd = WindowNative.GetWindowHandle(window);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "powerplan.ico");
            if (File.Exists(iconPath))
            {
                appWindow.SetIcon(iconPath);
            }
        }
        catch
        {
            // Ignore icon setup failures to avoid affecting startup flow.
        }
    }

    private static void ApplySystemBackdrop(Window window)
    {
        try
        {
            window.SystemBackdrop = new MicaBackdrop();
        }
        catch
        {
            // Keep window creation resilient if the current system does not support Mica.
        }
    }

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint hWnd);
}
