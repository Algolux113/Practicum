using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Infrastructure.DataAccess;
using Infrastructure.DataAccess.Repositories;
using Domain.Exceptions;
using Application.Interfaces;
using Domain.Models;
using Application.Services;

namespace Tests;

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
    private readonly Guid _userId;

    public BookingServiceIntegrationTests()
    {
        // Имя вычисляется один раз, чтобы разные scope обращались к одной базе.
        var dbName = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(dbName));
        services.AddScoped<IEventRepository, EventRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IBookingRepository, BookingRepository>();
        services.AddScoped<IEventService, EventService>();
        services.AddScoped<IBookingService, BookingService>();
        services.AddScoped<IUserRepository, UserRepository>();
        _serviceProvider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });
        _scope = _serviceProvider.CreateScope();
        var context = _scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = User.Create("test-user", "test-hash");
        context.Users.Add(user);
        context.SaveChanges();
        _userId = user.Id;
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

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public async Task CreateBookingAsync_ShouldRejectStartedOrEndedEventWithoutCreatingBooking(int endOffsetHours)
    {
        var (bookings, events) = CreateServices();
        var now = DateTime.UtcNow;
        var eventItem = await events.CreateAsync(
            Event.Create("Событие", null, now.AddHours(-2), now.AddHours(endOffsetHours), 1).ToDto());

        await Assert.ThrowsAsync<PastEventBookingException>(() => bookings.CreateBookingAsync(eventItem.Id, _userId));

        var context = _scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Empty(await context.Bookings.AsNoTracking().ToListAsync());
        Assert.Equal(1, (await events.GetAsync(eventItem.Id)).AvailableSeats);
    }

    [Fact]
    public async Task CreateBookingAsync_AtActiveLimit_ShouldNotCreateBookingOrReserveSeat()
    {
        var (bookings, events) = CreateServices();
        var firstEventId = await CreateTestEvent(events, totalSeats: 15);
        var otherEventId = await CreateTestEvent(events, totalSeats: 3);
        var first = await bookings.CreateBookingAsync(firstEventId, _userId);
        await bookings.ProcessPendingAsync(first.Id);
        for (var i = 1; i < 10; i++)
            await bookings.CreateBookingAsync(firstEventId, _userId);

        var exception = await Assert.ThrowsAsync<ActiveBookingLimitExceededException>(() =>
            bookings.CreateBookingAsync(otherEventId, _userId));

        Assert.Contains("10", exception.Message);
        var context = _scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(10, await context.Bookings.CountAsync());
        Assert.Equal(3, (await events.GetAsync(otherEventId)).AvailableSeats);
    }

    [Fact]
    public async Task CreateBookingAsync_ShouldEnforceLimitIndependentlyForEachUser()
    {
        var (bookings, events) = CreateServices();
        var context = _scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var otherUser = User.Create("other-user", "test-hash");
        context.Users.Add(otherUser);
        await context.SaveChangesAsync();
        var eventId = await CreateTestEvent(events, totalSeats: 25);

        for (var i = 0; i < 10; i++)
            await bookings.CreateBookingAsync(eventId, _userId);
        for (var i = 0; i < 10; i++)
            await bookings.CreateBookingAsync(eventId, otherUser.Id);

        await Assert.ThrowsAsync<ActiveBookingLimitExceededException>(() =>
            bookings.CreateBookingAsync(eventId, otherUser.Id));

        Assert.Equal(10, await context.Bookings.CountAsync(booking => booking.UserId == _userId));
        Assert.Equal(10, await context.Bookings.CountAsync(booking => booking.UserId == otherUser.Id));
        Assert.Equal(5, (await events.GetAsync(eventId)).AvailableSeats);
    }

    [Fact]
    public async Task CancelAsync_ShouldCheckOwnerOrAdminAndReleaseSeatOnce()
    {
        var (bookings, events) = CreateServices();
        var eventId = await CreateTestEvent(events, totalSeats: 2);
        var first = await bookings.CreateBookingAsync(eventId, _userId);
        var second = await bookings.CreateBookingAsync(eventId, _userId);
        await bookings.ProcessPendingAsync(second.Id);
        var otherUserId = Guid.NewGuid();

        await Assert.ThrowsAsync<ForbiddenOperationException>(() =>
            bookings.CancelAsync(first.Id, otherUserId, UserRole.User));
        Assert.Equal(0, (await events.GetAsync(eventId)).AvailableSeats);
        Assert.Equal(BookingStatus.Cancelled, await bookings.CancelAsync(first.Id, _userId, UserRole.User));
        Assert.Equal(BookingStatus.Cancelled, await bookings.CancelAsync(first.Id, _userId, UserRole.User));
        Assert.Equal(1, (await events.GetAsync(eventId)).AvailableSeats);
        Assert.Equal(BookingStatus.Cancelled, await bookings.CancelAsync(second.Id, otherUserId, UserRole.Admin));
        Assert.Equal(2, (await events.GetAsync(eventId)).AvailableSeats);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RejectAsync_ShouldReleaseSeatExactlyOnce(bool confirmed)
    {
        var (bookings, events) = CreateServices();
        var eventId = await CreateTestEvent(events, totalSeats: 3);
        var booking = await bookings.CreateBookingAsync(eventId, _userId);
        await bookings.CreateBookingAsync(eventId, _userId);
        if (confirmed)
            await bookings.ProcessPendingAsync(booking.Id);

        await bookings.RejectAsync(booking.Id);
        await bookings.RejectAsync(booking.Id);

        Assert.Equal(2, (await events.GetAsync(eventId)).AvailableSeats);
        Assert.NotNull((await bookings.GetAsync(booking.Id, _userId, UserRole.User)).ProcessedAt);
    }

    /// <summary>
    /// Вспомогательный метод создания события, возвращает его Id
    /// </summary>
    private static async Task<Guid> CreateTestEvent(IEventService eventService, int totalSeats = 100)
    {
        var newEvent = Event.Create(
            "Conference 2024",
            "Annual tech conference",
            DateTime.UtcNow.AddHours(1),
            DateTime.UtcNow.AddHours(2),
            totalSeats);
        newEvent = await eventService.CreateAsync(newEvent.ToDto());
        return newEvent.Id;
    }

    #region Успешные сценарии

    /// <summary>
    /// Тест: удаление события сохраняет историю броней и отклоняет ожидающую бронь при обработке
    /// </summary>
    [Fact]
    public async Task DeleteEvent_ShouldPreserveHistoryAndRejectPendingBooking()
    {
        // Arrange
        var (bookings, events) = CreateServices();
        var eventId = await CreateTestEvent(events, totalSeats: 3);
        var confirmed = await bookings.CreateBookingAsync(eventId, _userId);
        await bookings.ProcessPendingAsync(confirmed.Id);
        var pending = await bookings.CreateBookingAsync(eventId, _userId);

        // Act
        await events.DeleteAsync(eventId);

        // Assert
        using var scope = _serviceProvider.CreateScope();
        var freshEvents = scope.ServiceProvider.GetRequiredService<IEventService>();
        var freshBookings = scope.ServiceProvider.GetRequiredService<IBookingService>();
        await Assert.ThrowsAsync<NotFoundException>(() => freshEvents.GetAsync(eventId));
        Assert.Empty((await freshEvents.GetAllAsync()).Items);
        await Assert.ThrowsAsync<NotFoundException>(() => freshBookings.CreateBookingAsync(eventId, _userId));
        Assert.Equal(BookingStatus.Confirmed, (await freshBookings.GetAsync(confirmed.Id, _userId, UserRole.User)).Status);
        Assert.Equal(BookingStatus.Rejected, await freshBookings.ProcessPendingAsync(pending.Id));
        Assert.Equal(BookingStatus.Rejected, (await freshBookings.GetAsync(pending.Id, _userId, UserRole.User)).Status);
        Assert.Equal(2, (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Bookings.CountAsync()));
    }

    /// <summary>
    /// Тест: отклонение брони сохраняет статус и освобождает место за одно сохранение,
    /// а повторное отклонение и обработка не изменяют результат
    /// </summary>
    [Fact]
    public async Task RejectAsync_ShouldSaveStatusAndSeatOnceAndBeIdempotent()
    {
        // Arrange
        var (bookings, events) = CreateServices();
        var eventId = await CreateTestEvent(events, totalSeats: 3);
        var booking = await bookings.CreateBookingAsync(eventId, _userId);
        await bookings.CreateBookingAsync(eventId, _userId);
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<IBookingService>();
        var saves = 0;
        context.SavingChanges += (_, _) => saves++;

        // Act
        await service.RejectAsync(booking.Id);
        await service.RejectAsync(booking.Id);
        await service.ProcessPendingAsync(booking.Id);

        // Assert
        Assert.Equal(1, saves);
        Assert.Equal(BookingStatus.Rejected, (await bookings.GetAsync(booking.Id, _userId, UserRole.User)).Status);
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
        var result = await bookingService.CreateBookingAsync(eventId, _userId);

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
        var booking1 = await bookingService.CreateBookingAsync(eventId, _userId);
        var booking2 = await bookingService.CreateBookingAsync(eventId, _userId);
        var booking3 = await bookingService.CreateBookingAsync(eventId, _userId);

        // Assert
        Assert.NotNull(booking1);
        Assert.NotNull(booking2);
        Assert.NotNull(booking3);
        Assert.NotEqual(booking1.Id, booking2.Id);
        Assert.NotEqual(booking1.Id, booking3.Id);
        Assert.NotEqual(booking2.Id, booking3.Id);
        Assert.All(new[] { booking1, booking2, booking3 }, b => Assert.Equal(eventId, b.EventId));
        Assert.Equal(3, (await _scope.ServiceProvider.GetRequiredService<AppDbContext>().Bookings.CountAsync()));
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
        var created = await bookingService.CreateBookingAsync(eventId, _userId);

        // Act
        var result = (await bookingService.GetAsync(created.Id, _userId, UserRole.User));

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
        var booking = await bookingService.CreateBookingAsync(eventId, _userId);

        // Act & Assert - подтверждение брони

        await bookingService.ProcessPendingAsync(booking.Id);

        var confirmed = (await bookingService.GetAsync(booking.Id, _userId, UserRole.User));
        Assert.Equal(BookingStatus.Confirmed, confirmed.Status);

        // Отмена брони

        await bookingService.RejectAsync(booking.Id);

        var rejected = (await bookingService.GetAsync(booking.Id, _userId, UserRole.User));
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
        await bookingService.CreateBookingAsync(eventId, _userId);

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
            await bookingService.CreateBookingAsync(eventId, _userId);
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
            bookings.Add(await bookingService.CreateBookingAsync(eventId, _userId));

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
        var booking = Booking.Create(Guid.NewGuid(), Guid.NewGuid());

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
        var booking = Booking.Create(Guid.NewGuid(), Guid.NewGuid());

        // Act
        booking.Reject();

        // Assert
        Assert.Equal(BookingStatus.Rejected, booking.Status);
        Assert.NotNull(booking.ProcessedAt);
    }

    /// <summary>
    /// Тест: сохранение отказа автоматически восстанавливает количество свободных мест
    /// </summary>
    [Fact]
    public async Task RejectBooking_ShouldRestoreAvailableSeats()
    {
        // Arrange
        var (bookingService, eventService) = CreateServices();
        var eventId = await CreateTestEvent(eventService, totalSeats: 1);
        var booking = await bookingService.CreateBookingAsync(eventId, _userId);
        Assert.Equal(0, (await eventService.GetAsync(eventId)).AvailableSeats);

        // Act
        booking.Reject();
        await bookingService.RejectAsync(booking.Id);

        // Assert
        Assert.Equal(1, (await eventService.GetAsync(eventId)).AvailableSeats);
    }

    /// <summary>
    /// Тест: после сохранения отказа освободившееся место можно занять новой бронью
    /// </summary>
    [Fact]
    public async Task RejectBooking_AllowsNewBookingForSameSeat()
    {
        // Arrange
        var (bookingService, eventService) = CreateServices();
        var eventId = await CreateTestEvent(eventService, totalSeats: 1);
        var rejectedBooking = await bookingService.CreateBookingAsync(eventId, _userId);
        rejectedBooking.Reject();
        await bookingService.RejectAsync(rejectedBooking.Id);

        // Act
        var newBooking = await bookingService.CreateBookingAsync(eventId, _userId);

        // Assert
        Assert.NotEqual(rejectedBooking.Id, newBooking.Id);
        Assert.Equal(BookingStatus.Pending, newBooking.Status);
        Assert.Equal(0, (await eventService.GetAsync(eventId)).AvailableSeats);
    }

    #endregion



    #region Неуспешные сценарии

    [Fact]
    public async Task CreateBookingAsync_WithStaleTrackedEvent_ShouldUseCurrentSeatCount()
    {
        // Arrange
        var (bookingService, eventService) = CreateServices();
        var eventId = await CreateTestEvent(eventService, totalSeats: 1);
        using var otherScope = _serviceProvider.CreateScope();
        await otherScope.ServiceProvider.GetRequiredService<IBookingService>().CreateBookingAsync(eventId, _userId);

        // Act & Assert
        await Assert.ThrowsAsync<NoAvailableSeatsException>(() => bookingService.CreateBookingAsync(eventId, _userId));
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
        var booking = await scope.ServiceProvider.GetRequiredService<IBookingService>().CreateBookingAsync(eventId, _userId);

        // Assert
        Assert.Equal(1, saves);
        using var verificationScope = _serviceProvider.CreateScope();
        var verification = verificationScope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, (await verification.Events.SingleAsync()).AvailableSeats);
        var storedBooking = await verification.Bookings.SingleAsync();
        Assert.Equal(booking.Id, storedBooking.Id);
        Assert.Equal(_userId, storedBooking.UserId);
        var json = System.Text.Json.JsonSerializer.Serialize(Application.DTOs.BookingResponse.From(booking));
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
            bookingService.CreateBookingAsync(Guid.NewGuid(), _userId));
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
            bookingService.CreateBookingAsync(eventId, _userId));
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
        await bookingService.CreateBookingAsync(eventId, _userId);

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(async () => await bookingService.GetAsync(Guid.NewGuid(), _userId, UserRole.User));
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
        await bookingService.CreateBookingAsync(eventId, _userId);

        // Act & Assert
        await Assert.ThrowsAsync<NoAvailableSeatsException>(() =>
            bookingService.CreateBookingAsync(eventId, _userId));
    }

    #endregion
}
