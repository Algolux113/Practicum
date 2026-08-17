using PracticumApi.Interfaces;
using PracticumApi.Models;
using PracticumApi.Services;
using PracticumApi.Exceptions;

namespace PracticumTests;

/// <summary>
/// Интеграционные тесты для EventService с использованием реальной реализации
/// </summary>
public class EventServiceIntegrationTests
{
    private IEventService CreateEventService() => new EventService();

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
        var result = eventService.Get(1);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(1, result.Id);
        Assert.Equal("Conference 2024", result.Title);
        Assert.Equal("Annual tech conference", result.Description);
    }

    /// <summary>
    /// Тест получения всех событий
    /// </summary>
    [Fact]
    public void GetAll_NoFilters_ShouldReturnAllEvents()
    {
        // Arrange
        var eventService = CreateEventService();
        eventService.Add(new Event { Title = "Event 1", StartAt = DateTime.Now, EndAt = DateTime.Now.AddHours(1) });
        eventService.Add(new Event { Title = "Event 2", StartAt = DateTime.Now, EndAt = DateTime.Now.AddHours(1) });
        eventService.Add(new Event { Title = "Event 3", StartAt = DateTime.Now, EndAt = DateTime.Now.AddHours(1) });

        // Act
        var result = eventService.GetAll();

        // Assert
        Assert.Equal(3, result.Items.Count);
        Assert.Equal(3, result.TotalCount);
        Assert.Equal(1, result.Page);
        Assert.Equal(10, result.PageSize);
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
        var result = eventService.Get(1);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(1, result.Id);
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
            Id = 1,
            Title = "Updated Event",
            Description = "Updated description",
            StartAt = new DateTime(2024, 5, 21, 14, 00, 00),
            EndAt = new DateTime(2024, 5, 21, 15, 00, 00)
        };

        // Act
        eventService.Update(updatedEvent);
        var result = eventService.Get(1);

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
        Assert.NotNull(eventService.Get(1));

        // Act
        eventService.Delete(1);

        // Assert - попытка получить удалённое событие должна выбросить исключение
        Assert.Throws<NotFoundException>(() => eventService.Get(1));
    }

    /// <summary>
    /// Тест фильтрации событий по названию (фильтр срабатывает при частичном совпадении)
    /// </summary>
    [Fact]
    public void GetAll_FilterByTitle_ShouldReturnMatchingEvents()
    {
        // Arrange
        var eventService = CreateEventService();
        eventService.Add(new Event { Title = "Conference 2024", StartAt = DateTime.Now, EndAt = DateTime.Now.AddHours(1) });
        eventService.Add(new Event { Title = "Workshop on .NET", StartAt = DateTime.Now, EndAt = DateTime.Now.AddHours(1) });
        eventService.Add(new Event { Title = "Conference 2025", StartAt = DateTime.Now, EndAt = DateTime.Now.AddHours(1) });

        // Act
        var result = eventService.GetAll(title: "Conference");

        // Assert
        Assert.Equal(2, result.Items.Count);
        Assert.Equal(2, result.TotalCount);
        Assert.All(result.Items, item => Assert.Contains("Conference", item.Title, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Тест фильтрации по дате начала (from)
    /// </summary>
    [Fact]
    public void GetAll_FilterByStartDate_ShouldReturnEventsAfterDate()
    {
        // Arrange
        var eventService = CreateEventService();
        var date1 = new DateTime(2024, 1, 1, 10, 00, 00);
        var date2 = new DateTime(2024, 6, 15, 10, 00, 00);
        var date3 = new DateTime(2024, 12, 31, 10, 00, 00);

        eventService.Add(new Event { Title = "Event 1", StartAt = date1, EndAt = date1.AddHours(1) });
        eventService.Add(new Event { Title = "Event 2", StartAt = date2, EndAt = date2.AddHours(1) });
        eventService.Add(new Event { Title = "Event 3", StartAt = date3, EndAt = date3.AddHours(1) });

        // Act
        var result = eventService.GetAll(from: new DateTime(2024, 6, 1));

        // Assert
        Assert.Equal(2, result.Items.Count);
        Assert.DoesNotContain(result.Items, e => e.Title == "Event 1");
    }

    /// <summary>
    /// Тест фильтрации по дате окончания (to)
    /// </summary>
    [Fact]
    public void GetAll_FilterByEndDate_ShouldReturnEventsBeforeDate()
    {
        // Arrange
        var eventService = CreateEventService();
        var date1 = new DateTime(2024, 1, 1, 10, 00, 00);
        var date2 = new DateTime(2024, 6, 15, 10, 00, 00);
        var date3 = new DateTime(2024, 12, 31, 10, 00, 00);

        eventService.Add(new Event { Title = "Event 1", StartAt = date1, EndAt = date1.AddHours(1) });
        eventService.Add(new Event { Title = "Event 2", StartAt = date2, EndAt = date2.AddHours(1) });
        eventService.Add(new Event { Title = "Event 3", StartAt = date3, EndAt = date3.AddHours(1) });

        // Act
        var result = eventService.GetAll(to: new DateTime(2024, 6, 30));

        // Assert
        Assert.Equal(2, result.Items.Count);
        Assert.DoesNotContain(result.Items, e => e.Title == "Event 3");
    }

    /// <summary>
    /// Тест пагинации событий
    /// </summary>
    [Fact]
    public void GetAll_WithPagination_ShouldReturnCorrectPage()
    {
        // Arrange
        var eventService = CreateEventService();
        for (int i = 1; i <= 25; i++)
        {
            eventService.Add(new Event
            {
                Title = $"Event {i}",
                StartAt = DateTime.Now.AddDays(i),
                EndAt = DateTime.Now.AddDays(i).AddHours(1)
            });
        }

        // Act - первая страница, по 10 элементов
        var page1 = eventService.GetAll(page: 1, pageSize: 10);
        // Вторая страница
        var page2 = eventService.GetAll(page: 2, pageSize: 10);
        // Третья страница
        var page3 = eventService.GetAll(page: 3, pageSize: 10);

        // Assert
        Assert.Equal(10, page1.Items.Count);
        Assert.Equal(10, page2.Items.Count);
        Assert.Equal(5, page3.Items.Count);
        Assert.Equal(25, page1.TotalCount);
        Assert.Equal(25, page2.TotalCount);
        Assert.Equal(25, page3.TotalCount);
        Assert.Equal(1, page1.Page);
        Assert.Equal(2, page2.Page);
        Assert.Equal(3, page3.Page);
    }

    /// <summary>
    /// Тест комбинированной фильтрации (название + даты + пагинация)
    /// </summary>
    [Fact]
    public void GetAll_CombinedFilters_ShouldReturnFilteredAndPaginatedResults()
    {
        // Arrange
        var eventService = CreateEventService();
        var baseDate = new DateTime(2024, 1, 1);
        for (int i = 0; i < 5; i++)
        {
            eventService.Add(new Event
            {
                Title = "Conference",
                StartAt = baseDate.AddMonths(i),
                EndAt = baseDate.AddMonths(i).AddDays(1)
            });
        }
        for (int i = 0; i < 3; i++)
        {
            eventService.Add(new Event
            {
                Title = "Workshop",
                StartAt = baseDate.AddMonths(i + 6),
                EndAt = baseDate.AddMonths(i + 6).AddDays(1)
            });
        }

        // Act
        var result = eventService.GetAll(
            title: "Conference",
            from: new DateTime(2024, 2, 1),
            to: new DateTime(2024, 5, 1),
            page: 1,
            pageSize: 5
        );

        // Assert
        Assert.True(result.Items.Count > 0);
        Assert.All(result.Items, e => Assert.Equal("Conference", e.Title));
        Assert.All(result.Items, e => Assert.True(e.StartAt >= new DateTime(2024, 2, 1)));
        Assert.All(result.Items, e => Assert.True(e.EndAt <= new DateTime(2024, 5, 1)));
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
        Assert.Throws<NotFoundException>(() => eventService.Get(999));
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
        Assert.Throws<NotFoundException>(() => eventService.Get(1));
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
            Id = 999,
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
        Assert.Throws<NotFoundException>(() => eventService.Delete(999));
    }

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
        var result = eventService.Get(1);

        // Assert
        Assert.NotNull(result);
        // Событие добавлено, но с некорректными датами (валидация должна быть на уровне контроллера через EventDTO)
        Assert.True(result.EndAt < result.StartAt);
    }

    /// <summary>
    /// Тест, что фильтр по названию не находит события, если названия не совпадают
    /// </summary>
    [Fact]
    public void GetAll_FilterByTitle_ShouldReturnEmptyIfNoMatch()
    {
        // Arrange
        var eventService = CreateEventService();
        eventService.Add(new Event { Title = "Conference", StartAt = DateTime.Now, EndAt = DateTime.Now.AddHours(1) });
        eventService.Add(new Event { Title = "Workshop", StartAt = DateTime.Now, EndAt = DateTime.Now.AddHours(1) });

        // Act
        var result = eventService.GetAll(title: "Webinar");

        // Assert
        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
    }

    /// <summary>
    /// Тест, что фильтр по датам не находит события вне диапазона
    /// </summary>
    [Fact]
    public void GetAll_FilterByDateRange_ShouldReturnEmptyIfOutOfRange()
    {
        // Arrange
        var eventService = CreateEventService();
        eventService.Add(new Event { Title = "Event 1", StartAt = new DateTime(2024, 1, 1), EndAt = new DateTime(2024, 1, 2) });
        eventService.Add(new Event { Title = "Event 2", StartAt = new DateTime(2024, 2, 1), EndAt = new DateTime(2024, 2, 2) });

        // Act
        var result = eventService.GetAll(from: new DateTime(2024, 6, 1), to: new DateTime(2024, 12, 31));

        // Assert
        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
    }

    /// <summary>
    /// Тест пагинации с номером страницы больше, чем существует
    /// </summary>
    [Fact]
    public void GetAll_WithPageNumberTooHigh_ShouldReturnEmptyPage()
    {
        // Arrange
        var eventService = CreateEventService();
        for (int i = 1; i <= 5; i++)
        {
            eventService.Add(new Event { Title = $"Event {i}", StartAt = DateTime.Now, EndAt = DateTime.Now.AddHours(1) });
        }

        // Act
        var result = eventService.GetAll(page: 10, pageSize: 5);

        // Assert
        Assert.Empty(result.Items);
        Assert.Equal(5, result.TotalCount);
        Assert.Equal(10, result.Page);
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
            Id = 1,
            Title = "Updated Event",
            StartAt = new DateTime(2024, 5, 21, 15, 00, 00),
            EndAt = new DateTime(2024, 5, 21, 09, 00, 00) // EndAt раньше StartAt
        };

        // Act
        eventService.Update(invalidUpdatedEvent);
        var result = eventService.Get(1);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Updated Event", result.Title);
        Assert.True(result.EndAt < result.StartAt);
    }

    /// <summary>
    /// Тест валидации: page = 0 должен выбросить ValidationException
    /// </summary>
    [Fact]
    public void GetAll_WithPageZero_ShouldThrowValidationException()
    {
        // Arrange
        var eventService = CreateEventService();

        // Act & Assert
        var ex = Assert.Throws<ValidationException>(() => eventService.GetAll(page: 0));
        Assert.Contains("Номер страницы должен быть больше или равен 1", ex.Message);
    }

    /// <summary>
    /// Тест валидации: page < 0 должен выбросить ValidationException
    /// </summary>
    [Fact]
    public void GetAll_WithNegativePage_ShouldThrowValidationException()
    {
        // Arrange
        var eventService = CreateEventService();

        // Act & Assert
        var ex = Assert.Throws<ValidationException>(() => eventService.GetAll(page: -5));
        Assert.Contains("Номер страницы должен быть больше или равен 1", ex.Message);
    }

    /// <summary>
    /// Тест валидации: pageSize = 0 должен выбросить ValidationException
    /// </summary>
    [Fact]
    public void GetAll_WithPageSizeZero_ShouldThrowValidationException()
    {
        // Arrange
        var eventService = CreateEventService();

        // Act & Assert
        var ex = Assert.Throws<ValidationException>(() => eventService.GetAll(pageSize: 0));
        Assert.Contains("Размер страницы должен быть больше или равен 1", ex.Message);
    }

    /// <summary>
    /// Тест валидации: pageSize > 100 должен выбросить ValidationException
    /// </summary>
    [Fact]
    public void GetAll_WithPageSizeExceeding100_ShouldThrowValidationException()
    {
        // Arrange
        var eventService = CreateEventService();

        // Act & Assert
        var ex = Assert.Throws<ValidationException>(() => eventService.GetAll(pageSize: 101));
        Assert.Contains("Размер страницы не может превышать 100", ex.Message);
    }

    #endregion
}
