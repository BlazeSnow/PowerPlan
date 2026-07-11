using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using System.Runtime.InteropServices;
using Windows.UI.ViewManagement;
using WinRT.Interop;

namespace PowerPlan.Services;

public sealed class NativeThemeService : IDisposable
{
    private readonly WindowService _windowService;
    private readonly Action<ElementTheme> _applyTrayTheme;
    private readonly UISettings _uiSettings = new();
    private FrameworkElement? _rootElement;
    private ElementTheme? _lastAppliedTheme;
    private bool _disposed;

    public NativeThemeService(WindowService windowService, Action<ElementTheme> applyTrayTheme)
    {
        _windowService = windowService;
        _applyTrayTheme = applyTrayTheme;
        _uiSettings.ColorValuesChanged += OnColorValuesChanged;
    }

    public ElementTheme EffectiveTheme
    {
        get
        {
            if (_rootElement?.ActualTheme is ElementTheme.Light or ElementTheme.Dark)
            {
                return _rootElement.ActualTheme;
            }

            return IsSystemUsingDarkTheme() ? ElementTheme.Dark : ElementTheme.Light;
        }
    }

    public void AttachToWindowContent()
    {
        var root = _windowService.Window?.Content as FrameworkElement;
        if (ReferenceEquals(root, _rootElement))
        {
            ApplyTheme();
            return;
        }

        if (_rootElement is not null)
        {
            _rootElement.ActualThemeChanged -= OnRootActualThemeChanged;
        }

        _rootElement = root;
        if (_rootElement is not null)
        {
            _rootElement.ActualThemeChanged += OnRootActualThemeChanged;
        }

        ApplyTheme();
    }

    public void ApplyTheme(bool forceTitleBar = false)
    {
        var themeChanged = ApplyThemeFor(EffectiveTheme);
        if (_windowService.IsVisible && (themeChanged || forceTitleBar))
        {
            ApplySystemTitleBarTheme();
        }
    }

    private void OnRootActualThemeChanged(FrameworkElement sender, object args)
    {
        ApplyTheme();
    }

    private void OnColorValuesChanged(UISettings sender, object args)
    {
        var dispatcher = _windowService.DispatcherQueue;
        if (dispatcher is null)
        {
            return;
        }

        if (dispatcher.HasThreadAccess)
        {
            ApplyTheme();
        }
        else
        {
            _ = dispatcher.TryEnqueue(DispatcherQueuePriority.Normal, () => ApplyTheme());
        }
    }

    private bool ApplyThemeFor(ElementTheme theme)
    {
        if (_lastAppliedTheme == theme)
        {
            return false;
        }

        _lastAppliedTheme = theme;
        ApplyNativeMenuTheme(theme);
        _applyTrayTheme(theme);
        return true;
    }

    private bool IsSystemUsingDarkTheme()
    {
        try
        {
            var background = _uiSettings.GetColorValue(UIColorType.Background);
            return background.R < 128 && background.G < 128 && background.B < 128;
        }
        catch
        {
            return false;
        }
    }

    private void ApplySystemTitleBarTheme()
    {
        var window = _windowService.Window;
        if (window is null)
        {
            return;
        }

        var hwnd = WindowNative.GetWindowHandle(window);
        var useDarkMode = EffectiveTheme == ElementTheme.Dark ? 1 : 0;
        var result = DwmSetWindowAttribute(hwnd, DwmaUseImmersiveDarkMode, ref useDarkMode, Marshal.SizeOf<int>());
        if (result != 0)
        {
            _ = DwmSetWindowAttribute(hwnd, DwmaUseImmersiveDarkModeBefore20H1, ref useDarkMode, Marshal.SizeOf<int>());
        }

        ApplyCaptionButtonTheme(window, useDarkMode == 1);
    }

    private static void ApplyCaptionButtonTheme(Window window, bool isDark)
    {
        try
        {
            if (!AppWindowTitleBar.IsCustomizationSupported())
            {
                return;
            }

            var hwnd = WindowNative.GetWindowHandle(window);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            var titleBar = AppWindow.GetFromWindowId(windowId).TitleBar;
            var foreground = isDark ? Windows.UI.Color.FromArgb(255, 255, 255, 255) : Windows.UI.Color.FromArgb(255, 0, 0, 0);
            var inactiveForeground = isDark ? Windows.UI.Color.FromArgb(160, 255, 255, 255) : Windows.UI.Color.FromArgb(160, 0, 0, 0);
            var hoverBackground = isDark ? Windows.UI.Color.FromArgb(32, 255, 255, 255) : Windows.UI.Color.FromArgb(24, 0, 0, 0);
            var pressedBackground = isDark ? Windows.UI.Color.FromArgb(48, 255, 255, 255) : Windows.UI.Color.FromArgb(36, 0, 0, 0);

            titleBar.ButtonBackgroundColor = Windows.UI.Color.FromArgb(0, 0, 0, 0);
            titleBar.ButtonInactiveBackgroundColor = Windows.UI.Color.FromArgb(0, 0, 0, 0);
            titleBar.ButtonHoverBackgroundColor = hoverBackground;
            titleBar.ButtonPressedBackgroundColor = pressedBackground;
            titleBar.ButtonForegroundColor = foreground;
            titleBar.ButtonInactiveForegroundColor = inactiveForeground;
        }
        catch
        {
            // Ignore title bar button theme failures to avoid affecting startup flow.
        }
    }

    private static void ApplyNativeMenuTheme(ElementTheme theme)
    {
        try
        {
            _ = SetPreferredAppMode(theme == ElementTheme.Dark ? PreferredAppMode.ForceDark : PreferredAppMode.ForceLight);
            FlushMenuThemes();
        }
        catch
        {
            // Native popup menu dark mode APIs are undocumented and may be unavailable on some systems.
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _uiSettings.ColorValuesChanged -= OnColorValuesChanged;
        if (_rootElement is not null)
        {
            _rootElement.ActualThemeChanged -= OnRootActualThemeChanged;
            _rootElement = null;
        }
    }

    private const uint DwmaUseImmersiveDarkMode = 20;
    private const uint DwmaUseImmersiveDarkModeBefore20H1 = 19;

    private enum PreferredAppMode { Default, AllowDark, ForceDark, ForceLight, Max }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, uint dwAttribute, ref int pvAttribute, int cbAttribute);

    [DllImport("uxtheme.dll", EntryPoint = "#135", ExactSpelling = true)]
    private static extern PreferredAppMode SetPreferredAppMode(PreferredAppMode appMode);

    [DllImport("uxtheme.dll", EntryPoint = "#136", ExactSpelling = true)]
    private static extern void FlushMenuThemes();
}
