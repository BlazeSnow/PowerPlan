namespace PowerPlan.Services;

public interface IPowerPlanErrorFormatter
{
    Exception CreateInvalidGuidException(string errorKey);

    Exception CreateEmptyNameException();

    Exception CreateDuplicateMissingGuidException();

    Exception CreateWin32Exception(uint result, string errorKey);
}
