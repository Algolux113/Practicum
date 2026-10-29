using Domain.Models;
using Domain.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using Application.Interfaces;
using Application.DTOs;

namespace Tests;

public partial class EventServiceIntegrationTests
{
    [Fact]
    public async Task Create_FromDto_ShouldInitializeSeatsAndNormalizeDates()
    {
        // Arrange
        var service = CreateEventService();
        var dto = new EventDTO
        {
            Title = "Событие", Description = "Описание",
            StartAt = new DateTime(2026, 11, 1, 10, 0, 0),
            EndAt = new DateTime(2026, 11, 1, 11, 0, 0), TotalSeats = 3
        };

        // Act
        var created = await service.CreateAsync(dto);
        var saved = await service.GetAsync(created.Id);

        // Assert
        Assert.NotEqual(Guid.Empty, saved.Id);
        Assert.Equal(3, saved.TotalSeats);
        Assert.Equal(3, saved.AvailableSeats);
        Assert.Equal(DateTimeKind.Utc, saved.StartAt.Kind);
        Assert.Equal(dto.Description, saved.Description);
    }

    [Fact]
    public async Task Update_FromDto_ShouldRejectCapacityBelowReservedSeats()
    {
        // Arrange
        var service = CreateEventService();
        var dto = new EventDTO
        {
            Title = "Событие", StartAt = DateTime.UtcNow,
            EndAt = DateTime.UtcNow.AddHours(1), TotalSeats = 3
        };
        var created = await service.CreateAsync(dto);
        var bookingService = _scope.ServiceProvider.GetRequiredService<IBookingService>();
        await bookingService.CreateBookingAsync(created.Id);
        await bookingService.CreateBookingAsync(created.Id);
        dto.TotalSeats = 1;

        // Act / Assert
        await Assert.ThrowsAsync<ValidationException>(() => service.UpdateAsync(created.Id, dto));
        var saved = await service.GetAsync(created.Id);
        Assert.Equal(3, saved.TotalSeats);
        Assert.Equal(1, saved.AvailableSeats);
    }

    [Fact]
    public async Task Create_FromInvalidDto_ShouldValidateWithoutHttpPipeline()
    {
        // Arrange
        var service = CreateEventService();
        var dto = new EventDTO
        {
            Title = "Событие", StartAt = DateTime.UtcNow,
            EndAt = DateTime.UtcNow.AddHours(-1), TotalSeats = 1
        };

        // Act / Assert
        await Assert.ThrowsAsync<ValidationException>(() => service.CreateAsync(dto));
        Assert.Empty((await service.GetAllAsync()).Items);
    }

    #region Успешные сценарии

    [Fact]
    public async Task Update_AfterBookingInAnotherContext_ShouldPreserveReservedSeats()
    {
        // Arrange
        var eventService = CreateEventService();
        var eventItem = Event.Create("Событие", null, DateTime.UtcNow, DateTime.UtcNow.AddHours(1), 3);
        eventItem = await eventService.CreateAsync(eventItem.ToDto());
        using var otherScope = _serviceProvider.CreateScope();
        var bookingService = otherScope.ServiceProvider.GetRequiredService<IBookingService>();
        await bookingService.CreateBookingAsync(eventItem.Id);

        // Act
        var update = eventItem.ToDto();
        update.TotalSeats = 5;
        await eventService.UpdateAsync(eventItem.Id, update);

        // Assert
        Assert.Equal(4, (await eventService.GetAsync(eventItem.Id)).AvailableSeats);
    }

    /// <summary>
    /// Тест успешного создания события
    /// </summary>
    [Fact]
    public async Task Add_ValidEvent_ShouldAddEvent()
    {
        // Arrange
        var eventService = CreateEventService();
        var newEvent = Event.Create("Conference 2024", "Annual tech conference", new DateTime(2024, 6, 15, 09, 00, 00), new DateTime(2024, 6, 15, 17, 00, 00), 100);

        // Act
        newEvent = await eventService.CreateAsync(newEvent.ToDto());
        var result = (await eventService.GetAsync(newEvent.Id));

        // Assert
        Assert.NotNull(result);
        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal("Conference 2024", result.Title);
        Assert.Equal("Annual tech conference", result.Description);
    }

    /// <summary>
    /// Тест получения события по ID
    /// </summary>
    [Fact]
    public async Task Get_WithValidId_ShouldReturnEvent()
    {
        // Arrange
        var eventService = CreateEventService();
        var newEvent = Event.Create("Meeting", null, new DateTime(2024, 5, 20, 10, 00, 00), new DateTime(2024, 5, 20, 11, 00, 00), 100);
        newEvent = await eventService.CreateAsync(newEvent.ToDto());

        // Act
        var result = (await eventService.GetAsync(newEvent.Id));

        // Assert
        Assert.NotNull(result);
        Assert.Equal(newEvent.Id, result.Id);
        Assert.Equal("Meeting", result.Title);
    }

