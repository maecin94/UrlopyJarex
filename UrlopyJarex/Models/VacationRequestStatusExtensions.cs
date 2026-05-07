namespace UrlopyJarex.Models;

public static class VacationRequestStatusExtensions
{
    public static string ToPolishName(this VacationRequestStatus status)
    {
        return status switch
        {
            VacationRequestStatus.PendingSubstituteApproval => "Oczekuje na akceptację zastępcy",
            VacationRequestStatus.RejectedBySubstitute => "Odrzucony przez zastępcę",
            VacationRequestStatus.PendingCoordinatorApproval => "Oczekuje na akceptację koordynatora",
            VacationRequestStatus.RejectedByCoordinator => "Odrzucony przez koordynatora",
            VacationRequestStatus.Approved => "Zaakceptowany",
            VacationRequestStatus.Cancelled => "Anulowany",
            _ => "Nieznany"
        };
    }

    public static string ToBadgeCssClass(this VacationRequestStatus status)
    {
        return status switch
        {
            VacationRequestStatus.PendingSubstituteApproval => "badge bg-warning text-dark",
            VacationRequestStatus.PendingCoordinatorApproval => "badge bg-warning text-dark",
            VacationRequestStatus.Approved => "badge bg-success",
            VacationRequestStatus.Cancelled => "badge bg-secondary",
            VacationRequestStatus.RejectedBySubstitute => "badge bg-danger",
            VacationRequestStatus.RejectedByCoordinator => "badge bg-danger",
            _ => "badge bg-secondary"
        };
    }

    public static string ToShortPolishName(this VacationRequestStatus status)
    {
        return status switch
        {
            VacationRequestStatus.PendingSubstituteApproval => "Do zastępcy",
            VacationRequestStatus.RejectedBySubstitute => "Odrzucony",
            VacationRequestStatus.PendingCoordinatorApproval => "Do koordynatora",
            VacationRequestStatus.RejectedByCoordinator => "Odrzucony",
            VacationRequestStatus.Approved => "Zaakceptowany",
            VacationRequestStatus.Cancelled => "Anulowany",
            _ => "Nieznany"
        };
    }
}