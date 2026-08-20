using PowerPlan.Models;
using PowerPlan.Services;

namespace PowerPlan.Tests.TestDoubles;

internal sealed class InMemorySettingsStore : ISettingsStore
{
    private readonly Dictionary<string, object> _values = new(StringComparer.Ordinal);

    public bool ThrowOnWrite { get; set; }

    public List<string> BooleanWriteKeys { get; } = [];

    public List<string> StringWriteKeys { get; } = [];

    public bool Contains(string key) => _values.ContainsKey(key);

    public bool GetBoolean(string key, bool defaultValue)
    {
        return _values.TryGetValue(key, out var value) && value is bool boolean
            ? boolean
            : defaultValue;
    }

    public string GetString(string key, string defaultValue)
    {
        return _values.TryGetValue(key, out var value) && value is string text
            ? text
            : defaultValue;
    }

    public void SetBoolean(string key, bool value)
    {
        ThrowIfWriteBlocked();
        BooleanWriteKeys.Add(key);
        _values[key] = value;
    }

    public void SetString(string key, string value)
    {
        ThrowIfWriteBlocked();
        StringWriteKeys.Add(key);
        _values[key] = value;
    }

    private void ThrowIfWriteBlocked()
    {
        if (ThrowOnWrite)
        {
            throw new InvalidOperationException("Settings writes are unavailable.");
        }
    }
}

internal sealed class FakeLegacySettingsStore : ILegacySettingsStore
{
    public AppSettings? PrimarySettings { get; set; }

    public AppSettings? FallbackSettings { get; set; }

    public int PrimaryMigratedCount { get; private set; }

    public int FallbackMigratedCount { get; private set; }

    public Task<AppSettings?> LoadPrimaryAsync() => Task.FromResult(PrimarySettings);

    public Task<AppSettings?> LoadFallbackAsync() => Task.FromResult(FallbackSettings);

    public void MarkPrimaryMigrated() => PrimaryMigratedCount++;

    public void MarkFallbackMigrated() => FallbackMigratedCount++;
}

internal sealed class FakeLanguagePreferenceProvider : ILanguagePreferenceProvider
{
    public string? PreferredLanguage { get; set; }

    public bool ThrowOnRead { get; set; }

    public string? GetPreferredLanguage()
    {
        if (ThrowOnRead)
        {
            throw new InvalidOperationException("Language preference is unavailable.");
        }

        return PreferredLanguage;
    }
}

internal sealed class FakePowerSchemeNativeApi : IPowerSchemeNativeApi
{
    public Guid ActiveScheme { get; set; }

    public uint GetActiveSchemeResult { get; set; }

    public uint EnumerateResult { get; set; }

    public uint ReadFriendlyNameResult { get; set; }

    public uint SetActiveSchemeResult { get; set; }

    public uint WriteFriendlyNameResult { get; set; }

    public uint RestoreDefaultSchemesResult { get; set; }

    public PowerSchemeDuplicateResult DuplicateSchemeResult { get; set; } = new(0, Guid.NewGuid());

    public List<Guid> Schemes { get; } = [];

    public Dictionary<Guid, string> FriendlyNames { get; } = [];

    public TaskCompletionSource? FirstReadStarted { get; set; }

    public TaskCompletionSource? FirstReadRelease { get; set; }

    private int _readGateEntered;

    public int GetActiveSchemeCallCount { get; private set; }

    public int EnumerateSchemeCallCount { get; private set; }

    public int ReadFriendlyNameCallCount { get; private set; }

    public Guid? SetActiveSchemeArgument { get; private set; }

    public Guid? DuplicateSchemeArgument { get; private set; }

    public (Guid Guid, string Name)? WriteFriendlyNameArgument { get; private set; }

    public int RestoreDefaultSchemesCallCount { get; private set; }

    public uint GetActiveScheme(out Guid schemeGuid)
    {
        GetActiveSchemeCallCount++;
        if (Interlocked.Exchange(ref _readGateEntered, 1) == 0 && FirstReadStarted is not null && FirstReadRelease is not null)
        {
            FirstReadStarted.TrySetResult();
            FirstReadRelease.Task.GetAwaiter().GetResult();
        }

        schemeGuid = ActiveScheme;
        return GetActiveSchemeResult;
    }

    public uint EnumerateScheme(uint index, out Guid schemeGuid)
    {
        EnumerateSchemeCallCount++;
        if (EnumerateResult != 0)
        {
            schemeGuid = Guid.Empty;
            return EnumerateResult;
        }

        if (index >= Schemes.Count)
        {
            schemeGuid = Guid.Empty;
            return 259;
        }

        schemeGuid = Schemes[(int)index];
        return 0;
    }

    public uint ReadFriendlyName(Guid schemeGuid, out string name)
    {
        ReadFriendlyNameCallCount++;
        name = FriendlyNames.GetValueOrDefault(schemeGuid, string.Empty);
        return ReadFriendlyNameResult;
    }

    public uint SetActiveScheme(Guid schemeGuid)
    {
        SetActiveSchemeArgument = schemeGuid;
        return SetActiveSchemeResult;
    }

    public PowerSchemeDuplicateResult DuplicateScheme(Guid sourceSchemeGuid)
    {
        DuplicateSchemeArgument = sourceSchemeGuid;
        return DuplicateSchemeResult;
    }

    public uint WriteFriendlyName(Guid schemeGuid, string name)
    {
        WriteFriendlyNameArgument = (schemeGuid, name);
        return WriteFriendlyNameResult;
    }

    public uint RestoreDefaultSchemes()
    {
        RestoreDefaultSchemesCallCount++;
        return RestoreDefaultSchemesResult;
    }
}

internal sealed class FakePowerPlanErrorFormatter : IPowerPlanErrorFormatter
{
    public List<(uint Result, string ErrorKey)> Win32Errors { get; } = [];

    public List<string> InvalidGuidErrorKeys { get; } = [];

    public Exception CreateInvalidGuidException(string errorKey)
    {
        InvalidGuidErrorKeys.Add(errorKey);
        return new InvalidOperationException(errorKey);
    }

    public Exception CreateEmptyNameException() => new InvalidOperationException("EmptyName");

    public Exception CreateDuplicateMissingGuidException() => new InvalidOperationException("DuplicateMissingGuid");

    public Exception CreateWin32Exception(uint result, string errorKey)
    {
        Win32Errors.Add((result, errorKey));
        return new InvalidOperationException($"{errorKey}:{result}");
    }
}
