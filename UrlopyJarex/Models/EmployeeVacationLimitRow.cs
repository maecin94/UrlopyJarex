namespace UrlopyJarex.Models;

public class EmployeeVacationLimitRow
{
    public int? Id { get; set; }

    public int EmployeeId { get; set; }

    public string EmployeeFullName { get; set; } = string.Empty;

    public int Year { get; set; }

    public int? EntitlementDays { get; set; }

    public int UsedDays { get; set; }

    public bool HasLimit => EntitlementDays.HasValue;

    public int? RemainingDays =>
        EntitlementDays.HasValue
            ? EntitlementDays.Value - UsedDays
            : null;
}