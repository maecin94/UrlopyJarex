namespace UrlopyJarex.Models;

public class EmployeeVacationLimit
{
    public int Id { get; set; }

    public int EmployeeId { get; set; }

    public string EmployeeFullName { get; set; } = string.Empty;

    public int Year { get; set; }

    public int EntitlementDays { get; set; }

    public int UsedDays { get; set; }

    public int RemainingDays => EntitlementDays - UsedDays;
}