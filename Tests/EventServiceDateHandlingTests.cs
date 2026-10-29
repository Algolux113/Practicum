using Application.Common;
using Domain.Models;
using Application.DTOs;

namespace Tests;

public partial class EventServiceIntegrationTests
{
    /// <summary>
    /// Тест: добавление и обновление события нормализуют даты в UTC независимо от исходного DateTimeKind
    /// </summary>
    [Theory]
    [InlineData(DateTimeKind.Unspecified)]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Utc)]
    public async Task AddAndUpdate_ShouldNormalizeDatesToUtc(DateTimeKind kind)
    {
        // Arrange
        var service = CreateEventService();
        var start = DateTime.SpecifyKind(new DateTime(2026, 1, 1, 12, 0, 0), kind);
        var eventItem = Event.Create("Даты", null, start, start.AddHours(1), 5);

        // Act
        eventItem = await service.CreateAsync(eventItem.ToDto());
        var saved = await service.GetAsync(eventItem.Id);

        // Assert
        Assert.Equal(DateTimeKind.Utc, saved.StartAt.Kind);
        Assert.Equal(UtcDateTime.Normalize(start), saved.StartAt);

        // Act
        var update = saved.ToDto();
        update.StartAt = start.AddDays(1);
        update.EndAt = start.AddDays(1).AddHours(1);
        await service.UpdateAsync(saved.Id, update);
        var updated = await service.GetAsync(saved.Id);

        // Assert
        Assert.Equal(DateTimeKind.Utc, updated.StartAt.Kind);
        Assert.Equal(DateTimeKind.Utc, updated.EndAt.Kind);
        Assert.Equal(UtcDateTime.Normalize(start.AddDays(1)), updated.StartAt);
    }

    #region Неуспешные сценарии

    /// <summary>
    /// Тест создания события с некорректными датами (EndAt раньше StartAt)
    /// </summary>
    [Fact]
    public async Task Create_WithInvalidDates_ShouldReject()
    {
        // Arrange
        var eventService = CreateEventService();
        var invalidEvent = Event.Create("Invalid Event", null, new DateTime(2024, 6, 15, 17, 00, 00), new DateTime(2024, 6, 15, 09, 00, 00), 100);

        // Act / Assert
        await Assert.ThrowsAsync<Domain.Exceptions.ValidationException>(() => eventService.CreateAsync(invalidEvent.ToDto()));
        Assert.Empty((await eventService.GetAllAsync()).Items);
    }

    /// <summary>
    /// Тест обновления события с некорректными датами
    /// </summary>
    [Fact]
    public async Task Update_WithInvalidDates_ShouldRejectAndPreserveEvent()
    {
        // Arrange
        var eventService = CreateEventService();
        var originalEvent = Event.Create("Original Event", null, new DateTime(2024, 5, 20, 10, 00, 00), new DateTime(2024, 5, 20, 11, 00, 00), 100);
        originalEvent = await eventService.CreateAsync(originalEvent.ToDto());

        var invalidUpdatedEvent = Event.Create("Updated Event", null, new DateTime(2024, 5, 21, 15, 00, 00), new DateTime(2024, 5, 21, 09, 00, 00), 100);

        // Act
        await Assert.ThrowsAsync<Domain.Exceptions.ValidationException>(() =>
            eventService.UpdateAsync(originalEvent.Id, invalidUpdatedEvent.ToDto()));
        var result = (await eventService.GetAsync(originalEvent.Id));

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Original Event", result.Title);
        Assert.True(result.EndAt > result.StartAt);
    }

    #endregion
}
