namespace UrlopyJarex.Models;

public class VacationExportFilter
{
    public int Year { get; set; }

    public int? Month { get; set; }

    public VacationRequestStatus? Status { get; set; }
}