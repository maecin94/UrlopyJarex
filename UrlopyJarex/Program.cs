using UrlopyJarex.Components;
using UrlopyJarex.Data;
using UrlopyJarex.Models;
using UrlopyJarex.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<IDbConnectionFactory, DbConnectionFactory>();
builder.Services.AddScoped<EmployeeService>();
builder.Services.AddScoped<VacationLimitService>();
builder.Services.AddScoped<VacationRequestService>();
builder.Services.AddScoped<HolidayService>();
builder.Services.AddScoped<VacationExportService>();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapGet("/api/eksport-urlopow", async (
    int year,
    int? month,
    int? status,
    VacationExportService exportService) =>
{
    if (year < 2000 || year > 2100)
    {
        return Results.BadRequest("Nieprawidłowy rok.");
    }

    if (month.HasValue && (month.Value < 1 || month.Value > 12))
    {
        return Results.BadRequest("Nieprawidłowy miesiąc.");
    }

    VacationRequestStatus? parsedStatus = null;

    if (status.HasValue && status.Value > 0)
    {
        parsedStatus = (VacationRequestStatus)status.Value;
    }

    var filter = new VacationExportFilter
    {
        Year = year,
        Month = month,
        Status = parsedStatus
    };

    var bytes = await exportService.ExportToExcelAsync(filter);

    var fileName = month.HasValue
        ? $"urlopy_{year}_{month.Value:00}.xlsx"
        : $"urlopy_{year}.xlsx";

    return Results.File(
        bytes,
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        fileName);
});

app.Run();
