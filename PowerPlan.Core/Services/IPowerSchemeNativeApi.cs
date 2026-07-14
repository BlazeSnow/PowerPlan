namespace PowerPlan.Services;

public interface IPowerSchemeNativeApi
{
    uint GetActiveScheme(out Guid schemeGuid);

    uint EnumerateScheme(uint index, out Guid schemeGuid);

    uint ReadFriendlyName(Guid schemeGuid, out string name);

    uint SetActiveScheme(Guid schemeGuid);

    PowerSchemeDuplicateResult DuplicateScheme(Guid sourceSchemeGuid);

    uint WriteFriendlyName(Guid schemeGuid, string name);

    uint RestoreDefaultSchemes();
}

public readonly record struct PowerSchemeDuplicateResult(uint Result, Guid? SchemeGuid);
