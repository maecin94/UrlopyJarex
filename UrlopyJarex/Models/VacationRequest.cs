namespace UrlopyJarex.Models;

public class VacationRequest
{
    public int Id { get; set; }

    public int EmployeeId { get; set; }

    public string EmployeeFullName { get; set; } = string.Empty;

    public int SubstituteEmployeeId { get; set; }

    public string SubstituteEmployeeFullName { get; set; } = string.Empty;

    public DateTime DateFrom { get; set; }

    public DateTime DateTo { get; set; }

    public int Year { get; set; }

    public VacationType VacationType { get; set; }

    public int DaysCount { get; set; }

    public VacationRequestStatus Status { get; set; }

    public string? EmployeeComment { get; set; }

    public string? SubstituteComment { get; set; }

    public string? CoordinatorComment { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? CancelledAt { get; set; }

    public DateTime? SubstituteAcceptedAt { get; set; }

    public DateTime? CoordinatorAcceptedAt { get; set; }
}