using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PracticumApi.DataAccess;
using PracticumApi.Exceptions;
using PracticumApi.Interfaces;
using PracticumApi.Models;
using PracticumApi.Services;

namespace PracticumTests;

/// <summary>
/// Интеграционные тесты для BookingService с использованием реальных реализаций
/// </summary>
public class BookingServiceIntegrationTests : IDisposable
{
    /// <summary>
    /// Каждый тест использует отдельную базу EF Core InMemory.
    /// </summary>
    private readonly ServiceProvider _serviceProvider;
    private readonly IServiceScope _scope;

    public BookingServiceIntegrationTests()
    {
        // Имя вычисляется один раз, чтобы разные scope обращались к одной базе.
        var dbName = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(dbName));
        services.AddScoped<IEventService, EventService>();
        services.AddScoped<IBookingService, BookingService>();
        _serviceProvider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });
        _scope = _serviceProvider.CreateScope();
    }

    public void Dispose()
    {
        _scope.Dispose();
        _serviceProvider.Dispose();
    }

    private (IBookingService BookingService, IEventService EventService) CreateServices()
    {
        var eventService = _scope.ServiceProvider.GetRequiredService<IEventService>();
        var bookingService = _scope.ServiceProvider.GetRequiredService<IBookingService>();
        return (bookingService, eventService);
    }

    /// <summary>
    /// Вспомогательный метод создания события, возвращает его Id
    /// </summary>
    private static async Task<Guid> CreateTestEvent(IEventService eventService, int totalSeats = 100)
    {
        var newEvent = Event.Create(
            "Conference 2024",
            "Annual tech conference",
            new DateTime(2024, 6, 15, 09, 00, 00),
            new DateTime(2024, 6, 15, 17, 00, 00),
            totalSeats);
        await eventService.AddAsync(newEvent);
        return newEvent.Id;
    }

    #region Успешные сценарии

    [Fact]
    public async Task DeleteEvent_ShouldPreserveHistoryAndRejectPendingBooking()
    {
        var (bookings, events) = CreateServices();
        var eventId = await CreateTestEvent(events, totalSeats: 3);
        var confirmed = await bookings.CreateBookingAsync(eventId);
        await bookings.ProcessPendingAsync(confirmed.Id);
        var pending = await bookings.CreateBookingAsync(eventId);

        await events.DeleteAsync(eventId);

        using var scope = _serviceProvider.CreateScope();
        var freshEvents = scope.ServiceProvider.GetRequiredService<IEventService>();
        var freshBookings = scope.ServiceProvider.GetRequiredService<IBookingService>();
        await Assert.ThrowsAsync<NotFoundException>(() => freshEvents.GetAsync(eventId));
        Assert.Empty((await freshEvents.GetAllAsync()).Items);
        await Assert.ThrowsAsync<NotFoundException>(() => freshBookings.CreateBookingAsync(eventId));
        Assert.Equal(BookingStatus.Confirmed, (await freshBookings.GetAsync(confirmed.Id)).Status);
        Assert.Equal(BookingStatus.Rejected, await freshBookings.ProcessPendingAsync(pending.Id));
        Assert.Equal(BookingStatus.Rejected, (await freshBookings.GetAsync(pending.Id)).Status);
        Assert.Equal(2, (await freshBookings.GetAllAsync()).Count);
    }

    [Fact]
    public async Task RejectAsync_ShouldSaveStatusAndSeatOnceAndBeIdempotent()
    {
        var (bookings, events) = CreateServices();
        var eventId = await CreateTestEvent(events, totalSeats: 3);
        var booking = await bookings.CreateBookingAsync(eventId);
        await bookings.CreateBookingAsync(eventId);
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<IBookingService>();
        var saves = 0;
        context.SavingChanges += (_, _) => saves++;

        await service.RejectAsync(booking.Id);
        await service.RejectAsync(booking.Id);
        await service.ProcessPendingAsync(booking.Id);

        Assert.Equal(1, saves);
        Assert.Equal(BookingStatus.Rejected, (await bookings.GetAsync(booking.Id)).Status);
        Assert.Equal(2, (await events.GetAsync(eventId)).AvailableSeats);
    }

    /// <summary>
    /// Тест: создание брони для существующего события возвращает статус Pending
    /// </summary>
    [Fact]
    public async Task CreateBookingAsync_ForExistingEvent_ShouldReturnBookingWithPendingStatus()
    {
        // Arrange
        var (bookingService, eventService) = CreateServices();
        var eventId = await CreateTestEvent(eventService);

        // Act
        var result = await bookingService.CreateBookingAsync(eventId);

        // Assert
        Assert.NotNull(result);
        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal(eventId, result.EventId);
        Assert.Equal(BookingStatus.Pending, result.Status);
        Assert.NotEqual(default, result.CreatedAt);
    }

    /// <summary>
    /// Тест: несколько броней для одного события создаются с уникальными Id
    /// </summary>
    [Fact]
    public async Task CreateBookingAsync_MultipleBookingsForSameEvent_ShouldHaveUniqueIds()
    {
        // Arrange
        var (bookingService, eventService) = CreateServices();
        var eventId = await CreateTestEvent(eventService);

        // Act
        var booking1 = await bookingService.CreateBookingAsync(eventId);
        var booking2 = await bookingService.CreateBookingAsync(eventId);
        var booking3 = await bookingService.CreateBookingAsync(eventId);

        // Assert
        Assert.NotNull(booking1);
        Assert.NotNull(booking2);
        Assert.NotNull(booking3);
        Assert.NotEqual(booking1.Id, booking2.Id);
        Assert.NotEqual(booking1.Id, booking3.Id);
        Assert.NotEqual(booking2.Id, booking3.Id);
        Assert.All(new[] { booking1, booking2, booking3 }, b => Assert.Equal(eventId, b.EventId));
        Assert.Equal(3, (await bookingService.GetAllAsync()).Count);
    }

    /// <summary>
    /// Тест: получение брони по Id возвращает корректную информацию
    /// </summary>
    [Fact]
    public async Task Get_WithValidId_ShouldReturnCorrectBooking()
    {
        // Arrange
        var (bookingService, eventService) = CreateServices();
        var eventId = await CreateTestEvent(eventService);
        var created = await bookingService.CreateBookingAsync(eventId);

        // Act
        var result = (await bookingService.GetAsync(created.Id));

        // Assert
        Assert.NotNull(result);
        Assert.Equal(created.Id, result.Id);
        Assert.Equal(eventId, result.EventId);
        Assert.Equal(BookingStatus.Pending, result.Status);
        Assert.Equal(created.CreatedAt, result.CreatedAt);
    }

    /// <summary>
    /// Тест: получение брони отражает изменение статуса после Confirm/Reject
    /// </summary>
    [Fact]
    public async Task Get_ShouldReflectStatusChangeAfterConfirmAndReject()
    {
        // Arrange
        var (bookingService, eventService) = CreateServices();
        var eventId = await CreateTestEvent(eventService);
        var booking = await bookingService.CreateBookingAsync(eventId);

        // Act & Assert - подтверждение брони
        booking.Status = BookingStatus.Confirmed;
        booking.ProcessedAt = DateTime.UtcNow;
        await bookingService.UpdateAsync(booking);

        var confirmed = (await bookingService.GetAsync(booking.Id));
        Assert.Equal(BookingStatus.Confirmed, confirmed.Status);

        // Отмена брони
        booking.Status = BookingStatus.Rejected;
        booking.ProcessedAt = DateTime.UtcNow;
        await bookingService.UpdateAsync(booking);

        var rejected = (await bookingService.GetAsync(booking.Id));
        Assert.Equal(BookingStatus.Rejected, rejected.Status);
    }

    /// <summary>
    /// Тест: создание брони уменьшает AvailableSeats события на 1
    /// </summary>
    [Fact]
    public async Task CreateBookingAsync_ShouldDecrementAvailableSeatsByOne()
    {
        // Arrange
        var (bookingService, eventService) = CreateServices();
        var eventId = await CreateTestEvent(eventService, totalSeats: 5);

        // Act
        await bookingService.CreateBookingAsync(eventId);

        // Assert
        Assert.Equal(4, (await eventService.GetAsync(eventId)).AvailableSeats);
    }

    /// <summary>
    /// Тест: AvailableSeats корректно уменьшается на 1 после каждой успешной брони подряд
    /// (не только в итоге, а на каждом шаге)
    /// </summary>
    [Fact]
    public async Task CreateBookingAsync_ShouldDecrementAvailableSeatsAfterEachBooking()
    {
        // Arrange
        const int totalSeats = 3;
        var (bookingService, eventService) = CreateServices();
        var eventId = await CreateTestEvent(eventService, totalSeats);

        // Act & Assert
        for (var expectedAvailableSeats = totalSeats - 1; expectedAvailableSeats >= 0; expectedAvailableSeats--)
        {
            await bookingService.CreateBookingAsync(eventId);
            Assert.Equal(expectedAvailableSeats, (await eventService.GetAsync(eventId)).AvailableSeats);
        }
    }

    /// <summary>
    /// Тест: создание броней до лимита вместимости — все успешны, у каждой уникальный Id,
    /// свободных мест не остаётся
    /// </summary>
    [Fact]
    public async Task CreateBookingAsync_UpToCapacity_AllSucceedWithUniqueIds()
    {
        // Arrange
        const int totalSeats = 3;
        var (bookingService, eventService) = CreateServices();
        var eventId = await CreateTestEvent(eventService, totalSeats);

        // Act
        var bookings = new List<Booking>();
        for (var i = 0; i < totalSeats; i++)
            bookings.Add(await bookingService.CreateBookingAsync(eventId));

        // Assert
        Assert.Equal(totalSeats, bookings.Select(b => b.Id).Distinct().Count());
        Assert.All(bookings, b => Assert.Equal(BookingStatus.Pending, b.Status));
        Assert.Equal(0, (await eventService.GetAsync(eventId)).AvailableSeats);
    }

    /// <summary>
    /// Тест: Confirm() переводит бронь в статус Confirmed и заполняет ProcessedAt
    /// </summary>
    [Fact]
    public void Confirm_ShouldSetStatusConfirmedAndProcessedAt()
    {
        // Arrange
        var booking = new Booking(Guid.NewGuid()) { Id = Guid.NewGuid(), Status = BookingStatus.Pending, CreatedAt = DateTime.UtcNow };

        // Act
        booking.Confirm();

        // Assert
        Assert.Equal(BookingStatus.Confirmed, booking.Status);
        Assert.NotNull(booking.ProcessedAt);
    }

    /// <summary>
    /// Тест: Reject() переводит бронь в статус Rejected и заполняет ProcessedAt
    /// </summary>
    [Fact]
    public void Reject_ShouldSetStatusRejectedAndProcessedAt()
    {
        // Arrange
        var booking = new Booking(Guid.NewGuid()) { Id = Guid.NewGuid(), Status = BookingStatus.Pending, CreatedAt = DateTime.UtcNow };

        // Act
        booking.Reject();

        // Assert
        Assert.Equal(BookingStatus.Rejected, booking.Status);
        Assert.NotNull(booking.ProcessedAt);
    }

    /// <summary>
    /// Тест: после Reject() и IEventService.ReleaseSeats() количество свободных мест
    /// восстанавливается
    /// </summary>
    [Fact]
    public async Task Reject_ThenReleaseSeats_ShouldRestoreAvailableSeats()
    {
        // Arrange
        var (bookingService, eventService) = CreateServices();
        var eventId = await CreateTestEvent(eventService, totalSeats: 1);
        var booking = await bookingService.CreateBookingAsync(eventId);
        Assert.Equal(0, (await eventService.GetAsync(eventId)).AvailableSeats);

        // Act
        booking.Reject();
        await eventService.ReleaseSeatsAsync(eventId);
        await bookingService.UpdateAsync(booking);

        // Assert
        Assert.Equal(1, (await eventService.GetAsync(eventId)).AvailableSeats);
    }

    /// <summary>
    /// Тест: после Reject() и ReleaseSeats() освободившееся место можно занять новой бронью
    /// </summary>
    [Fact]
    public async Task Reject_ThenReleaseSeats_AllowsNewBookingForSameSeat()
    {
        // Arrange
        var (bookingService, eventService) = CreateServices();
        var eventId = await CreateTestEvent(eventService, totalSeats: 1);
        var rejectedBooking = await bookingService.CreateBookingAsync(eventId);
        rejectedBooking.Reject();
        await eventService.ReleaseSeatsAsync(eventId);
        await bookingService.UpdateAsync(rejectedBooking);

        // Act
        var newBooking = await bookingService.CreateBookingAsync(eventId);

        // Assert
        Assert.NotEqual(rejectedBooking.Id, newBooking.Id);
        Assert.Equal(BookingStatus.Pending, newBooking.Status);
        Assert.Equal(0, (await eventService.GetAsync(eventId)).AvailableSeats);
    }

    #endregion

    #region Конкурентность

    /// <summary>
    /// Тест: при 20 конкурентных запросах на событие с 5 местами успешными должны стать
    /// ровно 5 броней с уникальными Id, остальные 15 — NoAvailableSeatsException,
    /// AvailableSeats = 0
    /// </summary>
    [Fact]
    public async Task CreateBookingAsync_ConcurrentRequestsExceedingCapacity_ShouldPreventOverbooking()
    {
        // Arrange
        const int totalSeats = 5;
        const int concurrentRequests = 20;
        var (bookingService, eventService) = CreateServices();
        var eventId = await CreateTestEvent(eventService, totalSeats);

        // Act
        var results = await Task.WhenAll(
            Enumerable.Range(0, concurrentRequests)
                .Select(_ => TryCreateBookingAsync(eventId)));

        // Assert
        var successfulBookings = results.Where(booking => booking is not null).ToList();
        Assert.Equal(totalSeats, successfulBookings.Count);
        Assert.Equal(totalSeats, successfulBookings.Select(b => b!.Id).Distinct().Count());
        Assert.Equal(concurrentRequests - totalSeats, results.Count(booking => booking is null));
        Assert.Equal(totalSeats, (await bookingService.GetAllAsync()).Count);
        Assert.Equal(0, (await eventService.GetAsync(eventId)).AvailableSeats);
    }

    /// <summary>
    /// Тест: при 10 одновременных запросах на событие с 10 местами все брони создаются
    /// успешно и получают уникальные Id
    /// </summary>
    [Fact]
    public async Task CreateBookingAsync_ConcurrentRequestsWithinCapacity_ShouldProduceUniqueIds()
    {
        // Arrange
        const int totalSeats = 10;
        var (bookingService, eventService) = CreateServices();
        var eventId = await CreateTestEvent(eventService, totalSeats);

        // Act
        var bookings = await Task.WhenAll(
            Enumerable.Range(0, totalSeats)
                .Select(_ => Task.Run(async () =>
                {
                    using var scope = _serviceProvider.CreateScope();
                    var scopedBookingService = scope.ServiceProvider.GetRequiredService<IBookingService>();
                    return await scopedBookingService.CreateBookingAsync(eventId);
                })));

        // Assert
        Assert.Equal(totalSeats, bookings.Length);
        Assert.Equal(totalSeats, bookings.Select(b => b.Id).Distinct().Count());
        Assert.Equal(0, (await eventService.GetAsync(eventId)).AvailableSeats);
    }

    /// <summary>
    /// Запускает CreateBookingAsync на пуле потоков и возвращает созданную бронь при успехе,
    /// null — если брошено NoAvailableSeatsException
    /// </summary>
    private Task<Booking?> TryCreateBookingAsync(Guid eventId) =>
        Task.Run(async () =>
        {
            using var scope = _serviceProvider.CreateScope();
            var bookingService = scope.ServiceProvider.GetRequiredService<IBookingService>();
            try
            {
                return await bookingService.CreateBookingAsync(eventId);
            }
            catch (NoAvailableSeatsException)
            {
                return (Booking?)null;
            }
        });

    #endregion

    #region Неуспешные сценарии

    [Fact]
    public async Task CreateBookingAsync_WithStaleTrackedEvent_ShouldUseCurrentSeatCount()
    {
        // Arrange
        var (bookingService, eventService) = CreateServices();
        var eventId = await CreateTestEvent(eventService, totalSeats: 1);
        using var otherScope = _serviceProvider.CreateScope();
        await otherScope.ServiceProvider.GetRequiredService<IBookingService>().CreateBookingAsync(eventId);

        // Act & Assert
        await Assert.ThrowsAsync<NoAvailableSeatsException>(() => bookingService.CreateBookingAsync(eventId));
        using var verificationScope = _serviceProvider.CreateScope();
        var verification = verificationScope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(0, (await verification.Events.SingleAsync()).AvailableSeats);
        Assert.Equal(1, await verification.Bookings.CountAsync());
    }

    [Fact]
    public async Task CreateBookingAsync_ShouldPersistBookingAndSeatInOneSave()
    {
        // Arrange
        var (_, eventService) = CreateServices();
        var eventId = await CreateTestEvent(eventService, totalSeats: 2);
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var saves = 0;
        context.SavingChanges += (_, _) => saves++;

        // Act
        var booking = await scope.ServiceProvider.GetRequiredService<IBookingService>().CreateBookingAsync(eventId);

        // Assert
        Assert.Equal(1, saves);
        using var verificationScope = _serviceProvider.CreateScope();
        var verification = verificationScope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, (await verification.Events.SingleAsync()).AvailableSeats);
        Assert.Equal(booking.Id, (await verification.Bookings.SingleAsync()).Id);
        var json = System.Text.Json.JsonSerializer.Serialize(booking);
        Assert.Contains(booking.Id.ToString(), json);
    }

    /// <summary>
    /// Тест: создание брони для несуществующего события выбрасывает NotFoundException
    /// </summary>
    [Fact]
    public async Task CreateBookingAsync_ForNonExistentEvent_ShouldThrowNotFoundException()
    {
        // Arrange
        var (bookingService, _) = CreateServices();

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(() =>
            bookingService.CreateBookingAsync(Guid.NewGuid()));
    }

    /// <summary>
    /// Тест: создание брони для удалённого события выбрасывает NotFoundException
    /// </summary>
    [Fact]
    public async Task CreateBookingAsync_ForDeletedEvent_ShouldThrowNotFoundException()
    {
        // Arrange
        var (bookingService, eventService) = CreateServices();
        var eventId = await CreateTestEvent(eventService);
        await eventService.DeleteAsync(eventId);

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(() =>
            bookingService.CreateBookingAsync(eventId));
    }

    /// <summary>
    /// Тест: получение брони по несуществующему Id выбрасывает NotFoundException
    /// </summary>
    [Fact]
    public async Task Get_WithInvalidId_ShouldThrowNotFoundException()
    {
        // Arrange
        var (bookingService, eventService) = CreateServices();
        var eventId = await CreateTestEvent(eventService);
        await bookingService.CreateBookingAsync(eventId);

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(async () => await bookingService.GetAsync(Guid.NewGuid()));
    }

    /// <summary>
    /// Тест: бронирование события без свободных мест выбрасывает NoAvailableSeatsException
    /// </summary>
    [Fact]
    public async Task CreateBookingAsync_WhenNoSeatsRemaining_ShouldThrowNoAvailableSeatsException()
    {
        // Arrange
        var (bookingService, eventService) = CreateServices();
        var eventId = await CreateTestEvent(eventService, totalSeats: 1);
        await bookingService.CreateBookingAsync(eventId);

        // Act & Assert
        await Assert.ThrowsAsync<NoAvailableSeatsException>(() =>
            bookingService.CreateBookingAsync(eventId));
    }

    #endregion
}
