using Dapper;
using UrlopyJarex.Data;
using UrlopyJarex.Models;

namespace UrlopyJarex.Services;

public class EmployeeService
{
    private readonly IDbConnectionFactory _connectionFactory;

    public EmployeeService(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IEnumerable<Employee>> GetAllAsync()
    {
        using var connection = _connectionFactory.CreateConnection();

        const string sql = """
            SELECT 
                Id,
                FirstName,
                LastName,
                Email,
                IsCoordinator,
                IsActive,
                CreatedAt
            FROM Employees
            ORDER BY IsActive DESC, LastName, FirstName;
            """;

        return await connection.QueryAsync<Employee>(sql);
    }

    public async Task<IEnumerable<Employee>> GetActiveAsync()
    {
        using var connection = _connectionFactory.CreateConnection();

        const string sql = """
            SELECT 
                Id,
                FirstName,
                LastName,
                Email,
                IsCoordinator,
                IsActive,
                CreatedAt
            FROM Employees
            WHERE IsActive = 1
            ORDER BY LastName, FirstName;
            """;

        return await connection.QueryAsync<Employee>(sql);
    }

    public async Task<Employee?> GetByIdAsync(int id)
    {
        using var connection = _connectionFactory.CreateConnection();

        const string sql = """
            SELECT 
                Id,
                FirstName,
                LastName,
                Email,
                IsCoordinator,
                IsActive,
                CreatedAt
            FROM Employees
            WHERE Id = @Id;
            """;

        return await connection.QuerySingleOrDefaultAsync<Employee>(sql, new { Id = id });
    }

    public async Task<int> CreateAsync(Employee employee)
    {
        using var connection = _connectionFactory.CreateConnection();
        connection.Open();

        using var transaction = connection.BeginTransaction();

        try
        {
            if (employee.IsCoordinator)
            {
                await connection.ExecuteAsync(
                    """
                    UPDATE Employees
                    SET IsCoordinator = 0;
                    """,
                    transaction: transaction);
            }

            const string sql = """
                INSERT INTO Employees (
                    FirstName,
                    LastName,
                    Email,
                    IsCoordinator,
                    IsActive
                )
                OUTPUT INSERTED.Id
                VALUES (
                    @FirstName,
                    @LastName,
                    @Email,
                    @IsCoordinator,
                    @IsActive
                );
                """;

            var id = await connection.ExecuteScalarAsync<int>(
                sql,
                employee,
                transaction);

            transaction.Commit();

            return id;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task UpdateAsync(Employee employee)
    {
        using var connection = _connectionFactory.CreateConnection();
        connection.Open();

        using var transaction = connection.BeginTransaction();

        try
        {
            if (employee.IsCoordinator)
            {
                await connection.ExecuteAsync(
                    """
                    UPDATE Employees
                    SET IsCoordinator = 0
                    WHERE Id <> @Id;
                    """,
                    new { employee.Id },
                    transaction);
            }

            const string sql = """
                UPDATE Employees
                SET FirstName = @FirstName,
                    LastName = @LastName,
                    Email = @Email,
                    IsCoordinator = @IsCoordinator,
                    IsActive = @IsActive
                WHERE Id = @Id;
                """;

            await connection.ExecuteAsync(sql, employee, transaction);

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task SetCoordinatorAsync(int employeeId)
    {
        using var connection = _connectionFactory.CreateConnection();
        connection.Open();

        using var transaction = connection.BeginTransaction();

        try
        {
            await connection.ExecuteAsync(
                """
                UPDATE Employees
                SET IsCoordinator = 0;
                """,
                transaction: transaction);

            await connection.ExecuteAsync(
                """
                UPDATE Employees
                SET IsCoordinator = 1
                WHERE Id = @EmployeeId
                  AND IsActive = 1;
                """,
                new { EmployeeId = employeeId },
                transaction);

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task<bool> HasCoordinatorAsync()
    {
        using var connection = _connectionFactory.CreateConnection();

        const string sql = """
            SELECT COUNT(1)
            FROM Employees
            WHERE IsCoordinator = 1
              AND IsActive = 1;
            """;

        var count = await connection.ExecuteScalarAsync<int>(sql);

        return count > 0;
    }
}