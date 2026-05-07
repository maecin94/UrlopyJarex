using Dapper;
using UrlopyJarex.Data;
using UrlopyJarex.Models;

namespace UrlopyJarex.Services;

public class HolidayService
{
    private readonly IDbConnectionFactory _connectionFactory;

    public HolidayService(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IEnumerable<Holiday>> GetByYearAsync(int year)
    {
        using var connection = _connectionFactory.CreateConnection();

        const string sql = """
            SELECT
                Id,
                HolidayDate,
                Name,
                IsActive,
                CreatedAt
            FROM Holidays
            WHERE YEAR(HolidayDate) = @Year
            ORDER BY HolidayDate;
            """;

        return await connection.QueryAsync<Holiday>(
            sql,
            new { Year = year });
    }

    public async Task<IEnumerable<DateTime>> GetActiveDatesForPeriodAsync(
        DateTime dateFrom,
        DateTime dateTo)
    {
        using var connection = _connectionFactory.CreateConnection();

        dateFrom = dateFrom.Date;
        dateTo = dateTo.Date;

        const string sql = """
            SELECT HolidayDate
            FROM Holidays
            WHERE IsActive = 1
              AND HolidayDate >= @DateFrom
              AND HolidayDate <= @DateTo;
            """;

        return await connection.QueryAsync<DateTime>(
            sql,
            new
            {
                DateFrom = dateFrom,
                DateTo = dateTo
            });
    }

    public async Task<(bool Success, string Message)> CreateAsync(
        DateTime holidayDate,
        string name)
    {
        holidayDate = holidayDate.Date;

        if (string.IsNullOrWhiteSpace(name))
        {
            return (false, "Nazwa święta jest wymagana.");
        }

        using var connection = _connectionFactory.CreateConnection();

        try
        {
            const string sql = """
                INSERT INTO Holidays (
                    HolidayDate,
                    Name,
                    IsActive
                )
                VALUES (
                    @HolidayDate,
                    @Name,
                    1
                );
                """;

            await connection.ExecuteAsync(
                sql,
                new
                {
                    HolidayDate = holidayDate,
                    Name = name.Trim()
                });

            return (true, "Święto zostało dodane.");
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number is 2601 or 2627)
        {
            return (false, "Święto dla tej daty już istnieje.");
        }
    }

    public async Task<(bool Success, string Message)> UpdateAsync(Holiday holiday)
    {
        holiday.HolidayDate = holiday.HolidayDate.Date;

        if (holiday.Id <= 0)
        {
            return (false, "Nieprawidłowe święto.");
        }

        if (string.IsNullOrWhiteSpace(holiday.Name))
        {
            return (false, "Nazwa święta jest wymagana.");
        }

        using var connection = _connectionFactory.CreateConnection();

        try
        {
            const string sql = """
                UPDATE Holidays
                SET HolidayDate = @HolidayDate,
                    Name = @Name,
                    IsActive = @IsActive
                WHERE Id = @Id;
                """;

            var affectedRows = await connection.ExecuteAsync(sql, holiday);

            return affectedRows == 1
                ? (true, "Święto zostało zaktualizowane.")
                : (false, "Nie znaleziono święta.");
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number is 2601 or 2627)
        {
            return (false, "Święto dla tej daty już istnieje.");
        }
    }
    public async Task<IEnumerable<HolidayDefinition>> GetDefinitionsAsync()
    {
        using var connection = _connectionFactory.CreateConnection();

        const string sql = """
        SELECT
            Id,
            MonthNumber,
            DayNumber,
            Name,
            IsActive,
            CreatedAt
        FROM HolidayDefinitions
        ORDER BY MonthNumber, DayNumber;
        """;

        return await connection.QueryAsync<HolidayDefinition>(sql);
    }

    public async Task<IEnumerable<DateTime>> GetFixedHolidayDatesForPeriodAsync(
        DateTime dateFrom,
        DateTime dateTo)
    {
        using var connection = _connectionFactory.CreateConnection();

        dateFrom = dateFrom.Date;
        dateTo = dateTo.Date;

        var years = Enumerable
            .Range(dateFrom.Year, dateTo.Year - dateFrom.Year + 1)
            .ToArray();

        const string sql = """
        SELECT
            DATEFROMPARTS(y.Value, hd.MonthNumber, hd.DayNumber) AS HolidayDate
        FROM HolidayDefinitions hd
        CROSS APPLY (
            SELECT Value
            FROM STRING_SPLIT(@Years, ',')
        ) y
        WHERE hd.IsActive = 1
          AND DATEFROMPARTS(y.Value, hd.MonthNumber, hd.DayNumber) >= @DateFrom
          AND DATEFROMPARTS(y.Value, hd.MonthNumber, hd.DayNumber) <= @DateTo;
        """;

        return await connection.QueryAsync<DateTime>(
            sql,
            new
            {
                DateFrom = dateFrom,
                DateTo = dateTo,
                Years = string.Join(",", years)
            });
    }

    public async Task<IEnumerable<DateTime>> GetAllActiveHolidayDatesForPeriodAsync(
        DateTime dateFrom,
        DateTime dateTo)
    {
        var fixedDates = await GetFixedHolidayDatesForPeriodAsync(dateFrom, dateTo);
        var manualDates = await GetActiveDatesForPeriodAsync(dateFrom, dateTo);

        return fixedDates
            .Concat(manualDates)
            .Select(x => x.Date)
            .Distinct()
            .OrderBy(x => x);
    }
}