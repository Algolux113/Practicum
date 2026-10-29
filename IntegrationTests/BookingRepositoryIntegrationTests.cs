using Infrastructure.DataAccess;
using Microsoft.EntityFrameworkCore;
using Infrastructure.DataAccess.Repositories;
using Domain.Models;

namespace IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class BookingRepositoryIntegrationTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task Include_ShouldMaterializeReadOnlyNavigationsAndPrivateSetters()
    {
        // Arrange
        await using var database = await fixture.CreateDatabaseAsync();
        var eventId = await AddEventAsync(database.Context);
        var repository = new BookingRepository(database.Context);
        var booking = Booking.Create(eventId);
        await AddBookingsAsync(database.Context, repository, booking);

        // Act
        await using var freshContext = database.CreateContext();
        var savedEvent = await freshContext.Events.Include(e => e.Bookings).SingleAsync(e => e.Id == eventId);
        var savedBooking = Assert.Single(savedEvent.Bookings);

        // Assert
        Assert.Equal(booking.Id, savedBooking.Id);
        Assert.Equal(BookingStatus.Pending, savedBooking.Status);
        Assert.Same(savedEvent, savedBooking.Event);
        Assert.Equal(eventId, savedEvent.Id);
    }

    [Fact]
    public async Task GetPendingAsync_ShouldExcludeProcessedBookings()
    {
        // Arrange
        await using var database = await fixture.CreateDatabaseAsync();
        var repository = new BookingRepository(database.Context);
        var eventId = await AddEventAsync(database.Context);
        var pending = CreateBooking(eventId, BookingStatus.Pending);
        var confirmed = CreateBooking(eventId, BookingStatus.Confirmed);
        var rejected = CreateBooking(eventId, BookingStatus.Rejected);
        await AddBookingsAsync(database.Context, repository, pending, confirmed, rejected);

        // Act
        var all = await database.Context.Bookings.AsNoTracking().ToListAsync();
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
        await AddBookingsAsync(database.Context, repository, booking);

        // Act
        var saved = await repository.GetAsync(booking.Id);
        var missing = await repository.GetAsync(Guid.NewGuid());

        // Assert
        Assert.NotNull(saved);
        Assert.Equal(booking.Id, saved.Id);
        Assert.Null(missing);
    }

    [Fact]
    public async Task GetForUpdateAsync_AttachesAndRefreshesTrackedBooking()
    {
        // Arrange
        await using var database = await fixture.CreateDatabaseAsync();
        var repository = new BookingRepository(database.Context);
        var eventId = await AddEventAsync(database.Context);
        var booking = CreateBooking(eventId, BookingStatus.Pending);
        await AddBookingsAsync(database.Context, repository, booking);

        // Act
        var tracked = await repository.GetForUpdateAsync(booking.Id);

        // Assert
        Assert.NotNull(tracked);
        Assert.Same(tracked, await repository.GetForUpdateAsync(booking.Id));
        Assert.Equal(EntityState.Unchanged, database.Context.Entry(tracked).State);

        await using var otherContext = database.CreateContext();
        var stored = await otherContext.Bookings.SingleAsync(b => b.Id == booking.Id);
        stored.Confirm();
        await otherContext.SaveChangesAsync();

        // Act
        var refreshed = await repository.GetForUpdateAsync(booking.Id);

        // Assert
        Assert.Same(tracked, refreshed);
        Assert.Equal(BookingStatus.Confirmed, refreshed?.Status);
    }

    [Fact]
    public async Task UnitOfWork_PersistsBookingUpdate()
    {
        // Arrange
        await using var database = await fixture.CreateDatabaseAsync();
        var repository = new BookingRepository(database.Context);
        var eventId = await AddEventAsync(database.Context);
        var booking = CreateBooking(eventId, BookingStatus.Pending);
        await AddBookingsAsync(database.Context, repository, booking);

        // Act
        var tracked = await repository.GetForUpdateAsync(booking.Id);
        Assert.NotNull(tracked);
        tracked.Confirm();
        var processedAt = tracked.ProcessedAt!.Value;
        await new UnitOfWork(database.Context).ExecuteAsync(() => Task.FromResult(true));

        // Assert
        var saved = await repository.GetAsync(booking.Id);
        Assert.Equal(BookingStatus.Confirmed, saved?.Status);
        Assert.Equal(processedAt.AddTicks(-(processedAt.Ticks % 10)), saved?.ProcessedAt);
    }

    [Fact]
    public async Task FailedUnitOfWork_ShouldDiscardBookingTransition()
    {
        // Arrange
        await using var database = await fixture.CreateDatabaseAsync();
        var repository = new BookingRepository(database.Context);
        var eventId = await AddEventAsync(database.Context);
        var booking = CreateBooking(eventId, BookingStatus.Pending);
        await AddBookingsAsync(database.Context, repository, booking);

        // Act
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new UnitOfWork(database.Context).ExecuteAsync<bool>(async () =>
            {
                var current = await repository.GetForUpdateAsync(booking.Id);
                current!.Confirm();
                throw new InvalidOperationException("Тестовый сбой");
            }));
        await new UnitOfWork(database.Context).ExecuteAsync(() => Task.FromResult(true));

        // Assert
        Assert.Equal(BookingStatus.Pending, (await repository.GetAsync(booking.Id))!.Status);
    }

    private static async Task<Guid> AddEventAsync(DbContext context)
    {
        var eventItem = Event.Create(
            "Событие для бронирований", null,
            DateTime.UtcNow, DateTime.UtcNow.AddHours(1), 20);
        context.Add(eventItem);
        await context.SaveChangesAsync();
        return eventItem.Id;
    }

    private static async Task AddBookingsAsync(AppDbContext context, BookingRepository repository, params Booking[] bookings)
    {
        await new UnitOfWork(context).ExecuteAsync(async () =>
        {
            foreach (var booking in bookings)
                await repository.AddAsync(booking);
            return true;
        });
    }

    private static Booking CreateBooking(Guid eventId, BookingStatus status)
    {
        var booking = Booking.Create(eventId);
        if (status == BookingStatus.Confirmed)
            booking.Confirm();
        if (status == BookingStatus.Rejected)
            booking.Reject();
        return booking;
    }
}
