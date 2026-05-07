namespace UrlopyJarex.Models;

public class VacationAvailability
{
    public int EmployeeId { get; set; }

    public int Year { get; set; }

    public int EntitlementDays { get; set; }

    public int UsedDays { get; set; }

    public int RemainingDays => EntitlementDays - UsedDays;
}