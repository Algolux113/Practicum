using PracticumApi.Interfaces;
using PracticumApi.Services;

namespace PracticumTests;

/// <summary>
/// Интеграционные тесты для EventService с использованием реальной реализации.
/// Класс разбит на несколько файлов (partial):
/// EventServiceCrudTests.cs — Add/Get/Update/Delete,
/// EventServiceFilteringTests.cs — фильтрация по названию и датам,
/// EventServicePaginationTests.cs — пагинация и её валидация,
/// EventServiceDateHandlingTests.cs — поведение при некорректных датах.
/// </summary>
public partial class EventServiceIntegrationTests
{
    private IEventService CreateEventService() => new EventService();
}
