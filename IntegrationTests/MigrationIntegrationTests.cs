using Microsoft.EntityFrameworkCore;
using Infrastructure.DataAccess;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.DataAccess.Repositories;
using Application.Services;

namespace IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class MigrationIntegrationTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task VersionMigration_ShouldPreserveExistingEventsAndBookings()
    {
        await using var database = await fixture.CreateDatabaseAsync(applyMigrations: false);
        await database.Context.GetService<IMigrator>().MigrateAsync("20261013060334_InitialCreate");
        var eventId = Guid.NewGuid();
        var bookingId = Guid.NewGuid();
        var start = DateTime.UtcNow;
        var end = start.AddHours(1);
        await database.Context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO events (id, title, start_at, end_at, total_seats, available_seats, is_deleted)
            VALUES ({eventId}, 'До миграции', {start}, {end}, 2, 1, FALSE)
            """);
        await database.Context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO bookings (id, event_id, status, created_at)
            VALUES ({bookingId}, {eventId}, 'Pending', {start})
            """);

        await database.Context.Database.MigrateAsync();

        await using var context = database.CreateContext();
        var eventItem = await context.Events.SingleAsync();
        var booking = await context.Bookings.SingleAsync();
        Assert.Equal(eventId, eventItem.Id);
        Assert.Equal(bookingId, booking.Id);
        Assert.Equal(Guid.Empty, context.Entry(eventItem).Property<Guid>("Version").CurrentValue);
        Assert.Equal(Guid.Empty, context.Entry(booking).Property<Guid>("Version").CurrentValue);
        var service = new BookingService(new BookingRepository(context), new EventRepository(context), new UnitOfWork(context));
        await service.CreateBookingAsync(eventId);
        Assert.Equal(2, await context.Bookings.CountAsync());
        Assert.Equal(0, (await context.Events.AsNoTracking().SingleAsync()).AvailableSeats);
        Assert.NotEqual(Guid.Empty, context.Entry(eventItem).Property<Guid>("Version").CurrentValue);
    }

    [Fact]
    public async Task Migrate_CreatesTablesAndBookingEventForeignKey()
    {
        // Arrange
        await using var database = await fixture.CreateDatabaseAsync(applyMigrations: false);
        Assert.Empty(await database.Context.Database.GetAppliedMigrationsAsync());
        Assert.True(await QueryBooleanAsync(database.Context, "SELECT to_regclass('public.events') IS NULL"));
        Assert.True(await QueryBooleanAsync(database.Context, "SELECT to_regclass('public.bookings') IS NULL"));

        // Act
        await database.Context.Database.MigrateAsync();

        // Assert: миграция применена и обе таблицы созданы.
        Assert.Equal(new[] { "20261013060334_InitialCreate", "20261029090000_AddConcurrencyVersions" },
            await database.Context.Database.GetAppliedMigrationsAsync());
        Assert.Equal(2, await QueryIntAsync(database.Context, """
            SELECT COUNT(*)::int FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name IN ('events', 'bookings')
              AND column_name = 'version' AND data_type = 'uuid' AND is_nullable = 'NO'
            """));
        Assert.Equal(2, await QueryIntAsync(database.Context, """
            SELECT COUNT(*)::int
            FROM information_schema.tables
            WHERE table_schema = 'public' AND table_name IN ('events', 'bookings')
            """));

        // Assert: первичные ключи созданы, event_id обязателен, FK ссылается на events.id.
        Assert.Equal(2, await QueryIntAsync(database.Context, """
            SELECT COUNT(*)::int
            FROM information_schema.table_constraints
            WHERE table_schema = 'public'
              AND constraint_type = 'PRIMARY KEY'
              AND table_name IN ('events', 'bookings')
            """));
        Assert.Equal(1, await QueryIntAsync(database.Context, """
            SELECT COUNT(*)::int
            FROM information_schema.columns
            WHERE table_schema = 'public'
              AND table_name = 'bookings'
              AND column_name = 'event_id'
              AND is_nullable = 'NO'
            """));
        Assert.Equal(1, await QueryIntAsync(database.Context, """
            SELECT COUNT(*)::int
            FROM information_schema.key_column_usage AS key_column
            JOIN information_schema.constraint_column_usage AS referenced_column
              ON referenced_column.constraint_catalog = key_column.constraint_catalog
             AND referenced_column.constraint_schema = key_column.constraint_schema
             AND referenced_column.constraint_name = key_column.constraint_name
            JOIN information_schema.referential_constraints AS relation
              ON relation.constraint_catalog = key_column.constraint_catalog
             AND relation.constraint_schema = key_column.constraint_schema
             AND relation.constraint_name = key_column.constraint_name
            WHERE key_column.table_schema = 'public'
              AND key_column.table_name = 'bookings'
              AND key_column.column_name = 'event_id'
              AND referenced_column.table_name = 'events'
              AND referenced_column.column_name = 'id'
              AND relation.delete_rule = 'CASCADE'
            """));
    }

    private static async Task<bool> QueryBooleanAsync(AppDbContext context, string sql)
    {
        await context.Database.OpenConnectionAsync();
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        return (bool)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<int> QueryIntAsync(AppDbContext context, string sql)
    {
        await context.Database.OpenConnectionAsync();
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        return (int)(await command.ExecuteScalarAsync())!;
    }
}
