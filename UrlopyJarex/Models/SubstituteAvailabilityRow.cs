namespace UrlopyJarex.Models;

public class SubstituteAvailabilityRow
{
    public int EmployeeId { get; set; }

    public string EmployeeFullName { get; set; } = string.Empty;

    public bool IsSelectedEmployee { get; set; }

    public bool HasVacationConflict { get; set; }

    public bool HasSubstitutionConflict { get; set; }

    public bool IsAvailable =>
        !IsSelectedEmployee &&
        !HasVacationConflict &&
        !HasSubstitutionConflict;

    public string StatusText
    {
        get
        {
            if (IsSelectedEmployee)
            {
                return "Wybrany pracownik";
            }

            if (HasVacationConflict)
            {
                return "Ma urlop";
            }

            if (HasSubstitutionConflict)
            {
                return "Już zastępuje";
            }

            return "Dostępny";
        }
    }
}