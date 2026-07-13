using System.Runtime.InteropServices;
using System.Text;

namespace PowerPlan.Services;

public sealed class WindowsPowerSchemeNativeApi : IPowerSchemeNativeApi
{
    public uint GetActiveScheme(out Guid schemeGuid)
    {
        var result = PowerGetActiveScheme(IntPtr.Zero, out var activeGuidPointer);
        schemeGuid = default;

        try
        {
            if (result == ErrorSuccess)
            {
                schemeGuid = Marshal.PtrToStructure<Guid>(activeGuidPointer);
            }

            return result;
        }
        finally
        {
            if (activeGuidPointer != IntPtr.Zero)
            {
                _ = LocalFree(activeGuidPointer);
            }
        }
    }

    public uint EnumerateScheme(uint index, out Guid schemeGuid)
    {
        var bufferSize = GuidSize;
        var buffer = new byte[GuidSize];
        var result = PowerEnumerate(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, AccessScheme, index, buffer, ref bufferSize);
        schemeGuid = result == ErrorSuccess ? new Guid(buffer) : default;
        return result;
    }

    public uint ReadFriendlyName(Guid schemeGuid, out string name)
    {
        name = string.Empty;
        uint bufferSize = 0;
        var result = PowerReadFriendlyName(IntPtr.Zero, ref schemeGuid, IntPtr.Zero, IntPtr.Zero, null, ref bufferSize);
        if (result is not ErrorSuccess and not ErrorMoreData)
        {
            return result;
        }

        if (bufferSize == 0)
        {
            return ErrorSuccess;
        }

        var buffer = new byte[bufferSize];
        result = PowerReadFriendlyName(IntPtr.Zero, ref schemeGuid, IntPtr.Zero, IntPtr.Zero, buffer, ref bufferSize);
        if (result == ErrorSuccess)
        {
            name = Encoding.Unicode.GetString(buffer).TrimEnd('\0');
        }

        return result;
    }

    public uint SetActiveScheme(Guid schemeGuid)
    {
        return PowerSetActiveScheme(IntPtr.Zero, ref schemeGuid);
    }

    public PowerSchemeDuplicateResult DuplicateScheme(Guid sourceSchemeGuid)
    {
        var result = PowerDuplicateScheme(IntPtr.Zero, ref sourceSchemeGuid, out var destinationGuidPointer);

        try
        {
            return result == ErrorSuccess && destinationGuidPointer != IntPtr.Zero
                ? new PowerSchemeDuplicateResult(result, Marshal.PtrToStructure<Guid>(destinationGuidPointer))
                : new PowerSchemeDuplicateResult(result, null);
        }
        finally
        {
            if (destinationGuidPointer != IntPtr.Zero)
            {
                _ = LocalFree(destinationGuidPointer);
            }
        }
    }

    public uint WriteFriendlyName(Guid schemeGuid, string name)
    {
        var buffer = Encoding.Unicode.GetBytes(name + '\0');
        return PowerWriteFriendlyName(IntPtr.Zero, ref schemeGuid, IntPtr.Zero, IntPtr.Zero, buffer, (uint)buffer.Length);
    }

    public uint RestoreDefaultSchemes()
    {
        return PowerRestoreDefaultPowerSchemes();
    }

    private const uint ErrorSuccess = 0;
    private const uint ErrorMoreData = 234;
    private const uint AccessScheme = 16;
    private const uint GuidSize = 16;

    [DllImport("powrprof.dll")]
    private static extern uint PowerEnumerate(
        IntPtr rootPowerKey,
        IntPtr schemeGuid,
        IntPtr subGroupOfPowerSettingsGuid,
        uint accessFlags,
        uint index,
        [Out] byte[] buffer,
        ref uint bufferSize);

    [DllImport("powrprof.dll")]
    private static extern uint PowerGetActiveScheme(IntPtr rootPowerKey, out IntPtr activePolicyGuid);

    [DllImport("powrprof.dll")]
    private static extern uint PowerSetActiveScheme(IntPtr rootPowerKey, ref Guid schemeGuid);

    [DllImport("powrprof.dll")]
    private static extern uint PowerDuplicateScheme(IntPtr rootPowerKey, ref Guid sourceSchemeGuid, out IntPtr destinationSchemeGuid);

    [DllImport("powrprof.dll")]
    private static extern uint PowerReadFriendlyName(
        IntPtr rootPowerKey,
        ref Guid schemeGuid,
        IntPtr subGroupOfPowerSettingsGuid,
        IntPtr powerSettingGuid,
        [Out] byte[]? buffer,
        ref uint bufferSize);

    [DllImport("powrprof.dll")]
    private static extern uint PowerWriteFriendlyName(
        IntPtr rootPowerKey,
        ref Guid schemeGuid,
        IntPtr subGroupOfPowerSettingsGuid,
        IntPtr powerSettingGuid,
        byte[] buffer,
        uint bufferSize);

    [DllImport("powrprof.dll")]
    private static extern uint PowerRestoreDefaultPowerSchemes();

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr hMem);
}
