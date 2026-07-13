using System.ComponentModel;

namespace PowerPlan.Services;

public sealed class LocalizedPowerPlanErrorFormatter : IPowerPlanErrorFormatter
{
    public Exception CreateInvalidGuidException(string errorKey)
    {
        return new InvalidOperationException(LocalizationService.Get(errorKey));
    }

    public Exception CreateEmptyNameException()
    {
        return new InvalidOperationException(LocalizationService.Get("PowerPlan.Error.EmptyName"));
    }

    public Exception CreateDuplicateMissingGuidException()
    {
        return new InvalidOperationException(LocalizationService.Get("PowerPlan.Error.DuplicateMissingGuid"));
    }

    public Exception CreateWin32Exception(uint result, string errorKey)
    {
        return new Win32Exception(
            (int)result,
            LocalizationService.Format("PowerPlan.Error.Win32", LocalizationService.Get(errorKey), new Win32Exception((int)result).Message));
    }
}
