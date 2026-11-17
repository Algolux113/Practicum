using Application;
using Application.Interfaces;
using Application.Services;
using Domain.Exceptions;
using Domain.Models;
using Infrastructure;
using Infrastructure.DataAccess;
using Infrastructure.DataAccess.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public class BookingConcurrencyTests(PostgreSqlFixture fixture)
{
    [Theory]
    [InlineData(1, 30)]
    [InlineData(5, 30)]
    [InlineData(30, 30)]
    public async Task IndependentProviders_ShouldPreventOverbookingAndPersistOnlySuccessfulBookings(int capacity, int requests)
    {
        // Два независимых контейнера DI имитируют экземпляры API без общей памяти.
        await using var database = await fixture.CreateDatabaseAsync();
        var eventItem = await AddEventAsync(database, capacity);
        using var first = CreateProvider(database);
        using var second = CreateProvider(database);

        var userIds = await AddUsersAsync(database, requests);

        var results = await Task.WhenAll(Enumerable.Range(0, requests).Select(index => Task.Run(async () =>
        {
            await using var scope = (index % 2 == 0 ? first : second).CreateAsyncScope();
            try
            {
                return await scope.ServiceProvider.GetRequiredService<IBookingService>().CreateBookingAsync(eventItem.Id, userIds[index]);
            }
            catch (NoAvailableSeatsException)
            {
                return (Booking?)null;
            }
        })));

        var successful = results.Where(booking => booking is not null).ToArray();
        Assert.Equal(capacity, successful.Length);
        Assert.Equal(capacity, successful.Select(booking => booking!.Id).Distinct().Count());
        await using var verification = database.CreateContext();
        Assert.Equal(capacity, await verification.Bookings.CountAsync());
        Assert.Equal(0, (await verification.Events.SingleAsync()).AvailableSeats);
        Assert.Equal(successful.Select(booking => booking!.Id).Order(),
            (await verification.Bookings.Select(booking => booking.Id).ToListAsync()).Order());
    }

    [Fact]
    public async Task ConcurrentBookingsForDifferentEvents_ShouldEnforcePerUserLimit()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        var firstEvent = await AddEventAsync(database, 20);
        var secondEvent = await AddEventAsync(database, 20);
        var userId = Assert.Single(await AddUsersAsync(database, 1));
        using var first = CreateProvider(database);
        using var second = CreateProvider(database);

        var attempts = Enumerable.Range(0, 16).Select(async index =>
        {
            await using var scope = (index % 2 == 0 ? first : second).CreateAsyncScope();
            var bookings = scope.ServiceProvider.GetRequiredService<IBookingService>();
            try
            {
                return await bookings.CreateBookingAsync(index % 2 == 0 ? firstEvent.Id : secondEvent.Id, userId);
            }
            catch (ActiveBookingLimitExceededException)
            {
                return null;
            }
        });
        var successful = (await Task.WhenAll(attempts)).OfType<Booking>().ToArray();
        Assert.Equal(10, successful.Length);

        await using var finalScope = first.CreateAsyncScope();
        var service = finalScope.ServiceProvider.GetRequiredService<IBookingService>();
        await service.ProcessPendingAsync(successful[0].Id);
        await Assert.ThrowsAsync<ActiveBookingLimitExceededException>(() => service.CreateBookingAsync(firstEvent.Id, userId));
        await service.CancelAsync(successful[0].Id, userId, UserRole.User);
        await service.CreateBookingAsync(firstEvent.Id, userId);

        var context = finalScope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(10, await new BookingRepository(context).CountActiveAsync(userId));
        Assert.Equal(30, await context.Events.SumAsync(eventItem => eventItem.AvailableSeats));
    }

    [Fact]
    public async Task StaleVersion_ShouldRejectEntireSaveIncludingBookingInsert()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        var eventItem = await AddEventAsync(database, 1);
        var userId = Assert.Single(await AddUsersAsync(database, 1));
        await using var first = database.CreateContext();
        await using var second = database.CreateContext();
        var firstEvent = (await new EventRepository(first).GetForUpdateAsync(eventItem.Id))!;
        var staleEvent = (await new EventRepository(second).GetForUpdateAsync(eventItem.Id))!;
        var version = first.Entry(firstEvent).Property<Guid>("Version").CurrentValue;
        Assert.NotEqual(Guid.Empty, version);
        Assert.Equal(version, second.Entry(staleEvent).Property<Guid>("Version").CurrentValue);
        Assert.True(firstEvent.TryReserveSeats());
        Assert.True(staleEvent.TryReserveSeats());
        first.Bookings.Add(Booking.Create(eventItem.Id, userId));
        second.Bookings.Add(Booking.Create(eventItem.Id, userId));

        await first.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());

        await using var verification = database.CreateContext();
        Assert.Single(await verification.Bookings.ToListAsync());
        var savedEvent = await verification.Events.SingleAsync();
        Assert.Equal(0, savedEvent.AvailableSeats);
        Assert.NotEqual(version, verification.Entry(savedEvent).Property<Guid>("Version").CurrentValue);
    }

    [Fact]
    public async Task RecoveryAfterAnotherWorkerConfirms_ShouldKeepBookingAndReservedSeat()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        var eventItem = await AddEventAsync(database, 1);
        var userId = Assert.Single(await AddUsersAsync(database, 1));
        using var provider = CreateProvider(database);
        await using var processing = provider.CreateAsyncScope();
        var processor = processing.ServiceProvider.GetRequiredService<IBookingService>();
        var booking = await processor.CreateBookingAsync(eventItem.Id, userId);
        Assert.Equal(BookingStatus.Confirmed, await processor.ProcessPendingAsync(booking.Id));

        await using var recovery = provider.CreateAsyncScope();
        var service = recovery.ServiceProvider.GetRequiredService<IBookingService>();
        Assert.Equal(BookingStatus.Confirmed, await service.RejectPendingAsync(booking.Id));

        var context = recovery.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(BookingStatus.Confirmed, (await context.Bookings.AsNoTracking().SingleAsync()).Status);
        Assert.Equal(0, (await context.Events.AsNoTracking().SingleAsync()).AvailableSeats);
    }

    [Fact]
    public async Task ConcurrentRejections_ShouldReturnSeatOnlyOnce()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        var eventItem = await AddEventAsync(database, 3);
        using var first = CreateProvider(database);
        using var second = CreateProvider(database);
        var userId = Assert.Single(await AddUsersAsync(database, 1));
        Guid bookingId;
        await using (var scope = first.CreateAsyncScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IBookingService>();
            bookingId = (await service.CreateBookingAsync(eventItem.Id, userId)).Id;
            await service.CreateBookingAsync(eventItem.Id, userId);
        }

        var statuses = await Task.WhenAll(Enumerable.Range(0, 30).Select(async index =>
        {
            await using var scope = (index % 2 == 0 ? first : second).CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<IBookingService>().RejectAsync(bookingId);
        }));

        Assert.All(statuses, status => Assert.Equal(BookingStatus.Rejected, status));
        await using var verification = database.CreateContext();
        Assert.Equal(2, (await verification.Events.SingleAsync()).AvailableSeats);
        Assert.Equal(1, await verification.Bookings.CountAsync(booking => booking.Status == BookingStatus.Rejected));
    }

    [Fact]
    public async Task WritesToDifferentEvents_ShouldRunWithoutGlobalLock()
    {
        await using var database = await fixture.CreateDatabaseAsync();
        var firstEvent = await AddEventAsync(database, 2);
        var secondEvent = await AddEventAsync(database, 2);
        var bothEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = 0;

        async Task UpdateAsync(Guid id)
        {
            await using var context = database.CreateContext();
            await new UnitOfWork(context).ExecuteAsync(async () =>
            {
                var current = (await new EventRepository(context).GetForUpdateAsync(id))!;
                if (Interlocked.Increment(ref entered) == 2)
                    bothEntered.TrySetResult();
                await bothEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                current.Update("Изменено", current.Description, current.StartAt, current.EndAt, current.TotalSeats);
                return true;
            });
        }

        await Task.WhenAll(UpdateAsync(firstEvent.Id), UpdateAsync(secondEvent.Id));
        await using var verification = database.CreateContext();
        Assert.Equal(2, await verification.Events.CountAsync(eventItem => eventItem.Title == "Изменено"));
    }

    private static ServiceProvider CreateProvider(PostgreSqlTestDatabase database)
    {
        var services = new ServiceCollection();
        services.AddApplication();
        services.AddInfrastructure(database.Context.Database.GetConnectionString());
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private static async Task<Guid[]> AddUsersAsync(PostgreSqlTestDatabase database, int count)
    {
        await using var context = database.CreateContext();
        var users = Enumerable.Range(0, count).Select(index => User.Create($"test-user-{index}", "test-hash")).ToArray();
        context.Users.AddRange(users);
        await context.SaveChangesAsync();
        return users.Select(user => user.Id).ToArray();
    }

    private static async Task<Event> AddEventAsync(PostgreSqlTestDatabase database, int capacity)
    {
        await using var context = database.CreateContext();
        var eventItem = Event.Create("Конкурентность", null, DateTime.UtcNow.AddHours(1), DateTime.UtcNow.AddHours(2), capacity);
        await new UnitOfWork(context).ExecuteAsync(async () =>
        {
            await new EventRepository(context).AddAsync(eventItem);
            return true;
        });
        return eventItem;
    }
}
