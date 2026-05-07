namespace UrlopyJarex.Models;

public enum VacationRequestStatus
{
    PendingSubstituteApproval = 1,
    RejectedBySubstitute = 2,
    PendingCoordinatorApproval = 3,
    RejectedByCoordinator = 4,
    Approved = 5,
    Cancelled = 6
}