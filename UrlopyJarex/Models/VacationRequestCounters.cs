namespace UrlopyJarex.Models;

public class VacationRequestCounters
{
    public int PendingSubstituteApproval { get; set; }

    public int PendingCoordinatorApproval { get; set; }

    public int ApprovedInYear { get; set; }

    public int CancelledInYear { get; set; }
}