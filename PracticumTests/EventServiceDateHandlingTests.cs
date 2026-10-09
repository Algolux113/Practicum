using PracticumApi.Models;

namespace PracticumTests;

public partial class EventServiceIntegrationTests
{
    [Theory]
    [InlineData(DateTimeKind.Unspecified)]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Utc)]
    public async Task AddAndUpdate_ShouldNormalizeDatesToUtc(DateTimeKind kind)
    {
        var service = CreateEventService();
        var start = DateTime.SpecifyKind(new DateTime(2026, 1, 1, 12, 0, 0), kind);
        var eventItem = Event.Create("Даты", null, start, start.AddHours(1), 5);
        await service.AddAsync(eventItem);
        var saved = await service.GetAsync(eventItem.Id);
        Assert.Equal(DateTimeKind.Utc, saved.StartAt.Kind);
        Assert.Equal(UtcDateTime.Normalize(start), saved.StartAt);
        saved.StartAt = start.AddDays(1);
        saved.EndAt = start.AddDays(1).AddHours(1);
        await service.UpdateAsync(saved);
        var updated = await service.GetAsync(saved.Id);
        Assert.Equal(DateTimeKind.Utc, updated.StartAt.Kind);
        Assert.Equal(DateTimeKind.Utc, updated.EndAt.Kind);
        Assert.Equal(UtcDateTime.Normalize(start.AddDays(1)), updated.StartAt);
    }

    #region Неуспешные сценарии

    /// <summary>
    /// Тест создания события с некорректными датами (EndAt раньше StartAt)
    /// </summary>
    [Fact]
    public async Task Add_WithInvalidDates_ShouldStillAdd()
    {
        // Arrange - в самом сервисе нет валидации, но тест показывает, что такие события можно добавить
        var eventService = CreateEventService();
        var invalidEvent = new Event("Invalid Event")
        {
            StartAt = new DateTime(2024, 6, 15, 17, 00, 00),
            EndAt = new DateTime(2024, 6, 15, 09, 00, 00) // EndAt раньше StartAt
        };

        // Act
        await eventService.AddAsync(invalidEvent);
        var result = (await eventService.GetAsync(invalidEvent.Id));

        // Assert
        Assert.NotNull(result);
        // Событие добавлено, но с некорректными датами (валидация должна быть на уровне контроллера через EventDTO)
        Assert.True(result.EndAt < result.StartAt);
    }

    /// <summary>
    /// Тест обновления события с некорректными датами
    /// </summary>
    [Fact]
    public async Task Update_WithInvalidDates_ShouldStillUpdate()
    {
        // Arrange
        var eventService = CreateEventService();
        var originalEvent = new Event("Original Event")
        {
            StartAt = new DateTime(2024, 5, 20, 10, 00, 00),
            EndAt = new DateTime(2024, 5, 20, 11, 00, 00)
        };
        await eventService.AddAsync(originalEvent);

        var invalidUpdatedEvent = new Event("Updated Event")
        {
            Id = originalEvent.Id,
            StartAt = new DateTime(2024, 5, 21, 15, 00, 00),
            EndAt = new DateTime(2024, 5, 21, 09, 00, 00) // EndAt раньше StartAt
        };

        // Act
        await eventService.UpdateAsync(invalidUpdatedEvent);
        var result = (await eventService.GetAsync(originalEvent.Id));

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Updated Event", result.Title);
        Assert.True(result.EndAt < result.StartAt);
    }

    #endregion
}
