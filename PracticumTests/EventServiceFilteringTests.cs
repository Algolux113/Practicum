using PracticumApi.Models;

namespace PracticumTests;

public partial class EventServiceIntegrationTests
{
    #region Успешные сценарии

    /// <summary>
    /// Тест получения всех событий
    /// </summary>
    [Fact]
    public async Task GetAll_NoFilters_ShouldReturnAllEvents()
    {
        // Arrange
        var eventService = CreateEventService();
        await eventService.AddAsync(new Event("Event 1") { StartAt = DateTime.Now, EndAt = DateTime.Now.AddHours(1) });
        await eventService.AddAsync(new Event("Event 2") { StartAt = DateTime.Now, EndAt = DateTime.Now.AddHours(1) });
        await eventService.AddAsync(new Event("Event 3") { StartAt = DateTime.Now, EndAt = DateTime.Now.AddHours(1) });

        // Act
        var result = (await eventService.GetAllAsync());

        // Assert
        Assert.Equal(3, result.Items.Count);
        Assert.Equal(3, result.TotalCount);
        Assert.Equal(1, result.Page);
        Assert.Equal(10, result.PageSize);
    }

    /// <summary>
    /// Тест фильтрации событий по названию (фильтр срабатывает при частичном совпадении)
    /// </summary>
    [Fact]
    public async Task GetAll_FilterByTitle_ShouldReturnMatchingEvents()
    {
        // Arrange
        var eventService = CreateEventService();
        await eventService.AddAsync(new Event("Conference 2024") { StartAt = DateTime.Now, EndAt = DateTime.Now.AddHours(1) });
        await eventService.AddAsync(new Event("Workshop on .NET") { StartAt = DateTime.Now, EndAt = DateTime.Now.AddHours(1) });
        await eventService.AddAsync(new Event("Conference 2025") { StartAt = DateTime.Now, EndAt = DateTime.Now.AddHours(1) });

        // Act
        var result = (await eventService.GetAllAsync(title: "Conference"));

        // Assert
        Assert.Equal(2, result.Items.Count);
        Assert.Equal(2, result.TotalCount);
        Assert.All(result.Items, item => Assert.Contains("Conference", item.Title, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Тест фильтрации по дате начала (from)
    /// </summary>
    [Fact]
    public async Task GetAll_FilterByStartDate_ShouldReturnEventsAfterDate()
    {
        // Arrange
        var eventService = CreateEventService();
        var date1 = new DateTime(2024, 1, 1, 10, 00, 00);
        var date2 = new DateTime(2024, 6, 15, 10, 00, 00);
        var date3 = new DateTime(2024, 12, 31, 10, 00, 00);

        await eventService.AddAsync(new Event("Event 1") { StartAt = date1, EndAt = date1.AddHours(1) });
        await eventService.AddAsync(new Event("Event 2") { StartAt = date2, EndAt = date2.AddHours(1) });
        await eventService.AddAsync(new Event("Event 3") { StartAt = date3, EndAt = date3.AddHours(1) });

        // Act
        var result = (await eventService.GetAllAsync(from: new DateTime(2024, 6, 1)));

        // Assert
        Assert.Equal(2, result.Items.Count);
        Assert.DoesNotContain(result.Items, e => e.Title == "Event 1");
    }

    /// <summary>
    /// Тест фильтрации по дате окончания (to)
    /// </summary>
    [Fact]
    public async Task GetAll_FilterByEndDate_ShouldReturnEventsBeforeDate()
    {
        // Arrange
        var eventService = CreateEventService();
        var date1 = new DateTime(2024, 1, 1, 10, 00, 00);
        var date2 = new DateTime(2024, 6, 15, 10, 00, 00);
        var date3 = new DateTime(2024, 12, 31, 10, 00, 00);

        await eventService.AddAsync(new Event("Event 1") { StartAt = date1, EndAt = date1.AddHours(1) });
        await eventService.AddAsync(new Event("Event 2") { StartAt = date2, EndAt = date2.AddHours(1) });
        await eventService.AddAsync(new Event("Event 3") { StartAt = date3, EndAt = date3.AddHours(1) });

        // Act
        var result = (await eventService.GetAllAsync(to: new DateTime(2024, 6, 30)));

        // Assert
        Assert.Equal(2, result.Items.Count);
        Assert.DoesNotContain(result.Items, e => e.Title == "Event 3");
    }

    /// <summary>
    /// Тест комбинированной фильтрации (название + даты + пагинация)
    /// </summary>
    [Fact]
    public async Task GetAll_CombinedFilters_ShouldReturnFilteredAndPaginatedResults()
    {
        // Arrange
        var eventService = CreateEventService();
        var baseDate = new DateTime(2024, 1, 1);
        for (int i = 0; i < 5; i++)
        {
            await eventService.AddAsync(new Event("Conference")
            {
                StartAt = baseDate.AddMonths(i),
                EndAt = baseDate.AddMonths(i).AddDays(1)
            });
        }
        for (int i = 0; i < 3; i++)
        {
            await eventService.AddAsync(new Event("Workshop")
            {
                StartAt = baseDate.AddMonths(i + 6),
                EndAt = baseDate.AddMonths(i + 6).AddDays(1)
            });
        }

        // Act
        var result = (await eventService.GetAllAsync(
            title: "Conference",
            from: new DateTime(2024, 2, 1),
            to: new DateTime(2024, 5, 1),
            page: 1,
            pageSize: 5
        ));

        // Assert
        Assert.True(result.Items.Count > 0);
        Assert.All(result.Items, e => Assert.Equal("Conference", e.Title));
        Assert.All(result.Items, e => Assert.True(e.StartAt >= new DateTime(2024, 2, 1)));
        Assert.All(result.Items, e => Assert.True(e.EndAt <= new DateTime(2024, 5, 1)));
    }

    #endregion

    #region Неуспешные сценарии

    /// <summary>
    /// Тест, что фильтр по названию не находит события, если названия не совпадают
    /// </summary>
    [Fact]
    public async Task GetAll_FilterByTitle_ShouldReturnEmptyIfNoMatch()
    {
        // Arrange
        var eventService = CreateEventService();
        await eventService.AddAsync(new Event("Conference") { StartAt = DateTime.Now, EndAt = DateTime.Now.AddHours(1) });
        await eventService.AddAsync(new Event("Workshop") { StartAt = DateTime.Now, EndAt = DateTime.Now.AddHours(1) });

        // Act
        var result = (await eventService.GetAllAsync(title: "Webinar"));

        // Assert
        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
    }

    /// <summary>
    /// Тест, что фильтр по датам не находит события вне диапазона
    /// </summary>
    [Fact]
    public async Task GetAll_FilterByDateRange_ShouldReturnEmptyIfOutOfRange()
    {
        // Arrange
        var eventService = CreateEventService();
        await eventService.AddAsync(new Event("Event 1") { StartAt = new DateTime(2024, 1, 1), EndAt = new DateTime(2024, 1, 2) });
        await eventService.AddAsync(new Event("Event 2") { StartAt = new DateTime(2024, 2, 1), EndAt = new DateTime(2024, 2, 2) });

        // Act
        var result = (await eventService.GetAllAsync(from: new DateTime(2024, 6, 1), to: new DateTime(2024, 12, 31)));

        // Assert
        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
    }

    #endregion
}
