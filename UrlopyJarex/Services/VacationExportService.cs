using ClosedXML.Excel;
using UrlopyJarex.Models;

namespace UrlopyJarex.Services;

public class VacationExportService
{
    private readonly VacationRequestService _vacationRequestService;

    public VacationExportService(VacationRequestService vacationRequestService)
    {
        _vacationRequestService = vacationRequestService;
    }

    public async Task<byte[]> ExportToExcelAsync(VacationExportFilter filter)
    {
        var requests = (await _vacationRequestService.GetForExportAsync(
            filter.Year,
            filter.Month,
            filter.Status)).ToList();

        using var workbook = new XLWorkbook();

        AddVacationSheet(workbook, requests);
        AddSummarySheet(workbook, requests);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        return stream.ToArray();
    }

    private static void AddVacationSheet(
        XLWorkbook workbook,
        List<VacationRequest> requests)
    {
        var worksheet = workbook.Worksheets.Add("Urlopy");

        var row = 1;

        worksheet.Cell(row, 1).Value = "Id";
        worksheet.Cell(row, 2).Value = "Pracownik";
        worksheet.Cell(row, 3).Value = "Zastępca";
        worksheet.Cell(row, 4).Value = "Data od";
        worksheet.Cell(row, 5).Value = "Data do";
        worksheet.Cell(row, 6).Value = "Rok";
        worksheet.Cell(row, 7).Value = "Dni";
        worksheet.Cell(row, 8).Value = "Typ urlopu";
        worksheet.Cell(row, 9).Value = "Status";
        worksheet.Cell(row, 10).Value = "Komentarz pracownika";
        worksheet.Cell(row, 11).Value = "Komentarz zastępcy";
        worksheet.Cell(row, 12).Value = "Komentarz koordynatora";
        worksheet.Cell(row, 13).Value = "Utworzono";
        worksheet.Cell(row, 14).Value = "Akceptacja zastępcy";
        worksheet.Cell(row, 15).Value = "Akceptacja koordynatora";
        worksheet.Cell(row, 16).Value = "Anulowano";

        worksheet.Range(row, 1, row, 16).Style.Font.Bold = true;
        worksheet.Range(row, 1, row, 16).Style.Fill.BackgroundColor = XLColor.LightGray;

        foreach (var request in requests)
        {
            row++;

            worksheet.Cell(row, 1).Value = request.Id;
            worksheet.Cell(row, 2).Value = request.EmployeeFullName;
            worksheet.Cell(row, 3).Value = request.SubstituteEmployeeFullName;
            worksheet.Cell(row, 4).Value = request.DateFrom;
            worksheet.Cell(row, 5).Value = request.DateTo;
            worksheet.Cell(row, 6).Value = request.Year;
            worksheet.Cell(row, 7).Value = request.DaysCount;
            worksheet.Cell(row, 8).Value = request.VacationType.ToPolishName();
            worksheet.Cell(row, 9).Value = request.Status.ToPolishName();
            worksheet.Cell(row, 10).Value = request.EmployeeComment ?? "";
            worksheet.Cell(row, 11).Value = request.SubstituteComment ?? "";
            worksheet.Cell(row, 12).Value = request.CoordinatorComment ?? "";
            worksheet.Cell(row, 13).Value = request.CreatedAt;

            if (request.SubstituteAcceptedAt.HasValue)
            {
                worksheet.Cell(row, 14).Value = request.SubstituteAcceptedAt.Value;
            }

            if (request.CoordinatorAcceptedAt.HasValue)
            {
                worksheet.Cell(row, 15).Value = request.CoordinatorAcceptedAt.Value;
            }

            if (request.CancelledAt.HasValue)
            {
                worksheet.Cell(row, 16).Value = request.CancelledAt.Value;
            }
        }

        worksheet.Columns().AdjustToContents();

        worksheet.Column(4).Style.DateFormat.Format = "yyyy-mm-dd";
        worksheet.Column(5).Style.DateFormat.Format = "yyyy-mm-dd";
        worksheet.Column(13).Style.DateFormat.Format = "yyyy-mm-dd hh:mm";
        worksheet.Column(14).Style.DateFormat.Format = "yyyy-mm-dd hh:mm";
        worksheet.Column(15).Style.DateFormat.Format = "yyyy-mm-dd hh:mm";
        worksheet.Column(16).Style.DateFormat.Format = "yyyy-mm-dd hh:mm";
    }

    private static void AddSummarySheet(
        XLWorkbook workbook,
        List<VacationRequest> requests)
    {
        var worksheet = workbook.Worksheets.Add("Podsumowanie");

        var summary = requests
            .Where(x => x.Status == VacationRequestStatus.Approved)
            .GroupBy(x => x.EmployeeFullName)
            .Select(x => new
            {
                EmployeeFullName = x.Key,
                DaysCount = x.Sum(y => y.DaysCount),
                RequestsCount = x.Count()
            })
            .OrderBy(x => x.EmployeeFullName)
            .ToList();

        var row = 1;

        worksheet.Cell(row, 1).Value = "Pracownik";
        worksheet.Cell(row, 2).Value = "Liczba zaakceptowanych wniosków";
        worksheet.Cell(row, 3).Value = "Liczba zaakceptowanych dni";

        worksheet.Range(row, 1, row, 3).Style.Font.Bold = true;
        worksheet.Range(row, 1, row, 3).Style.Fill.BackgroundColor = XLColor.LightGray;

        foreach (var item in summary)
        {
            row++;

            worksheet.Cell(row, 1).Value = item.EmployeeFullName;
            worksheet.Cell(row, 2).Value = item.RequestsCount;
            worksheet.Cell(row, 3).Value = item.DaysCount;
        }

        worksheet.Columns().AdjustToContents();
    }
}