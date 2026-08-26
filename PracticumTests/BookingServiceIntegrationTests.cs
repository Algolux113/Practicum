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
    private static Guid CreateEvent(IEventService eventService)
    {
        var newEvent = new Event
        {
            Title = "Conference 2024",
            Description = "Annual tech conference",
            StartAt = new DateTime(2024, 6, 15, 09, 00, 00),
            EndAt = new DateTime(2024, 6, 15, 17, 00, 00)
        };
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
        var eventId = CreateEvent(eventService);

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
        var eventId = CreateEvent(eventService);

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
        var eventId = CreateEvent(eventService);
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
        var eventId = CreateEvent(eventService);
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
        var eventId = CreateEvent(eventService);
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
        var eventId = CreateEvent(eventService);
        await bookingService.CreateBookingAsync(eventId);

        // Act & Assert
        Assert.Throws<NotFoundException>(() => bookingService.Get(Guid.NewGuid()));
    }

    #endregion
}