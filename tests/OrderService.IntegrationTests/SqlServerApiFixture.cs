using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

namespace OrderService.IntegrationTests;

/// <summary>
/// One real SQL Server container for the whole test run, plus the API hosted in-process
/// against it. This covers what the SQLite unit tests cannot: SQL Server row locking under
/// truly parallel requests, the retrying execution strategy, DATETIME2 round-tripping, and
/// the full HTTP pipeline (binding, exception handler, ProblemDetails).
/// </summary>
public sealed partial class SqlServerApiFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _sql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    private WebApplicationFactory<Program> _factory = null!;
    private string _orderDbConnectionString = null!;

    public HttpClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _sql.StartAsync();

        _orderDbConnectionString = new SqlConnectionStringBuilder(_sql.GetConnectionString())
        {
            InitialCatalog = "OrderDb"
        }.ConnectionString;

        await ResetDatabaseAsync();

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseSetting("ConnectionStrings:OrderDb", _orderDbConnectionString));

        Client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        Client.Dispose();
        await _factory.DisposeAsync();
        await _sql.DisposeAsync();
    }

    /// <summary>Re-runs the committed schema + seed script, the same one the README uses.</summary>
    public async Task ResetDatabaseAsync()
    {
        var script = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "0-schema-and-seed.sql"));

        await using var connection = new SqlConnection(_sql.GetConnectionString());
        await connection.OpenAsync();

        // GO is a sqlcmd batch separator, not T-SQL, so split on it here.
        foreach (var batch in GoSeparator().Split(script).Where(b => !string.IsNullOrWhiteSpace(b)))
        {
            await using var command = new SqlCommand(batch, connection);
            await command.ExecuteNonQueryAsync();
        }
    }

    public async Task ExecuteAsync(string sql)
    {
        await using var connection = new SqlConnection(_orderDbConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<T> ScalarAsync<T>(string sql)
    {
        await using var connection = new SqlConnection(_orderDbConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync())!;
    }

    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex GoSeparator();
}

[CollectionDefinition(Name)]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerApiFixture>
{
    public const string Name = "SQL Server";
}
