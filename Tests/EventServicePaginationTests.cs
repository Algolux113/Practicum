using Domain.Models;
using Domain.Exceptions;

namespace Tests;

public partial class EventServiceIntegrationTests
{
    #region Успешные сценарии

    /// <summary>
    /// Тест пагинации событий
    /// </summary>
    [Fact]
    public async Task GetAll_WithPagination_ShouldReturnCorrectPage()
    {
        // Arrange
        var eventService = CreateEventService();
        for (int i = 1; i <= 25; i++)
        {
            await eventService.CreateAsync((Event.Create($"Event {i}", null, DateTime.Now.AddDays(i), DateTime.Now.AddDays(i).AddHours(1), 100)).ToDto());
        }

        // Act - первая страница, по 10 элементов
        var page1 = (await eventService.GetAllAsync(page: 1, pageSize: 10));
        // Вторая страница
        var page2 = (await eventService.GetAllAsync(page: 2, pageSize: 10));
        // Третья страница
        var page3 = (await eventService.GetAllAsync(page: 3, pageSize: 10));

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

    #endregion

    #region Неуспешные сценарии

    /// <summary>
    /// Тест: переполнение смещения пагинации выбрасывает ValidationException
    /// </summary>
    [Fact]
    public async Task GetAll_WithOverflowingOffset_ShouldThrowValidationException()
    {
        // Arrange
        var service = CreateEventService();

        // Act
        Func<Task> action = () => service.GetAllAsync(page: int.MaxValue, pageSize: 100);

        // Assert
        await Assert.ThrowsAsync<ValidationException>(action);
    }

    /// <summary>
    /// Тест пагинации с номером страницы больше, чем существует
    /// </summary>
    [Fact]
    public async Task GetAll_WithPageNumberTooHigh_ShouldReturnEmptyPage()
    {
        // Arrange
        var eventService = CreateEventService();
        for (int i = 1; i <= 5; i++)
        {
            await eventService.CreateAsync((Event.Create($"Event {i}", null, DateTime.Now, DateTime.Now.AddHours(1), 100)).ToDto());
        }

        // Act
        var result = (await eventService.GetAllAsync(page: 10, pageSize: 5));

        // Assert
        Assert.Empty(result.Items);
        Assert.Equal(5, result.TotalCount);
        Assert.Equal(10, result.Page);
    }

    /// <summary>
    /// Тест валидации: page = 0 должен выбросить ValidationException
    /// </summary>
    [Fact]
    public async Task GetAll_WithPageZero_ShouldThrowValidationException()
    {
        // Arrange
        var eventService = CreateEventService();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ValidationException>(async () => await eventService.GetAllAsync(page: 0));
        Assert.Contains("Номер страницы должен быть больше или равен 1", ex.Message);
    }

    /// <summary>
    /// Тест валидации: page < 0 должен выбросить ValidationException
    /// </summary>
    [Fact]
    public async Task GetAll_WithNegativePage_ShouldThrowValidationException()
    {
        // Arrange
        var eventService = CreateEventService();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ValidationException>(async () => await eventService.GetAllAsync(page: -5));
        Assert.Contains("Номер страницы должен быть больше или равен 1", ex.Message);
    }

    /// <summary>
    /// Тест валидации: pageSize = 0 должен выбросить ValidationException
    /// </summary>
    [Fact]
    public async Task GetAll_WithPageSizeZero_ShouldThrowValidationException()
    {
        // Arrange
        var eventService = CreateEventService();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ValidationException>(async () => await eventService.GetAllAsync(pageSize: 0));
        Assert.Contains("Размер страницы должен быть больше или равен 1", ex.Message);
    }

    /// <summary>
    /// Тест валидации: pageSize > 100 должен выбросить ValidationException
    /// </summary>
    [Fact]
    public async Task GetAll_WithPageSizeExceeding100_ShouldThrowValidationException()
    {
        // Arrange
        var eventService = CreateEventService();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ValidationException>(async () => await eventService.GetAllAsync(pageSize: 101));
        Assert.Contains("Размер страницы не может превышать 100", ex.Message);
    }

    #endregion
}
