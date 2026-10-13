using Microsoft.EntityFrameworkCore;
using PracticumApi.DataAccess.Repositories;
using PracticumApi.Models;

namespace PracticumIntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class BookingRepositoryIntegrationTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task GetAllAsyncAndGetPendingAsync_ReturnExpectedBookings()
    {
        // Arrange
        await using var database = await fixture.CreateDatabaseAsync();
        var repository = new BookingRepository(database.Context);
        var eventId = await AddEventAsync(database.Context);
        var pending = CreateBooking(eventId, BookingStatus.Pending);
        var confirmed = CreateBooking(eventId, BookingStatus.Confirmed);
        var rejected = CreateBooking(eventId, BookingStatus.Rejected);
        await AddBookingsAsync(repository, pending, confirmed, rejected);

        // Act
        var all = await repository.GetAllAsync();
        var pendingBookings = await repository.GetPendingAsync();

        // Assert
        Assert.Equal(3, all.Count);
        Assert.Equal(new[] { pending.Id }, pendingBookings.Select(booking => booking.Id));
    }

    [Fact]
    public async Task GetAsync_ReturnsBookingOrNull()
    {
        // Arrange
        await using var database = await fixture.CreateDatabaseAsync();
        var repository = new BookingRepository(database.Context);
        var eventId = await AddEventAsync(database.Context);
        var booking = CreateBooking(eventId, BookingStatus.Pending);
        await AddBookingsAsync(repository, booking);

        // Act
        var saved = await repository.GetAsync(booking.Id);
        var missing = await repository.GetAsync(Guid.NewGuid());

        // Assert
        Assert.NotNull(saved);
        Assert.Equal(booking.Id, saved.Id);
        Assert.Null(missing);
    }

    [Fact]
    public async Task GetTrackedAsync_AttachesAndRefreshesTrackedBooking()
    {
        // Arrange
        await using var database = await fixture.CreateDatabaseAsync();
        var repository = new BookingRepository(database.Context);
        var eventId = await AddEventAsync(database.Context);
        var booking = CreateBooking(eventId, BookingStatus.Pending);
        await AddBookingsAsync(repository, booking);

        // Act
        var tracked = await repository.GetTrackedAsync(booking.Id);

        // Assert
        Assert.NotNull(tracked);
        Assert.Same(tracked, await repository.GetTrackedAsync(booking.Id));
        Assert.Equal(EntityState.Unchanged, database.Context.Entry(tracked).State);

        await using var otherContext = database.CreateContext();
        var stored = await otherContext.Bookings.SingleAsync(b => b.Id == booking.Id);
        stored.Status = BookingStatus.Confirmed;
        stored.ProcessedAt = DateTime.UtcNow;
        await otherContext.SaveChangesAsync();

        // Act
        var refreshed = await repository.GetTrackedAsync(booking.Id);

        // Assert
        Assert.Same(tracked, refreshed);
        Assert.Equal(BookingStatus.Confirmed, refreshed?.Status);
    }

    [Fact]
    public async Task SaveChangesAsync_PersistsTrackedBookingUpdate()
    {
        // Arrange
        await using var database = await fixture.CreateDatabaseAsync();
        var repository = new BookingRepository(database.Context);
        var eventId = await AddEventAsync(database.Context);
        var booking = CreateBooking(eventId, BookingStatus.Pending);
        await AddBookingsAsync(repository, booking);

        // Act
        var tracked = await repository.GetTrackedAsync(booking.Id);
        Assert.NotNull(tracked);
        tracked.Status = BookingStatus.Confirmed;
        var processedAt = DateTime.UtcNow;
        tracked.ProcessedAt = processedAt;
        await repository.SaveChangesAsync();

        // Assert
        var saved = await repository.GetAsync(booking.Id);
        Assert.Equal(BookingStatus.Confirmed, saved?.Status);
        Assert.Equal(processedAt.AddTicks(-(processedAt.Ticks % 10)), saved?.ProcessedAt);
    }

    [Fact]
    public async Task DeleteAndDetach_PersistDeletionAndDetachEntity()
    {
        // Arrange
        await using var database = await fixture.CreateDatabaseAsync();
        var repository = new BookingRepository(database.Context);
        var eventId = await AddEventAsync(database.Context);
        var booking = CreateBooking(eventId, BookingStatus.Pending);
        await AddBookingsAsync(repository, booking);

        // Act
        var tracked = await repository.GetTrackedAsync(booking.Id);
        Assert.NotNull(tracked);
        repository.Detach(tracked);
        Assert.Equal(EntityState.Detached, database.Context.Entry(tracked).State);

        var toDelete = await repository.GetTrackedAsync(booking.Id);
        Assert.NotNull(toDelete);
        repository.Delete(toDelete);
        await repository.SaveChangesAsync();

        // Assert
        Assert.Null(await repository.GetAsync(booking.Id));
    }

    private static async Task<Guid> AddEventAsync(DbContext context)
    {
        var eventItem = Event.Create(
            "Событие для бронирований", null,
            DateTime.UtcNow, DateTime.UtcNow.AddHours(1), 20);
        eventItem.Id = Guid.NewGuid();
        context.Add(eventItem);
        await context.SaveChangesAsync();
        return eventItem.Id;
    }

    private static async Task AddBookingsAsync(BookingRepository repository, params Booking[] bookings)
    {
        foreach (var booking in bookings)
            await repository.AddAsync(booking);
        await repository.SaveChangesAsync();
    }

    private static Booking CreateBooking(Guid eventId, BookingStatus status) => new(eventId)
    {
        Id = Guid.NewGuid(),
        Status = status,
        CreatedAt = DateTime.UtcNow,
        ProcessedAt = status == BookingStatus.Pending ? null : DateTime.UtcNow
    };
}
