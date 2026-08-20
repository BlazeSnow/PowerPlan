using System.Runtime.InteropServices;

namespace PowerPlan.Tray.Services;

internal sealed class TrayMenuTheme
{
    private const uint Windows10Version = 10;
    private const uint Windows10Build1809 = 17763;
    private const uint Windows10Build1903 = 18362;
    private const ushort AllowDarkModeForAppOrdinal = 135;
    private const ushort RefreshImmersiveColorPolicyStateOrdinal = 104;
    private const ushort FlushMenuThemesOrdinal = 136;

    private nint _uxThemeModule;
    private AllowDarkModeForAppDelegate? _allowDarkModeForApp;
    private SetPreferredAppModeDelegate? _setPreferredAppMode;
    private VoidDelegate? _refreshImmersiveColorPolicyState;
    private VoidDelegate? _flushMenuThemes;
    private bool _initialized;
    private bool _supported;
    private bool _policyApplied;

    public void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        try
        {
            var build = GetWindowsBuildNumber();
            if (build < Windows10Build1809)
            {
                return;
            }

            _uxThemeModule = LoadLibraryW("uxtheme.dll");
            if (_uxThemeModule == nint.Zero)
            {
                return;
            }

            if (build < Windows10Build1903)
            {
                _allowDarkModeForApp = GetDelegate<AllowDarkModeForAppDelegate>(AllowDarkModeForAppOrdinal);
                if (_allowDarkModeForApp is null)
                {
                    return;
                }

                _allowDarkModeForApp(true);
                _policyApplied = true;
            }
            else
            {
                _setPreferredAppMode = GetDelegate<SetPreferredAppModeDelegate>(AllowDarkModeForAppOrdinal);
                if (_setPreferredAppMode is null)
                {
                    return;
                }

                _setPreferredAppMode(PreferredAppMode.AllowDark);
                _policyApplied = true;
            }

            _refreshImmersiveColorPolicyState = GetDelegate<VoidDelegate>(RefreshImmersiveColorPolicyStateOrdinal);
            _flushMenuThemes = GetDelegate<VoidDelegate>(FlushMenuThemesOrdinal);
            if (_refreshImmersiveColorPolicyState is null || _flushMenuThemes is null)
            {
                RestoreDefaultAppMode();
                return;
            }

            _supported = true;
            Refresh();
        }
        catch
        {
            _supported = false;
        }
    }

    public void Refresh()
    {
        if (!_supported)
        {
            return;
        }

        try
        {
            _refreshImmersiveColorPolicyState?.Invoke();
            _flushMenuThemes?.Invoke();
        }
        catch
        {
            RestoreDefaultAppMode();
            _supported = false;
        }
    }

    public void Dispose()
    {
        if (_policyApplied)
        {
            RestoreDefaultAppMode();
        }

        if (_uxThemeModule != nint.Zero)
        {
            _ = FreeLibrary(_uxThemeModule);
            _uxThemeModule = nint.Zero;
        }

        _allowDarkModeForApp = null;
        _setPreferredAppMode = null;
        _refreshImmersiveColorPolicyState = null;
        _flushMenuThemes = null;
        _policyApplied = false;
        _supported = false;
        _initialized = false;
    }

    private void RestoreDefaultAppMode()
    {
        try
        {
            if (_setPreferredAppMode is not null)
            {
                _setPreferredAppMode(PreferredAppMode.Default);
            }
            else
            {
                _allowDarkModeForApp?.Invoke(false);
            }
        }
        catch
        {
            // Keep cleanup best-effort when the optional theme API is unavailable.
        }
    }

    private TDelegate? GetDelegate<TDelegate>(ushort ordinal)
        where TDelegate : Delegate
    {
        var function = GetProcAddress(_uxThemeModule, (nint)ordinal);
        return function == nint.Zero
            ? null
            : Marshal.GetDelegateForFunctionPointer<TDelegate>(function);
    }

    private static uint GetWindowsBuildNumber()
    {
        RtlGetNtVersionNumbers(out var majorVersion, out _, out var buildNumber);
        return majorVersion == Windows10Version
            ? buildNumber & 0x0FFFFFFF
            : 0;
    }

    private enum PreferredAppMode
    {
        Default,
        AllowDark
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate bool AllowDarkModeForAppDelegate([MarshalAs(UnmanagedType.Bool)] bool allow);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate PreferredAppMode SetPreferredAppModeDelegate(PreferredAppMode appMode);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void VoidDelegate();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint LoadLibraryW(string fileName);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FreeLibrary(nint module);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GetProcAddress(nint module, nint ordinal);

    [DllImport("ntdll.dll")]
    private static extern void RtlGetNtVersionNumbers(out uint majorVersion, out uint minorVersion, out uint buildNumber);
}
