namespace UrlopyJarex.Models;

public class VacationConflictInfo
{
    public bool EmployeeHasVacationConflict { get; set; }

    public bool EmployeeHasSubstitutionConflict { get; set; }

    public bool SubstituteHasVacationConflict { get; set; }

    public bool SubstituteHasSubstitutionConflict { get; set; }

    public bool HasAnyConflict =>
        EmployeeHasVacationConflict ||
        EmployeeHasSubstitutionConflict ||
        SubstituteHasVacationConflict ||
        SubstituteHasSubstitutionConflict;
}