using Dapper;
using System.Data;
using UrlopyJarex.Data;
using UrlopyJarex.Models;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace UrlopyJarex.Services;

public class VacationRequestService
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly HolidayService _holidayService;

    public VacationRequestService(
        IDbConnectionFactory connectionFactory,
        HolidayService holidayService)
    {
        _connectionFactory = connectionFactory;
        _holidayService = holidayService;
    }

    private static readonly int[] ActiveStatuses =
    {
        (int)VacationRequestStatus.PendingSubstituteApproval,
        (int)VacationRequestStatus.PendingCoordinatorApproval,
        (int)VacationRequestStatus.Approved
    };

    public async Task<(bool Success, string Message)> CreateAsync(
        int employeeId,
        int substituteEmployeeId,
        DateTime dateFrom,
        DateTime dateTo,
        VacationType vacationType,
        string? employeeComment)
    {
        dateFrom = dateFrom.Date;
        dateTo = dateTo.Date;

        if (employeeId <= 0)
        {
            return (false, "Wybierz pracownika.");
        }

        if (substituteEmployeeId <= 0)
        {
            return (false, "Wybierz osobę zastępującą.");
        }

        if (employeeId == substituteEmployeeId)
        {
            return (false, "Pracownik nie może być własnym zastępcą.");
        }

        if (dateTo < dateFrom)
        {
            return (false, "Data zakończenia nie może być wcześniejsza niż data rozpoczęcia.");
        }

        if (dateFrom.Year != dateTo.Year)
        {
            return (false, "Urlop nie może przechodzić przez granicę roku. Złóż dwa osobne wnioski.");
        }

        var daysCount = await CountWorkingDaysWithHolidaysAsync(dateFrom, dateTo);

        if (daysCount <= 0)
        {
            return (false, "W wybranym zakresie nie ma dni roboczych.");
        }

        using var connection = _connectionFactory.CreateConnection();
        connection.Open();

        using var transaction = connection.BeginTransaction();

        try
        {
            var year = dateFrom.Year;

            var employeeExists = await EmployeeExistsAsync(connection, transaction, employeeId);

            if (!employeeExists)
            {
                transaction.Rollback();
                return (false, "Wybrany pracownik nie istnieje albo jest nieaktywny.");
            }

            var substituteExists = await EmployeeExistsAsync(connection, transaction, substituteEmployeeId);

            if (!substituteExists)
            {
                transaction.Rollback();
                return (false, "Wybrana osoba zastępująca nie istnieje albo jest nieaktywna.");
            }

            var remainingDays = await connection.ExecuteScalarAsync<int?>(
                """
                SELECT EntitlementDays - UsedDays
                FROM EmployeeVacationLimits
                WHERE EmployeeId = @EmployeeId
                  AND Year = @Year;
                """,
                new
                {
                    EmployeeId = employeeId,
                    Year = year
                },
                transaction);

            if (remainingDays is null)
            {
                transaction.Rollback();
                return (false, "Brak ustawionego limitu urlopowego dla pracownika w wybranym roku.");
            }

            if (remainingDays.Value < daysCount)
            {
                transaction.Rollback();
                return (
                    false,
                    $"Brak wystarczającej liczby dni urlopu. Pozostało: {remainingDays.Value}, wymagane: {daysCount}."
                );
            }

            var employeeHasConflict = await HasEmployeeVacationConflictAsync(
                connection,
                transaction,
                employeeId,
                dateFrom,
                dateTo,
                ignoredRequestId: null);

            if (employeeHasConflict)
            {
                transaction.Rollback();
                return (false, "Pracownik ma już aktywny wniosek urlopowy w tym terminie.");
            }

            var employeeAlreadySubstitutes = await SubstituteHasConflictAsync(
                connection,
                transaction,
                employeeId,
                dateFrom,
                dateTo,
                ignoredRequestId: null);

            if (employeeAlreadySubstitutes)
            {
                transaction.Rollback();
                return (false, "Pracownik nie może iść na urlop, ponieważ w tym terminie zastępuje już inną osobę.");
            }

            var substituteHasVacation = await HasEmployeeVacationConflictAsync(
                connection,
                transaction,
                substituteEmployeeId,
                dateFrom,
                dateTo,
                ignoredRequestId: null);

            if (substituteHasVacation)
            {
                transaction.Rollback();
                return (false, "Osoba zastępująca ma urlop w tym terminie.");
            }

            var substituteAlreadySubstitutes = await SubstituteHasConflictAsync(
                connection,
                transaction,
                substituteEmployeeId,
                dateFrom,
                dateTo,
                ignoredRequestId: null);

            if (substituteAlreadySubstitutes)
            {
                transaction.Rollback();
                return (false, "Osoba zastępująca zastępuje już inną osobę w tym terminie.");
            }

            await connection.ExecuteAsync(
                """
                INSERT INTO VacationRequests (
                    EmployeeId,
                    SubstituteEmployeeId,
                    DateFrom,
                    DateTo,
                    Year,
                    VacationType,
                    DaysCount,
                    Status,
                    EmployeeComment
                )
                VALUES (
                    @EmployeeId,
                    @SubstituteEmployeeId,
                    @DateFrom,
                    @DateTo,
                    @Year,
                    @VacationType,
                    @DaysCount,
                    @Status,
                    @EmployeeComment
                );
                """,
                new
                {
                    EmployeeId = employeeId,
                    SubstituteEmployeeId = substituteEmployeeId,
                    DateFrom = dateFrom,
                    DateTo = dateTo,
                    Year = year,
                    VacationType = (int)vacationType,
                    DaysCount = daysCount,
                    Status = (int)VacationRequestStatus.PendingSubstituteApproval,
                    EmployeeComment = employeeComment
                },
                transaction);

            transaction.Commit();

            return (true, "Wniosek urlopowy został utworzony.");
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task<IEnumerable<VacationRequest>> GetPendingSubstituteApprovalAsync()
    {
        using var connection = _connectionFactory.CreateConnection();

        const string sql = """
            SELECT 
                vr.Id,
                vr.EmployeeId,
                CONCAT(e.FirstName, ' ', e.LastName) AS EmployeeFullName,
                vr.SubstituteEmployeeId,
                CONCAT(s.FirstName, ' ', s.LastName) AS SubstituteEmployeeFullName,
                vr.DateFrom,
                vr.DateTo,
                vr.Year,
                vr.VacationType,
                vr.DaysCount,
                vr.Status,
                vr.EmployeeComment,
                vr.SubstituteComment,
                vr.CoordinatorComment,
                vr.CreatedAt,
                vr.CancelledAt,
                vr.SubstituteAcceptedAt,
                vr.CoordinatorAcceptedAt
            FROM VacationRequests vr
            INNER JOIN Employees e ON e.Id = vr.EmployeeId
            INNER JOIN Employees s ON s.Id = vr.SubstituteEmployeeId
            WHERE vr.Status = @Status
            ORDER BY vr.CreatedAt;
            """;

        return await connection.QueryAsync<VacationRequest>(
            sql,
            new
            {
                Status = (int)VacationRequestStatus.PendingSubstituteApproval
            });
    }

    public async Task<IEnumerable<VacationRequest>> GetPendingCoordinatorApprovalAsync()
    {
        using var connection = _connectionFactory.CreateConnection();

        const string sql = """
            SELECT 
                vr.Id,
                vr.EmployeeId,
                CONCAT(e.FirstName, ' ', e.LastName) AS EmployeeFullName,
                vr.SubstituteEmployeeId,
                CONCAT(s.FirstName, ' ', s.LastName) AS SubstituteEmployeeFullName,
                vr.DateFrom,
                vr.DateTo,
                vr.Year,
                vr.VacationType,
                vr.DaysCount,
                vr.Status,
                vr.EmployeeComment,
                vr.SubstituteComment,
                vr.CoordinatorComment,
                vr.CreatedAt,
                vr.CancelledAt,
                vr.SubstituteAcceptedAt,
                vr.CoordinatorAcceptedAt
            FROM VacationRequests vr
            INNER JOIN Employees e ON e.Id = vr.EmployeeId
            INNER JOIN Employees s ON s.Id = vr.SubstituteEmployeeId
            WHERE vr.Status = @Status
            ORDER BY vr.CreatedAt;
            """;

        return await connection.QueryAsync<VacationRequest>(
            sql,
            new
            {
                Status = (int)VacationRequestStatus.PendingCoordinatorApproval
            });
    }

    public async Task<IEnumerable<VacationRequest>> GetApprovedForPeriodAsync(
        DateTime dateFrom,
        DateTime dateTo)
    {
        using var connection = _connectionFactory.CreateConnection();

        dateFrom = dateFrom.Date;
        dateTo = dateTo.Date;

        const string sql = """
            SELECT 
                vr.Id,
                vr.EmployeeId,
                CONCAT(e.FirstName, ' ', e.LastName) AS EmployeeFullName,
                vr.SubstituteEmployeeId,
                CONCAT(s.FirstName, ' ', s.LastName) AS SubstituteEmployeeFullName,
                vr.DateFrom,
                vr.DateTo,
                vr.Year,
                vr.VacationType,
                vr.DaysCount,
                vr.Status,
                vr.EmployeeComment,
                vr.SubstituteComment,
                vr.CoordinatorComment,
                vr.CreatedAt,
                vr.CancelledAt,
                vr.SubstituteAcceptedAt,
                vr.CoordinatorAcceptedAt
            FROM VacationRequests vr
            INNER JOIN Employees e ON e.Id = vr.EmployeeId
            INNER JOIN Employees s ON s.Id = vr.SubstituteEmployeeId
            WHERE vr.Status = @Status
              AND vr.DateFrom <= @DateTo
              AND vr.DateTo >= @DateFrom
            ORDER BY vr.DateFrom, e.LastName, e.FirstName;
            """;

        return await connection.QueryAsync<VacationRequest>(
            sql,
            new
            {
                Status = (int)VacationRequestStatus.Approved,
                DateFrom = dateFrom,
                DateTo = dateTo
            });
    }

    public async Task<IEnumerable<VacationRequest>> GetApprovedAsync()
    {
        using var connection = _connectionFactory.CreateConnection();

        const string sql = """
        SELECT 
            vr.Id,
            vr.EmployeeId,
            CONCAT(e.FirstName, ' ', e.LastName) AS EmployeeFullName,
            vr.SubstituteEmployeeId,
            CONCAT(s.FirstName, ' ', s.LastName) AS SubstituteEmployeeFullName,
            vr.DateFrom,
            vr.DateTo,
            vr.Year,
            vr.VacationType,
            vr.DaysCount,
            vr.Status,
            vr.EmployeeComment,
            vr.SubstituteComment,
            vr.CoordinatorComment,
            vr.CreatedAt,
            vr.CancelledAt,
            vr.SubstituteAcceptedAt,
            vr.CoordinatorAcceptedAt
        FROM VacationRequests vr
        INNER JOIN Employees e ON e.Id = vr.EmployeeId
        INNER JOIN Employees s ON s.Id = vr.SubstituteEmployeeId
        WHERE vr.Status = @Status
        ORDER BY vr.DateFrom DESC, e.LastName, e.FirstName;
        """;

        return await connection.QueryAsync<VacationRequest>(
            sql,
            new
            {
                Status = (int)VacationRequestStatus.Approved
            });
    }

    public async Task<IEnumerable<VacationRequest>> GetAllAsync()
    {
        using var connection = _connectionFactory.CreateConnection();

        const string sql = """
        SELECT 
            vr.Id,
            vr.EmployeeId,
            CONCAT(e.FirstName, ' ', e.LastName) AS EmployeeFullName,
            vr.SubstituteEmployeeId,
            CONCAT(s.FirstName, ' ', s.LastName) AS SubstituteEmployeeFullName,
            vr.DateFrom,
            vr.DateTo,
            vr.Year,
            vr.VacationType,
            vr.DaysCount,
            vr.Status,
            vr.EmployeeComment,
            vr.SubstituteComment,
            vr.CoordinatorComment,
            vr.CreatedAt,
            vr.CancelledAt,
            vr.SubstituteAcceptedAt,
            vr.CoordinatorAcceptedAt
        FROM VacationRequests vr
        INNER JOIN Employees e ON e.Id = vr.EmployeeId
        INNER JOIN Employees s ON s.Id = vr.SubstituteEmployeeId
        ORDER BY vr.CreatedAt DESC;
        """;

        return await connection.QueryAsync<VacationRequest>(sql);
    }

    public async Task<VacationConflictInfo> CheckConflictsAsync(
    int employeeId,
    int substituteEmployeeId,
    DateTime dateFrom,
    DateTime dateTo)
    {
        dateFrom = dateFrom.Date;
        dateTo = dateTo.Date;

        var result = new VacationConflictInfo();

        if (employeeId <= 0 || substituteEmployeeId <= 0 || dateTo < dateFrom)
        {
            return result;
        }

        using var connection = _connectionFactory.CreateConnection();
        connection.Open();

        using var transaction = connection.BeginTransaction();

        try
        {
            result.EmployeeHasVacationConflict = await HasEmployeeVacationConflictAsync(
                connection,
                transaction,
                employeeId,
                dateFrom,
                dateTo,
                ignoredRequestId: null);

            result.EmployeeHasSubstitutionConflict = await SubstituteHasConflictAsync(
                connection,
                transaction,
                employeeId,
                dateFrom,
                dateTo,
                ignoredRequestId: null);

            result.SubstituteHasVacationConflict = await HasEmployeeVacationConflictAsync(
                connection,
                transaction,
                substituteEmployeeId,
                dateFrom,
                dateTo,
                ignoredRequestId: null);

            result.SubstituteHasSubstitutionConflict = await SubstituteHasConflictAsync(
                connection,
                transaction,
                substituteEmployeeId,
                dateFrom,
                dateTo,
                ignoredRequestId: null);

            transaction.Commit();

            return result;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task<IEnumerable<SubstituteAvailabilityRow>> GetSubstituteAvailabilityAsync(
    int employeeId,
    DateTime dateFrom,
    DateTime dateTo)
    {
        dateFrom = dateFrom.Date;
        dateTo = dateTo.Date;

        if (dateTo < dateFrom)
        {
            return Enumerable.Empty<SubstituteAvailabilityRow>();
        }

        using var connection = _connectionFactory.CreateConnection();

        const string sql = """
        SELECT
            e.Id AS EmployeeId,
            CONCAT(e.FirstName, ' ', e.LastName) AS EmployeeFullName,

            CASE 
                WHEN e.Id = @EmployeeId THEN CAST(1 AS bit)
                ELSE CAST(0 AS bit)
            END AS IsSelectedEmployee,

            CASE
                WHEN EXISTS (
                    SELECT 1
                    FROM VacationRequests vr
                    WHERE vr.EmployeeId = e.Id
                      AND vr.Status IN @ActiveStatuses
                      AND vr.DateFrom <= @DateTo
                      AND vr.DateTo >= @DateFrom
                )
                THEN CAST(1 AS bit)
                ELSE CAST(0 AS bit)
            END AS HasVacationConflict,

            CASE
                WHEN EXISTS (
                    SELECT 1
                    FROM VacationRequests vr
                    WHERE vr.SubstituteEmployeeId = e.Id
                      AND vr.Status IN @ActiveStatuses
                      AND vr.DateFrom <= @DateTo
                      AND vr.DateTo >= @DateFrom
                )
                THEN CAST(1 AS bit)
                ELSE CAST(0 AS bit)
            END AS HasSubstitutionConflict

        FROM Employees e
        WHERE e.IsActive = 1
        ORDER BY e.LastName, e.FirstName;
        """;

        return await connection.QueryAsync<SubstituteAvailabilityRow>(
            sql,
            new
            {
                EmployeeId = employeeId,
                DateFrom = dateFrom,
                DateTo = dateTo,
                ActiveStatuses
            });
    }

    public async Task<IEnumerable<VacationRequest>> GetApprovedAbsencesAsync(
    DateTime dateFrom,
    DateTime dateTo)
    {
        using var connection = _connectionFactory.CreateConnection();

        dateFrom = dateFrom.Date;
        dateTo = dateTo.Date;

        const string sql = """
        SELECT 
            vr.Id,
            vr.EmployeeId,
            CONCAT(e.FirstName, ' ', e.LastName) AS EmployeeFullName,
            vr.SubstituteEmployeeId,
            CONCAT(s.FirstName, ' ', s.LastName) AS SubstituteEmployeeFullName,
            vr.DateFrom,
            vr.DateTo,
            vr.Year,
            vr.VacationType,
            vr.DaysCount,
            vr.Status,
            vr.EmployeeComment,
            vr.SubstituteComment,
            vr.CoordinatorComment,
            vr.CreatedAt,
            vr.CancelledAt,
            vr.SubstituteAcceptedAt,
            vr.CoordinatorAcceptedAt
        FROM VacationRequests vr
        INNER JOIN Employees e ON e.Id = vr.EmployeeId
        INNER JOIN Employees s ON s.Id = vr.SubstituteEmployeeId
        WHERE vr.Status = @Status
          AND vr.DateFrom <= @DateTo
          AND vr.DateTo >= @DateFrom
        ORDER BY vr.DateFrom, e.LastName, e.FirstName;
        """;

        return await connection.QueryAsync<VacationRequest>(
            sql,
            new
            {
                Status = (int)VacationRequestStatus.Approved,
                DateFrom = dateFrom,
                DateTo = dateTo
            });
    }

    public async Task<VacationRequestCounters> GetCountersAsync(int year)
    {
        using var connection = _connectionFactory.CreateConnection();

        const string sql = """
        SELECT
            SUM(CASE WHEN Status = @PendingSubstituteApproval THEN 1 ELSE 0 END) AS PendingSubstituteApproval,
            SUM(CASE WHEN Status = @PendingCoordinatorApproval THEN 1 ELSE 0 END) AS PendingCoordinatorApproval,
            SUM(CASE WHEN Status = @Approved AND Year = @Year THEN 1 ELSE 0 END) AS ApprovedInYear,
            SUM(CASE WHEN Status = @Cancelled AND Year = @Year THEN 1 ELSE 0 END) AS CancelledInYear
        FROM VacationRequests;
        """;

        var result = await connection.QuerySingleOrDefaultAsync<VacationRequestCounters>(
            sql,
            new
            {
                Year = year,
                PendingSubstituteApproval = (int)VacationRequestStatus.PendingSubstituteApproval,
                PendingCoordinatorApproval = (int)VacationRequestStatus.PendingCoordinatorApproval,
                Approved = (int)VacationRequestStatus.Approved,
                Cancelled = (int)VacationRequestStatus.Cancelled
            });

        return result ?? new VacationRequestCounters();
    }

    public async Task<(bool Success, string Message)> AcceptBySubstituteAsync(
        int requestId,
        string? comment)
    {
        using var connection = _connectionFactory.CreateConnection();

        var affectedRows = await connection.ExecuteAsync(
            """
            UPDATE VacationRequests
            SET Status = @NewStatus,
                SubstituteComment = @Comment,
                SubstituteAcceptedAt = SYSUTCDATETIME()
            WHERE Id = @RequestId
              AND Status = @CurrentStatus;
            """,
            new
            {
                RequestId = requestId,
                Comment = comment,
                CurrentStatus = (int)VacationRequestStatus.PendingSubstituteApproval,
                NewStatus = (int)VacationRequestStatus.PendingCoordinatorApproval
            });

        return affectedRows == 1
            ? (true, "Zastępstwo zostało zaakceptowane.")
            : (false, "Nie można zaakceptować tego wniosku. Wniosek ma nieprawidłowy status.");
    }

    public async Task<(bool Success, string Message)> RejectBySubstituteAsync(
        int requestId,
        string? comment)
    {
        using var connection = _connectionFactory.CreateConnection();

        var affectedRows = await connection.ExecuteAsync(
            """
            UPDATE VacationRequests
            SET Status = @NewStatus,
                SubstituteComment = @Comment
            WHERE Id = @RequestId
              AND Status = @CurrentStatus;
            """,
            new
            {
                RequestId = requestId,
                Comment = comment,
                CurrentStatus = (int)VacationRequestStatus.PendingSubstituteApproval,
                NewStatus = (int)VacationRequestStatus.RejectedBySubstitute
            });

        return affectedRows == 1
            ? (true, "Wniosek został odrzucony przez zastępcę.")
            : (false, "Nie można odrzucić tego wniosku. Wniosek ma nieprawidłowy status.");
    }

    public async Task<(bool Success, string Message)> AcceptByCoordinatorAsync(
        int requestId,
        string? comment)
    {
        using var connection = _connectionFactory.CreateConnection();
        connection.Open();

        using var transaction = connection.BeginTransaction();

        try
        {
            var request = await GetRequestForUpdateAsync(connection, transaction, requestId);

            if (request is null)
            {
                transaction.Rollback();
                return (false, "Nie znaleziono wniosku.");
            }

            if (request.Status != VacationRequestStatus.PendingCoordinatorApproval)
            {
                transaction.Rollback();
                return (false, "Wniosek nie oczekuje na akceptację koordynatora.");
            }

            var recalculatedDaysCount = await CountWorkingDaysWithHolidaysAsync(
    request.DateFrom,
    request.DateTo);

            if (recalculatedDaysCount <= 0)
            {
                transaction.Rollback();
                return (false, "Po uwzględnieniu świąt wniosek nie obejmuje żadnego dnia roboczego.");
            }

            if (recalculatedDaysCount != request.DaysCount)
            {
                request.DaysCount = recalculatedDaysCount;

                await connection.ExecuteAsync(
                    """
        UPDATE VacationRequests
        SET DaysCount = @DaysCount
        WHERE Id = @RequestId;
        """,
                    new
                    {
                        RequestId = request.Id,
                        DaysCount = request.DaysCount
                    },
                    transaction);
            }

            var remainingDays = await connection.ExecuteScalarAsync<int?>(
                """
                SELECT EntitlementDays - UsedDays
                FROM EmployeeVacationLimits
                WHERE EmployeeId = @EmployeeId
                  AND Year = @Year;
                """,
                new
                {
                    EmployeeId = request.EmployeeId,
                    Year = request.Year
                },
                transaction);

            if (remainingDays is null)
            {
                transaction.Rollback();
                return (false, "Brak ustawionego limitu urlopowego dla pracownika w roku wniosku.");
            }

            if (remainingDays.Value < request.DaysCount)
            {
                transaction.Rollback();
                return (false, "Pracownik nie ma już wystarczającej liczby dni urlopu.");
            }

            var employeeHasConflict = await HasEmployeeVacationConflictAsync(
                connection,
                transaction,
                request.EmployeeId,
                request.DateFrom,
                request.DateTo,
                ignoredRequestId: request.Id);

            if (employeeHasConflict)
            {
                transaction.Rollback();
                return (false, "Pracownik ma konflikt z innym aktywnym wnioskiem urlopowym.");
            }

            var employeeAlreadySubstitutes = await SubstituteHasConflictAsync(
                connection,
                transaction,
                request.EmployeeId,
                request.DateFrom,
                request.DateTo,
                ignoredRequestId: request.Id);

            if (employeeAlreadySubstitutes)
            {
                transaction.Rollback();
                return (false, "Pracownik nie może iść na urlop, ponieważ w tym terminie zastępuje już inną osobę.");
            }

            var substituteHasVacation = await HasEmployeeVacationConflictAsync(
                connection,
                transaction,
                request.SubstituteEmployeeId,
                request.DateFrom,
                request.DateTo,
                ignoredRequestId: request.Id);

            if (substituteHasVacation)
            {
                transaction.Rollback();
                return (false, "Osoba zastępująca ma urlop w tym terminie.");
            }

            var substituteAlreadySubstitutes = await SubstituteHasConflictAsync(
                connection,
                transaction,
                request.SubstituteEmployeeId,
                request.DateFrom,
                request.DateTo,
                ignoredRequestId: request.Id);

            if (substituteAlreadySubstitutes)
            {
                transaction.Rollback();
                return (false, "Osoba zastępująca zastępuje już inną osobę w tym terminie.");
            }

            await connection.ExecuteAsync(
                """
                UPDATE VacationRequests
                SET Status = @Status,
                    CoordinatorComment = @Comment,
                    CoordinatorAcceptedAt = SYSUTCDATETIME()
                WHERE Id = @RequestId;
                """,
                new
                {
                    RequestId = requestId,
                    Status = (int)VacationRequestStatus.Approved,
                    Comment = comment
                },
                transaction);

            await connection.ExecuteAsync(
                """
                UPDATE EmployeeVacationLimits
                SET UsedDays = UsedDays + @DaysCount
                WHERE EmployeeId = @EmployeeId
                  AND Year = @Year;
                """,
                new
                {
                    EmployeeId = request.EmployeeId,
                    Year = request.Year,
                    DaysCount = request.DaysCount
                },
                transaction);

            transaction.Commit();

            return (true, "Wniosek został zaakceptowany przez koordynatora.");
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task<IEnumerable<VacationRequest>> GetForExportAsync(
    int year,
    int? month,
    VacationRequestStatus? status)
    {
        using var connection = _connectionFactory.CreateConnection();

        DateTime dateFrom;
        DateTime dateTo;

        if (month.HasValue && month.Value >= 1 && month.Value <= 12)
        {
            dateFrom = new DateTime(year, month.Value, 1);
            dateTo = dateFrom.AddMonths(1).AddDays(-1);
        }
        else
        {
            dateFrom = new DateTime(year, 1, 1);
            dateTo = new DateTime(year, 12, 31);
        }

        const string sql = """
        SELECT 
            vr.Id,
            vr.EmployeeId,
            CONCAT(e.FirstName, ' ', e.LastName) AS EmployeeFullName,
            vr.SubstituteEmployeeId,
            CONCAT(s.FirstName, ' ', s.LastName) AS SubstituteEmployeeFullName,
            vr.DateFrom,
            vr.DateTo,
            vr.Year,
            vr.VacationType,
            vr.DaysCount,
            vr.Status,
            vr.EmployeeComment,
            vr.SubstituteComment,
            vr.CoordinatorComment,
            vr.CreatedAt,
            vr.CancelledAt,
            vr.SubstituteAcceptedAt,
            vr.CoordinatorAcceptedAt
        FROM VacationRequests vr
        INNER JOIN Employees e ON e.Id = vr.EmployeeId
        INNER JOIN Employees s ON s.Id = vr.SubstituteEmployeeId
        WHERE vr.DateFrom <= @DateTo
          AND vr.DateTo >= @DateFrom
          AND (@Status IS NULL OR vr.Status = @Status)
        ORDER BY e.LastName, e.FirstName, vr.DateFrom;
        """;

        return await connection.QueryAsync<VacationRequest>(
            sql,
            new
            {
                DateFrom = dateFrom,
                DateTo = dateTo,
                Status = status.HasValue ? (int?)status.Value : null
            });
    }

    public async Task<(bool Success, string Message)> RejectByCoordinatorAsync(
        int requestId,
        string? comment)
    {
        using var connection = _connectionFactory.CreateConnection();

        var affectedRows = await connection.ExecuteAsync(
            """
            UPDATE VacationRequests
            SET Status = @NewStatus,
                CoordinatorComment = @Comment
            WHERE Id = @RequestId
              AND Status = @CurrentStatus;
            """,
            new
            {
                RequestId = requestId,
                Comment = comment,
                CurrentStatus = (int)VacationRequestStatus.PendingCoordinatorApproval,
                NewStatus = (int)VacationRequestStatus.RejectedByCoordinator
            });

        return affectedRows == 1
            ? (true, "Wniosek został odrzucony przez koordynatora.")
            : (false, "Nie można odrzucić tego wniosku. Wniosek ma nieprawidłowy status.");
    }

    public async Task<(bool Success, string Message)> CancelApprovedByCoordinatorAsync(
        int requestId,
        string? comment)
    {
        using var connection = _connectionFactory.CreateConnection();
        connection.Open();

        using var transaction = connection.BeginTransaction();

        try
        {
            var request = await GetRequestForUpdateAsync(connection, transaction, requestId);

            if (request is null)
            {
                transaction.Rollback();
                return (false, "Nie znaleziono wniosku.");
            }

            if (request.Status != VacationRequestStatus.Approved)
            {
                transaction.Rollback();
                return (false, "Anulować w tym trybie można tylko zaakceptowany urlop.");
            }

            await connection.ExecuteAsync(
                """
                UPDATE VacationRequests
                SET Status = @Status,
                    CancelledAt = SYSUTCDATETIME(),
                    CoordinatorComment = @Comment
                WHERE Id = @RequestId;
                """,
                new
                {
                    RequestId = requestId,
                    Status = (int)VacationRequestStatus.Cancelled,
                    Comment = comment
                },
                transaction);

            await connection.ExecuteAsync(
                """
                UPDATE EmployeeVacationLimits
                SET UsedDays = UsedDays - @DaysCount
                WHERE EmployeeId = @EmployeeId
                  AND Year = @Year;
                """,
                new
                {
                    EmployeeId = request.EmployeeId,
                    Year = request.Year,
                    DaysCount = request.DaysCount
                },
                transaction);

            transaction.Commit();

            return (true, "Zaakceptowany urlop został anulowany, a dni wróciły do puli.");
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public static int CountWorkingDays(DateTime from, DateTime to)
    {
        from = from.Date;
        to = to.Date;

        var count = 0;

        for (var date = from; date <= to; date = date.AddDays(1))
        {
            if (date.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday)
            {
                count++;
            }
        }

        return count;
    }

    public async Task<int> CountWorkingDaysWithHolidaysAsync(DateTime from, DateTime to)
    {
        from = from.Date;
        to = to.Date;

        if (to < from)
        {
            return 0;
        }

        var holidayDates = (await _holidayService.GetAllActiveHolidayDatesForPeriodAsync(from, to))
            .Select(x => x.Date)
            .ToHashSet();

        var count = 0;

        for (var date = from; date <= to; date = date.AddDays(1))
        {
            if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                continue;
            }

            if (holidayDates.Contains(date))
            {
                continue;
            }

            count++;
        }

        return count;
    }

    private static async Task<bool> EmployeeExistsAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        int employeeId)
    {
        var count = await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(1)
            FROM Employees
            WHERE Id = @EmployeeId
              AND IsActive = 1;
            """,
            new { EmployeeId = employeeId },
            transaction);

        return count == 1;
    }

    private static async Task<VacationRequest?> GetRequestForUpdateAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        int requestId)
    {
        const string sql = """
            SELECT 
                Id,
                EmployeeId,
                SubstituteEmployeeId,
                DateFrom,
                DateTo,
                Year,
                VacationType,
                DaysCount,
                Status
            FROM VacationRequests
            WHERE Id = @RequestId;
            """;

        return await connection.QuerySingleOrDefaultAsync<VacationRequest>(
            sql,
            new { RequestId = requestId },
            transaction);
    }

    private static async Task<bool> HasEmployeeVacationConflictAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        int employeeId,
        DateTime dateFrom,
        DateTime dateTo,
        int? ignoredRequestId)
    {
        var count = await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(1)
            FROM VacationRequests
            WHERE EmployeeId = @EmployeeId
              AND Status IN @ActiveStatuses
              AND DateFrom <= @DateTo
              AND DateTo >= @DateFrom
              AND (@IgnoredRequestId IS NULL OR Id <> @IgnoredRequestId);
            """,
            new
            {
                EmployeeId = employeeId,
                ActiveStatuses,
                DateFrom = dateFrom.Date,
                DateTo = dateTo.Date,
                IgnoredRequestId = ignoredRequestId
            },
            transaction);

        return count > 0;
    }

    private static async Task<bool> SubstituteHasConflictAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        int substituteEmployeeId,
        DateTime dateFrom,
        DateTime dateTo,
        int? ignoredRequestId)
    {
        var count = await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(1)
            FROM VacationRequests
            WHERE SubstituteEmployeeId = @SubstituteEmployeeId
              AND Status IN @ActiveStatuses
              AND DateFrom <= @DateTo
              AND DateTo >= @DateFrom
              AND (@IgnoredRequestId IS NULL OR Id <> @IgnoredRequestId);
            """,
            new
            {
                SubstituteEmployeeId = substituteEmployeeId,
                ActiveStatuses,
                DateFrom = dateFrom.Date,
                DateTo = dateTo.Date,
                IgnoredRequestId = ignoredRequestId
            },
            transaction);

        return count > 0;
    }
}