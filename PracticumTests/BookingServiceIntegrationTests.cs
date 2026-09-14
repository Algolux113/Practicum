using PracticumApi.Exceptions;
using PracticumApi.Interfaces;
using PracticumApi.Models;
using PracticumApi.Services;

namespace PracticumTests;

/// <summary>
/// Интеграционные тесты для BookingService с использованием реальных реализаций
/// </summary>
public class BookingServiceIntegrationTests
{
    /// <summary>
    /// Создаёт пару сервисов: BookingService с реальным EventService
    /// </summary>
    private (IBookingService BookingService, IEventService EventService) CreateServices()
    {
        var eventService = new EventService();
        var bookingService = new BookingService(eventService);
        return (bookingService, eventService);
    }

    /// <summary>
    /// Вспомогательный метод создания события, возвращает его Id
    /// </summary>
    private static Guid CreateTestEvent(IEventService eventService, int totalSeats = 100)
    {
        var newEvent = Event.Create(
            "Conference 2024",
            "Annual tech conference",
            new DateTime(2024, 6, 15, 09, 00, 00),
            new DateTime(2024, 6, 15, 17, 00, 00),
            totalSeats);
        eventService.Add(newEvent);
        return newEvent.Id;
    }

    #region Успешные сценарии

    /// <summary>
    /// Тест: создание брони для существующего события возвращает статус Pending
    /// </summary>
    [Fact]
    public async Task CreateBookingAsync_ForExistingEvent_ShouldReturnBookingWithPendingStatus()
    {
        // Arrange
        var (bookingService, eventService) = CreateServices();
        var eventId = CreateTestEvent(eventService);

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
        var eventId = CreateTestEvent(eventService);

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
        Assert.Equal(3, bookingService.GetAll().Count);
    }

    /// <summary>
    /// Тест: получение брони по Id возвращает корректную информацию
    /// </summary>
    [Fact]
    public async Task Get_WithValidId_ShouldReturnCorrectBooking()
    {
        // Arrange
        var (bookingService, eventService) = CreateServices();
        var eventId = CreateTestEvent(eventService);
        var created = await bookingService.CreateBookingAsync(eventId);

        // Act
        var result = bookingService.Get(created.Id);

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
        var eventId = CreateTestEvent(eventService);
        var booking = await bookingService.CreateBookingAsync(eventId);

        // Act & Assert - подтверждение брони
        booking.Status = BookingStatus.Confirmed;
        booking.ProcessedAt = DateTime.UtcNow;
        bookingService.Update(booking);

        var confirmed = bookingService.Get(booking.Id);
        Assert.Equal(BookingStatus.Confirmed, confirmed.Status);

        // Отмена брони
        booking.Status = BookingStatus.Rejected;
        booking.ProcessedAt = DateTime.UtcNow;
        bookingService.Update(booking);

        var rejected = bookingService.Get(booking.Id);
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
        var eventId = CreateTestEvent(eventService, totalSeats: 5);

        // Act
        await bookingService.CreateBookingAsync(eventId);

        // Assert
        Assert.Equal(4, eventService.Get(eventId).AvailableSeats);
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
        var eventId = CreateTestEvent(eventService, totalSeats);

        // Act
        var bookings = new List<Booking>();
        for (var i = 0; i < totalSeats; i++)
            bookings.Add(await bookingService.CreateBookingAsync(eventId));

        // Assert
        Assert.Equal(totalSeats, bookings.Select(b => b.Id).Distinct().Count());
        Assert.All(bookings, b => Assert.Equal(BookingStatus.Pending, b.Status));
        Assert.Equal(0, eventService.Get(eventId).AvailableSeats);
    }

    /// <summary>
    /// Тест: Confirm() переводит бронь в статус Confirmed и заполняет ProcessedAt
    /// </summary>
    [Fact]
    public void Confirm_ShouldSetStatusConfirmedAndProcessedAt()
    {
        // Arrange
        var booking = new Booking { Id = Guid.NewGuid(), EventId = Guid.NewGuid(), Status = BookingStatus.Pending, CreatedAt = DateTime.UtcNow };

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
        var booking = new Booking { Id = Guid.NewGuid(), EventId = Guid.NewGuid(), Status = BookingStatus.Pending, CreatedAt = DateTime.UtcNow };

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
        var eventId = CreateTestEvent(eventService, totalSeats: 1);
        var booking = await bookingService.CreateBookingAsync(eventId);
        Assert.Equal(0, eventService.Get(eventId).AvailableSeats);

        // Act
        booking.Reject();
        eventService.ReleaseSeats(eventId);
        bookingService.Update(booking);

        // Assert
        Assert.Equal(1, eventService.Get(eventId).AvailableSeats);
    }

