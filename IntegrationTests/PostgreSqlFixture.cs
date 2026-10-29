using Microsoft.EntityFrameworkCore;
using Npgsql;
using Infrastructure.DataAccess;
using Testcontainers.PostgreSql;

namespace IntegrationTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PostgreSqlCollection : ICollectionFixture<PostgreSqlFixture>
{
    public const string Name = "PostgreSQL integration tests";
}

public sealed class PostgreSqlFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("postgres")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    public async Task<PostgreSqlTestDatabase> CreateDatabaseAsync(bool applyMigrations = true)
    {
        var databaseName = $"practicum_{Guid.NewGuid():N}";
        var adminConnectionString = new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
        {
            Database = "postgres"
        }.ConnectionString;

        await using (var connection = new NpgsqlConnection(adminConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", connection);
            await command.ExecuteNonQueryAsync();
        }

        var databaseConnectionString = new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
        {
            Database = databaseName
        }.ConnectionString;
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(databaseConnectionString)
            .Options;
        var context = new AppDbContext(options);
        var database = new PostgreSqlTestDatabase(databaseName, adminConnectionString, options, context);

        try
        {
            if (applyMigrations)
                await context.Database.MigrateAsync();

            return database;
        }
        catch
        {
            await database.DisposeAsync();
            throw;
        }
    }
}

public sealed class PostgreSqlTestDatabase(
    string databaseName,
    string adminConnectionString,
    DbContextOptions<AppDbContext> options,
    AppDbContext context) : IAsyncDisposable
{
    public AppDbContext Context { get; } = context;

    public AppDbContext CreateContext() => new(options);

    public async ValueTask DisposeAsync()
    {
        await Context.DisposeAsync();

        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)", connection);
        await command.ExecuteNonQueryAsync();
    }
}
