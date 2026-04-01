namespace PowerPlan.Models;

public sealed class PowerPlanInfo
{
    public required string Guid { get; init; }
    public required string Name { get; init; }
    public required bool IsActive { get; init; }
}