    /// <summary>
    /// Тест: после Reject() и ReleaseSeats() освободившееся место можно занять новой бронью
    /// </summary>
    [Fact]
    public async Task Reject_ThenReleaseSeats_AllowsNewBookingForSameSeat()
    {
        // Arrange
        var (bookingService, eventService) = CreateServices();
        var eventId = CreateTestEvent(eventService, totalSeats: 1);
        var rejectedBooking = await bookingService.CreateBookingAsync(eventId);
        rejectedBooking.Reject();
        eventService.ReleaseSeats(eventId);
        bookingService.Update(rejectedBooking);

        // Act
        var newBooking = await bookingService.CreateBookingAsync(eventId);

        // Assert
        Assert.NotEqual(rejectedBooking.Id, newBooking.Id);
        Assert.Equal(BookingStatus.Pending, newBooking.Status);
        Assert.Equal(0, eventService.Get(eventId).AvailableSeats);
    }

    #endregion

    #region Конкурентность

    /// <summary>
    /// Тест: при 20 конкурентных запросах на событие с 5 местами успешными должны стать
    /// ровно 5 броней, остальные 15 — NoAvailableSeatsException, AvailableSeats = 0
    /// </summary>
    [Fact]
    public async Task CreateBookingAsync_ConcurrentRequestsExceedingCapacity_ShouldPreventOverbooking()
    {
        // Arrange
        const int totalSeats = 5;
        const int concurrentRequests = 20;
        var (bookingService, eventService) = CreateServices();
        var eventId = CreateTestEvent(eventService, totalSeats);

        // Act
        var results = await Task.WhenAll(
            Enumerable.Range(0, concurrentRequests)
                .Select(_ => TryCreateBookingAsync(bookingService, eventId)));

        // Assert
        Assert.Equal(totalSeats, results.Count(success => success));
        Assert.Equal(concurrentRequests - totalSeats, results.Count(success => !success));
        Assert.Equal(0, eventService.Get(eventId).AvailableSeats);
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
        var eventId = CreateTestEvent(eventService, totalSeats);

        // Act
        var bookings = await Task.WhenAll(
            Enumerable.Range(0, totalSeats)
                .Select(_ => Task.Run(() => bookingService.CreateBookingAsync(eventId))));

        // Assert
        Assert.Equal(totalSeats, bookings.Length);
        Assert.Equal(totalSeats, bookings.Select(b => b.Id).Distinct().Count());
        Assert.Equal(0, eventService.Get(eventId).AvailableSeats);
    }

    /// <summary>
    /// Запускает CreateBookingAsync на пуле потоков и возвращает true при успехе,
    /// false — если брошено NoAvailableSeatsException
    /// </summary>
    private static Task<bool> TryCreateBookingAsync(IBookingService bookingService, Guid eventId) =>
        Task.Run(async () =>
        {
            try
            {
                await bookingService.CreateBookingAsync(eventId);
                return true;
            }
            catch (NoAvailableSeatsException)
            {
                return false;
            }
        });

    #endregion

    #region Неуспешные сценарии

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
        var eventId = CreateTestEvent(eventService);
        eventService.Delete(eventId);

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
        var eventId = CreateTestEvent(eventService);
        await bookingService.CreateBookingAsync(eventId);

        // Act & Assert
        Assert.Throws<NotFoundException>(() => bookingService.Get(Guid.NewGuid()));
    }

    /// <summary>
    /// Тест: бронирование события без свободных мест выбрасывает NoAvailableSeatsException
    /// </summary>
    [Fact]
    public async Task CreateBookingAsync_WhenNoSeatsRemaining_ShouldThrowNoAvailableSeatsException()
    {
        // Arrange
        var (bookingService, eventService) = CreateServices();
        var eventId = CreateTestEvent(eventService, totalSeats: 1);
        await bookingService.CreateBookingAsync(eventId);

        // Act & Assert
        await Assert.ThrowsAsync<NoAvailableSeatsException>(() =>
            bookingService.CreateBookingAsync(eventId));
    }

    #endregion
}