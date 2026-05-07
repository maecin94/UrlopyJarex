using Dapper;
using UrlopyJarex.Data;
using UrlopyJarex.Models;

namespace UrlopyJarex.Services;

public class VacationLimitService
{
    private readonly IDbConnectionFactory _connectionFactory;

    public VacationLimitService(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IEnumerable<EmployeeVacationLimit>> GetByYearAsync(int year)
    {
        using var connection = _connectionFactory.CreateConnection();

        const string sql = """
            SELECT 
                l.Id,
                l.EmployeeId,
                CONCAT(e.FirstName, ' ', e.LastName) AS EmployeeFullName,
                l.Year,
                l.EntitlementDays,
                l.UsedDays
            FROM EmployeeVacationLimits l
            INNER JOIN Employees e ON e.Id = l.EmployeeId
            WHERE l.Year = @Year
            ORDER BY e.LastName, e.FirstName;
            """;

        return await connection.QueryAsync<EmployeeVacationLimit>(
            sql,
            new { Year = year });
    }

    public async Task<EmployeeVacationLimit?> GetByEmployeeAndYearAsync(int employeeId, int year)
    {
        using var connection = _connectionFactory.CreateConnection();

        const string sql = """
            SELECT 
                l.Id,
                l.EmployeeId,
                CONCAT(e.FirstName, ' ', e.LastName) AS EmployeeFullName,
                l.Year,
                l.EntitlementDays,
                l.UsedDays
            FROM EmployeeVacationLimits l
            INNER JOIN Employees e ON e.Id = l.EmployeeId
            WHERE l.EmployeeId = @EmployeeId
              AND l.Year = @Year;
            """;

        return await connection.QuerySingleOrDefaultAsync<EmployeeVacationLimit>(
            sql,
            new
            {
                EmployeeId = employeeId,
                Year = year
            });
    }

    public async Task<(bool Success, string Message)> UpsertAsync(
        int employeeId,
        int year,
        int entitlementDays)
    {
        if (employeeId <= 0)
        {
            return (false, "Wybierz pracownika.");
        }

        if (year < 2000 || year > 2100)
        {
            return (false, "Podaj poprawny rok.");
        }

        if (entitlementDays < 0)
        {
            return (false, "Wymiar urlopu nie może być ujemny.");
        }

        using var connection = _connectionFactory.CreateConnection();
        connection.Open();

        using var transaction = connection.BeginTransaction();

        try
        {
            var existing = await connection.QuerySingleOrDefaultAsync<EmployeeVacationLimit>(
                """
                SELECT 
                    Id,
                    EmployeeId,
                    Year,
                    EntitlementDays,
                    UsedDays
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

            if (existing is not null && entitlementDays < existing.UsedDays)
            {
                transaction.Rollback();

                return (
                    false,
                    $"Nie można ustawić limitu {entitlementDays}, ponieważ pracownik wykorzystał już {existing.UsedDays} dni."
                );
            }

            if (existing is null)
            {
                await connection.ExecuteAsync(
                    """
                    INSERT INTO EmployeeVacationLimits (
                        EmployeeId,
                        Year,
                        EntitlementDays,
                        UsedDays
                    )
                    VALUES (
                        @EmployeeId,
                        @Year,
                        @EntitlementDays,
                        0
                    );
                    """,
                    new
                    {
                        EmployeeId = employeeId,
                        Year = year,
                        EntitlementDays = entitlementDays
                    },
                    transaction);
            }
            else
            {
                await connection.ExecuteAsync(
                    """
                    UPDATE EmployeeVacationLimits
                    SET EntitlementDays = @EntitlementDays
                    WHERE EmployeeId = @EmployeeId
                      AND Year = @Year;
                    """,
                    new
                    {
                        EmployeeId = employeeId,
                        Year = year,
                        EntitlementDays = entitlementDays
                    },
                    transaction);
            }

            transaction.Commit();

            return (true, "Limit urlopowy został zapisany.");
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task<(bool Success, string Message)> EnsureLimitForEmployeeAsync(
        int employeeId,
        int year,
        int defaultEntitlementDays = 26)
    {
        if (employeeId <= 0)
        {
            return (false, "Nieprawidłowy pracownik.");
        }

        if (year < 2000 || year > 2100)
        {
            return (false, "Nieprawidłowy rok.");
        }

        using var connection = _connectionFactory.CreateConnection();

        const string sql = """
            IF NOT EXISTS (
                SELECT 1
                FROM EmployeeVacationLimits
                WHERE EmployeeId = @EmployeeId
                  AND Year = @Year
            )
            BEGIN
                INSERT INTO EmployeeVacationLimits (
                    EmployeeId,
                    Year,
                    EntitlementDays,
                    UsedDays
                )
                VALUES (
                    @EmployeeId,
                    @Year,
                    @EntitlementDays,
                    0
                );
            END
            """;

        await connection.ExecuteAsync(
            sql,
            new
            {
                EmployeeId = employeeId,
                Year = year,
                EntitlementDays = defaultEntitlementDays
            });

        return (true, "Limit urlopowy został przygotowany.");
    }

    public async Task<VacationAvailability?> GetAvailabilityAsync(int employeeId, int year)
    {
        using var connection = _connectionFactory.CreateConnection();

        const string sql = """
        SELECT
            EmployeeId,
            Year,
            EntitlementDays,
            UsedDays
        FROM EmployeeVacationLimits
        WHERE EmployeeId = @EmployeeId
          AND Year = @Year;
        """;

        return await connection.QuerySingleOrDefaultAsync<VacationAvailability>(
            sql,
            new
            {
                EmployeeId = employeeId,
                Year = year
            });
    }

    public async Task<IEnumerable<EmployeeVacationLimitRow>> GetRowsByYearAsync(int year)
    {
        using var connection = _connectionFactory.CreateConnection();

        const string sql = """
        SELECT 
            l.Id,
            e.Id AS EmployeeId,
            CONCAT(e.FirstName, ' ', e.LastName) AS EmployeeFullName,
            @Year AS Year,
            l.EntitlementDays,
            ISNULL(l.UsedDays, 0) AS UsedDays
        FROM Employees e
        LEFT JOIN EmployeeVacationLimits l
            ON l.EmployeeId = e.Id
           AND l.Year = @Year
        WHERE e.IsActive = 1
        ORDER BY e.LastName, e.FirstName;
        """;

        return await connection.QueryAsync<EmployeeVacationLimitRow>(
            sql,
            new { Year = year });
    }
}