    /// <summary>
    /// Тест обновления существующего события
    /// </summary>
    [Fact]
    public async Task Update_ExistingEvent_ShouldUpdateEvent()
    {
        // Arrange
        var eventService = CreateEventService();
        var originalEvent = Event.Create("Original Event", null, new DateTime(2024, 5, 20, 10, 00, 00), new DateTime(2024, 5, 20, 11, 00, 00), 100);
        originalEvent = await eventService.CreateAsync(originalEvent.ToDto());

        var updatedEvent = Event.Create("Updated Event", "Updated description", new DateTime(2024, 5, 21, 14, 00, 00), new DateTime(2024, 5, 21, 15, 00, 00), 100);

        // Act
        await eventService.UpdateAsync(originalEvent.Id, updatedEvent.ToDto());
        var result = (await eventService.GetAsync(originalEvent.Id));

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Updated Event", result.Title);
        Assert.Equal("Updated description", result.Description);
        Assert.Equal(new DateTime(2024, 5, 21, 14, 00, 00), result.StartAt);
    }

    /// <summary>
    /// Тест удаления существующего события
    /// </summary>
    [Fact]
    public async Task Delete_ExistingEvent_ShouldDeleteEvent()
    {
        // Arrange
        var eventService = CreateEventService();
        var newEvent = Event.Create("Event to Delete", null, new DateTime(2024, 5, 20, 10, 00, 00), new DateTime(2024, 5, 20, 11, 00, 00), 100);
        newEvent = await eventService.CreateAsync(newEvent.ToDto());
        Assert.NotNull((await eventService.GetAsync(newEvent.Id)));

        // Act
        await eventService.DeleteAsync(newEvent.Id);

        // Assert - попытка получить удалённое событие должна выбросить исключение
        await Assert.ThrowsAsync<NotFoundException>(async () => await eventService.GetAsync(newEvent.Id));
    }

    /// <summary>
    /// Тест: Update допускает уменьшение TotalSeats ровно до числа
    /// уже занятых мест — AvailableSeats становится 0
    /// </summary>
    [Fact]
    public void Update_EqualToTakenSeats_ShouldLeaveNoAvailableSeats()
    {
        // Arrange
        var eventItem = Event.Create("Conference", null, DateTime.Now, DateTime.Now.AddHours(1), totalSeats: 10);
        eventItem.TryReserveSeats(7);

        // Act
        eventItem.Update(eventItem.Title, eventItem.Description, eventItem.StartAt, eventItem.EndAt, 7);
        var result = eventItem.AvailableSeats;

        // Assert
        Assert.Equal(0, result);
    }

    #endregion

    #region Неуспешные сценарии

    /// <summary>
    /// Тест попытки получить событие с несуществующим ID
    /// </summary>
    [Fact]
    public async Task Get_WithInvalidId_ShouldThrowNotFoundException()
    {
        // Arrange
        var eventService = CreateEventService();
        await eventService.CreateAsync((Event.Create("Event", null, DateTime.Now, DateTime.Now.AddHours(1), 100)).ToDto());

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(async () => await eventService.GetAsync(Guid.NewGuid()));
    }

    /// <summary>
    /// Тест попытки получить событие из пустого списка
    /// </summary>
    [Fact]
    public async Task Get_FromEmptyList_ShouldThrowNotFoundException()
    {
        // Arrange
        var eventService = CreateEventService();

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(async () => await eventService.GetAsync(Guid.NewGuid()));
    }

    /// <summary>
    /// Тест попытки обновить событие с несуществующим ID
    /// </summary>
    [Fact]
    public async Task Update_WithInvalidId_ShouldThrowNotFoundException()
    {
        // Arrange
        var eventService = CreateEventService();
        var existingEvent = Event.Create("Event 1", null, DateTime.Now, DateTime.Now.AddHours(1), 100);
        existingEvent = await eventService.CreateAsync(existingEvent.ToDto());

        var nonExistentEvent = Event.Create("Non-existent Event", null, DateTime.Now.AddDays(1), DateTime.Now.AddDays(1).AddHours(1), 100);

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(async () => await eventService.UpdateAsync(nonExistentEvent.Id, nonExistentEvent.ToDto()));
    }

    /// <summary>
    /// Тест попытки удалить событие с несуществующим ID
    /// </summary>
    [Fact]
    public async Task Delete_WithInvalidId_ShouldThrowNotFoundException()
    {
        // Arrange
        var eventService = CreateEventService();
        await eventService.CreateAsync((Event.Create("Event 1", null, DateTime.Now, DateTime.Now.AddHours(1), 100)).ToDto());

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(async () => await eventService.DeleteAsync(Guid.NewGuid()));
    }

    /// <summary>
    /// Тест: нельзя уменьшить TotalSeats ниже числа уже занятых мест — иначе фоновый
    /// сервис мог бы подтвердить больше Pending-броней, чем есть физических мест
    /// </summary>
    [Fact]
    public void Update_BelowTakenSeats_ShouldThrowValidationException()
    {
        // Arrange
        var eventItem = Event.Create("Conference", null, DateTime.Now, DateTime.Now.AddHours(1), totalSeats: 10);
        eventItem.TryReserveSeats(7); // занято 7 мест из 10

        // Act & Assert
        Assert.Throws<ValidationException>(() => eventItem.Update(eventItem.Title, eventItem.Description, eventItem.StartAt, eventItem.EndAt, 5));
    }

    #endregion
}
