using PracticumApi.Models;

namespace PracticumTests;

public partial class EventServiceIntegrationTests
{
    #region Неуспешные сценарии

    /// <summary>
    /// Тест создания события с некорректными датами (EndAt раньше StartAt)
    /// </summary>
    [Fact]
    public void Add_WithInvalidDates_ShouldStillAdd()
    {
        // Arrange - в самом сервисе нет валидации, но тест показывает, что такие события можно добавить
        var eventService = CreateEventService();
        var invalidEvent = new Event
        {
            Title = "Invalid Event",
            StartAt = new DateTime(2024, 6, 15, 17, 00, 00),
            EndAt = new DateTime(2024, 6, 15, 09, 00, 00) // EndAt раньше StartAt
        };

        // Act
        eventService.Add(invalidEvent);
        var result = eventService.Get(invalidEvent.Id);

        // Assert
        Assert.NotNull(result);
        // Событие добавлено, но с некорректными датами (валидация должна быть на уровне контроллера через EventDTO)
        Assert.True(result.EndAt < result.StartAt);
    }

    /// <summary>
    /// Тест обновления события с некорректными датами
    /// </summary>
    [Fact]
    public void Update_WithInvalidDates_ShouldStillUpdate()
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

        var invalidUpdatedEvent = new Event
        {
            Id = originalEvent.Id,
            Title = "Updated Event",
            StartAt = new DateTime(2024, 5, 21, 15, 00, 00),
            EndAt = new DateTime(2024, 5, 21, 09, 00, 00) // EndAt раньше StartAt
        };

        // Act
        eventService.Update(invalidUpdatedEvent);
        var result = eventService.Get(originalEvent.Id);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Updated Event", result.Title);
        Assert.True(result.EndAt < result.StartAt);
    }

    #endregion
}
