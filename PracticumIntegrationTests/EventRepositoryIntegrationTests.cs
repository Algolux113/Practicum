using Microsoft.EntityFrameworkCore;
using PracticumApi.DataAccess.Repositories;
using PracticumApi.Models;

namespace PracticumIntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class EventRepositoryIntegrationTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task AddAsyncAndSaveChangesAsync_PersistEvent()
    {
        // Arrange
        await using var database = await fixture.CreateDatabaseAsync();
        var repository = new EventRepository(database.Context);
        var eventItem = CreateEvent("Событие для сохранения");

        // Act
        await repository.AddAsync(eventItem);
        Assert.Equal(EntityState.Added, database.Context.Entry(eventItem).State);
        await repository.SaveChangesAsync();

        // Assert
        var saved = await repository.GetAsync(eventItem.Id);
        Assert.NotNull(saved);
        Assert.Equal(eventItem.Title, saved.Title);
        Assert.Equal(eventItem.TotalSeats, saved.TotalSeats);
    }

    [Fact]
    public async Task GetPageAsync_FiltersSoftDeletedTitleAndDateThenAppliesPaging()
    {
        // Arrange
        await using var database = await fixture.CreateDatabaseAsync();
        var repository = new EventRepository(database.Context);
        var first = CreateEvent("Alpha: первый", UtcDate(10));
        var second = CreateEvent("ALPHA: второй", UtcDate(11));
        var outsideRange = CreateEvent("Alpha: раньше", UtcDate(9));
        var deleted = CreateEvent("Alpha: удалён", UtcDate(10));
        deleted.IsDeleted = true;
        await AddEventsAsync(repository, first, second, outsideRange, deleted, CreateEvent("Beta", UtcDate(10)));

        // Act
        var result = await repository.GetPageAsync(
            title: "alpha", from: UtcDate(10), to: UtcDate(11).AddDays(1), skip: 1, take: 1);

        // Assert
        Assert.Equal(2, result.TotalCount);
        var item = Assert.Single(result.Items);
        Assert.Equal(second.Id, item.Id);
    }

    [Fact]
    public async Task GetPageAsync_WithoutFilters_ReturnsAllVisibleEventsInOrder()
    {
        // Arrange
        await using var database = await fixture.CreateDatabaseAsync();
        var repository = new EventRepository(database.Context);
        var later = CreateEvent("Позднее событие", UtcDate(11));
        var earlier = CreateEvent("Раннее событие", UtcDate(9));
        var deleted = CreateEvent("Скрытое событие", UtcDate(10));
        deleted.IsDeleted = true;
        await AddEventsAsync(repository, later, deleted, earlier);

        // Act
        var result = await repository.GetPageAsync(
            title: null, from: null, to: null, skip: 0, take: 10);

        // Assert
        Assert.Equal(2, result.TotalCount);
        Assert.Equal(new[] { earlier.Id, later.Id }, result.Items.Select(item => item.Id));
    }

    [Fact]
    public async Task GetPageAsync_WithTitleOnly_FiltersCaseInsensitivelyBySubstring()
    {
        // Arrange
        await using var database = await fixture.CreateDatabaseAsync();
        var repository = new EventRepository(database.Context);
        var matching = CreateEvent("ТЕХНОЛОГИЧЕСКАЯ конференция");
        var other = CreateEvent("Музыкальный вечер");
        await AddEventsAsync(repository, matching, other);

        // Act
        var result = await repository.GetPageAsync(
            title: "конферен", from: null, to: null, skip: 0, take: 10);

        // Assert
        Assert.Equal(1, result.TotalCount);
        Assert.Equal(matching.Id, Assert.Single(result.Items).Id);
    }

    [Fact]
    public async Task GetPageAsync_WithFromOnly_IncludesEventsAtLowerBound()
    {
        // Arrange
        await using var database = await fixture.CreateDatabaseAsync();
        var repository = new EventRepository(database.Context);
        var before = CreateEvent("До границы", UtcDate(9));
        var onBoundary = CreateEvent("На границе", UtcDate(10));
        var after = CreateEvent("После границы", UtcDate(11));
        await AddEventsAsync(repository, before, onBoundary, after);

        // Act
        var result = await repository.GetPageAsync(
            title: null, from: UtcDate(10), to: null, skip: 0, take: 10);

        // Assert
        Assert.Equal(new[] { onBoundary.Id, after.Id }, result.Items.Select(item => item.Id));
    }

    [Fact]
    public async Task GetPageAsync_WithToOnly_IncludesEventsAtUpperBound()
    {
        // Arrange
        await using var database = await fixture.CreateDatabaseAsync();
        var repository = new EventRepository(database.Context);
        var before = CreateEvent("До границы", UtcDate(9));
        var onBoundary = CreateEvent("На границе", UtcDate(10));
        var after = CreateEvent("После границы", UtcDate(11));
        await AddEventsAsync(repository, before, onBoundary, after);

        // Act
        var result = await repository.GetPageAsync(
            title: null, from: null, to: UtcDate(10).AddHours(2), skip: 0, take: 10);

        // Assert
        Assert.Equal(new[] { before.Id, onBoundary.Id }, result.Items.Select(item => item.Id));
    }

    [Fact]
    public async Task GetPageAsync_AppliesSkipAndTakeAndReturnsEmptyPagePastEnd()
    {
        // Arrange
        await using var database = await fixture.CreateDatabaseAsync();
        var repository = new EventRepository(database.Context);
        var first = CreateEvent("Первое", UtcDate(9));
        var second = CreateEvent("Второе", UtcDate(10));
        var third = CreateEvent("Третье", UtcDate(11));
        await AddEventsAsync(repository, third, first, second);

        // Act
        var firstPage = await repository.GetPageAsync(
            title: null, from: null, to: null, skip: 0, take: 2);
        var pagePastEnd = await repository.GetPageAsync(
            title: null, from: null, to: null, skip: 10, take: 2);

        // Assert
        Assert.Equal(3, firstPage.TotalCount);
        Assert.Equal(new[] { first.Id, second.Id }, firstPage.Items.Select(item => item.Id));
        Assert.Equal(3, pagePastEnd.TotalCount);
        Assert.Empty(pagePastEnd.Items);
    }

    [Fact]
    public async Task GetAsync_ExcludesDeletedByDefaultAndCanIncludeIt()
    {
        // Arrange
        await using var database = await fixture.CreateDatabaseAsync();
        var repository = new EventRepository(database.Context);
        var eventItem = CreateEvent("Удалённое событие");
        eventItem.IsDeleted = true;
        await AddEventsAsync(repository, eventItem);

        // Act
        var hidden = await repository.GetAsync(eventItem.Id);
        var included = await repository.GetAsync(eventItem.Id, includeDeleted: true);
        var missing = await repository.GetAsync(Guid.NewGuid());

        // Assert
        Assert.Null(hidden);
        Assert.Equal(eventItem.Id, included?.Id);
        Assert.Null(missing);
    }

    [Fact]
    public async Task GetTrackedAsync_AttachesAndRefreshesTrackedEvent()
    {
        // Arrange
        await using var database = await fixture.CreateDatabaseAsync();
        var repository = new EventRepository(database.Context);
        var eventItem = CreateEvent("До обновления");
        await AddEventsAsync(repository, eventItem);

        // Act
        var tracked = await repository.GetTrackedAsync(eventItem.Id);

        // Assert
        Assert.NotNull(tracked);
        Assert.Same(tracked, await repository.GetTrackedAsync(eventItem.Id));
        Assert.Equal(EntityState.Unchanged, database.Context.Entry(tracked).State);

        await using var otherContext = database.CreateContext();
        var stored = await otherContext.Events.SingleAsync(e => e.Id == eventItem.Id);
        stored.Title = "После обновления";
        await otherContext.SaveChangesAsync();

        // Act
        var refreshed = await repository.GetTrackedAsync(eventItem.Id);

        // Assert
        Assert.Same(tracked, refreshed);
        Assert.Equal("После обновления", refreshed?.Title);
    }

    [Fact]
    public async Task SaveChangesAsync_PersistsTrackedEventUpdate()
    {
        // Arrange
        await using var database = await fixture.CreateDatabaseAsync();
        var repository = new EventRepository(database.Context);
        var eventItem = CreateEvent("Исходное название");
        await AddEventsAsync(repository, eventItem);

        // Act
        var tracked = await repository.GetTrackedAsync(eventItem.Id);
        Assert.NotNull(tracked);
        tracked.Title = "Обновлённое название";
        tracked.Description = "Обновлённое описание";
        await repository.SaveChangesAsync();

        // Assert
        var saved = await repository.GetAsync(eventItem.Id);
        Assert.Equal("Обновлённое название", saved?.Title);
        Assert.Equal("Обновлённое описание", saved?.Description);
    }

    [Fact]
    public async Task SaveChangesAsync_PersistsSoftDeletionAndQueriesCanIncludeDeletedEvent()
    {
        // Arrange
        await using var database = await fixture.CreateDatabaseAsync();
        var repository = new EventRepository(database.Context);
        var eventItem = CreateEvent("Событие для мягкого удаления");
        await AddEventsAsync(repository, eventItem);

        // Act
        var tracked = await repository.GetTrackedAsync(eventItem.Id);
        Assert.NotNull(tracked);
        tracked.IsDeleted = true;
        await repository.SaveChangesAsync();

        // Assert
        var hidden = await repository.GetAsync(eventItem.Id);
        var included = await repository.GetAsync(eventItem.Id, includeDeleted: true);
        Assert.Null(hidden);
        Assert.True(included?.IsDeleted);
    }

    [Fact]
    public async Task Detach_RemovesEventFromContextWithoutSavingIt()
    {
        // Arrange
        await using var database = await fixture.CreateDatabaseAsync();
        var repository = new EventRepository(database.Context);
        var eventItem = CreateEvent("Отсоединённое событие");

        // Act
        await repository.AddAsync(eventItem);
        repository.Detach(eventItem);
        await repository.SaveChangesAsync();

        // Assert
        Assert.Equal(EntityState.Detached, database.Context.Entry(eventItem).State);
        Assert.Null(await repository.GetAsync(eventItem.Id));
    }

    private static async Task AddEventsAsync(EventRepository repository, params Event[] events)
    {
        foreach (var eventItem in events)
            await repository.AddAsync(eventItem);
        await repository.SaveChangesAsync();
    }

    private static Event CreateEvent(string title, DateTime? startAt = null)
    {
        var actualStartAt = startAt ?? UtcDate(10);
        var eventItem = Event.Create(title, "Описание", actualStartAt, actualStartAt.AddHours(2), 25);
        eventItem.Id = Guid.NewGuid();
        return eventItem;
    }

    private static DateTime UtcDate(int day) =>
        new(2026, 1, day, 12, 0, 0, DateTimeKind.Utc);
}
