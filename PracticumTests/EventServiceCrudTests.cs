using PracticumApi.Models;
using PracticumApi.Exceptions;

namespace PracticumTests;

public partial class EventServiceIntegrationTests
{
    #region Успешные сценарии

    /// <summary>
    /// Тест успешного создания события
    /// </summary>
    [Fact]
    public void Add_ValidEvent_ShouldAddEvent()
    {
        // Arrange
        var eventService = CreateEventService();
        var newEvent = new Event
        {
            Title = "Conference 2024",
            Description = "Annual tech conference",
            StartAt = new DateTime(2024, 6, 15, 09, 00, 00),
            EndAt = new DateTime(2024, 6, 15, 17, 00, 00)
        };

        // Act
        eventService.Add(newEvent);
        var result = eventService.Get(newEvent.Id);

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
    public void Get_WithValidId_ShouldReturnEvent()
    {
        // Arrange
        var eventService = CreateEventService();
        var newEvent = new Event
        {
            Title = "Meeting",
            StartAt = new DateTime(2024, 5, 20, 10, 00, 00),
            EndAt = new DateTime(2024, 5, 20, 11, 00, 00)
        };
        eventService.Add(newEvent);

        // Act
        var result = eventService.Get(newEvent.Id);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(newEvent.Id, result.Id);
        Assert.Equal("Meeting", result.Title);
    }

    /// <summary>
    /// Тест обновления существующего события
    /// </summary>
    [Fact]
    public void Update_ExistingEvent_ShouldUpdateEvent()
    {
        // Arrange
        var eventService = CreateEventService();
        var originalEvent = new Event
        {
            Title = "Original Event",
            StartAt = new DateTime(2024, 5, 20, 10, 00, 00),
            EndAt = new DateTime(2024, 5, 20, 11, 00, 00)
        };
        eventService.Add(originalEvent);

        var updatedEvent = new Event
        {
            Id = originalEvent.Id,
            Title = "Updated Event",
            Description = "Updated description",
            StartAt = new DateTime(2024, 5, 21, 14, 00, 00),
            EndAt = new DateTime(2024, 5, 21, 15, 00, 00)
        };

        // Act
        eventService.Update(updatedEvent);
        var result = eventService.Get(originalEvent.Id);

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
    public void Delete_ExistingEvent_ShouldDeleteEvent()
    {
        // Arrange
        var eventService = CreateEventService();
        var newEvent = new Event
        {
            Title = "Event to Delete",
            StartAt = new DateTime(2024, 5, 20, 10, 00, 00),
            EndAt = new DateTime(2024, 5, 20, 11, 00, 00)
        };
        eventService.Add(newEvent);
        Assert.NotNull(eventService.Get(newEvent.Id));

        // Act
        eventService.Delete(newEvent.Id);

        // Assert - попытка получить удалённое событие должна выбросить исключение
        Assert.Throws<NotFoundException>(() => eventService.Get(newEvent.Id));
    }

    /// <summary>
    /// Тест: RecalculateAvailableSeats допускает уменьшение TotalSeats ровно до числа
    /// уже занятых мест — AvailableSeats становится 0
    /// </summary>
    [Fact]
    public void RecalculateAvailableSeats_EqualToTakenSeats_ShouldReturnZero()
    {
        // Arrange
        var eventItem = Event.Create("Conference", null, DateTime.Now, DateTime.Now.AddHours(1), totalSeats: 10);
        eventItem.TryReserveSeats(7);

        // Act
        var result = eventItem.RecalculateAvailableSeats(7);

        // Assert
        Assert.Equal(0, result);
    }

    #endregion

    #region Неуспешные сценарии

    /// <summary>
    /// Тест попытки получить событие с несуществующим ID
    /// </summary>
    [Fact]
    public void Get_WithInvalidId_ShouldThrowNotFoundException()
    {
        // Arrange
        var eventService = CreateEventService();
        eventService.Add(new Event { Title = "Event", StartAt = DateTime.Now, EndAt = DateTime.Now.AddHours(1) });

        // Act & Assert
        Assert.Throws<NotFoundException>(() => eventService.Get(Guid.NewGuid()));
    }

    /// <summary>
    /// Тест попытки получить событие из пустого списка
    /// </summary>
    [Fact]
    public void Get_FromEmptyList_ShouldThrowNotFoundException()
    {
        // Arrange
        var eventService = CreateEventService();

        // Act & Assert
        Assert.Throws<NotFoundException>(() => eventService.Get(Guid.NewGuid()));
    }

    /// <summary>
    /// Тест попытки обновить событие с несуществующим ID
    /// </summary>
    [Fact]
    public void Update_WithInvalidId_ShouldThrowNotFoundException()
    {
        // Arrange
        var eventService = CreateEventService();
        var existingEvent = new Event { Title = "Event 1", StartAt = DateTime.Now, EndAt = DateTime.Now.AddHours(1) };
        eventService.Add(existingEvent);

        var nonExistentEvent = new Event
        {
            Id = Guid.NewGuid(),
            Title = "Non-existent Event",
            StartAt = DateTime.Now.AddDays(1),
            EndAt = DateTime.Now.AddDays(1).AddHours(1)
        };

        // Act & Assert
        Assert.Throws<NotFoundException>(() => eventService.Update(nonExistentEvent));
    }

    /// <summary>
    /// Тест попытки удалить событие с несуществующим ID
    /// </summary>
    [Fact]
    public void Delete_WithInvalidId_ShouldThrowNotFoundException()
    {
        // Arrange
        var eventService = CreateEventService();
        eventService.Add(new Event { Title = "Event 1", StartAt = DateTime.Now, EndAt = DateTime.Now.AddHours(1) });

        // Act & Assert
        Assert.Throws<NotFoundException>(() => eventService.Delete(Guid.NewGuid()));
    }

    /// <summary>
    /// Тест: нельзя уменьшить TotalSeats ниже числа уже занятых мест — иначе фоновый
    /// сервис мог бы подтвердить больше Pending-броней, чем есть физических мест
    /// </summary>
    [Fact]
    public void RecalculateAvailableSeats_BelowTakenSeats_ShouldThrowValidationException()
    {
        // Arrange
        var eventItem = Event.Create("Conference", null, DateTime.Now, DateTime.Now.AddHours(1), totalSeats: 10);
        eventItem.TryReserveSeats(7); // занято 7 мест из 10

        // Act & Assert
        Assert.Throws<ValidationException>(() => eventItem.RecalculateAvailableSeats(5));
    }

    #endregion
}
