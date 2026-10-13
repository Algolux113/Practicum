using PracticumApi.Exceptions;
using PracticumApi.Interfaces;
using PracticumApi.Models;

namespace PracticumApi.Services;

public class EventService(IEventRepository repository) : IEventService
{
    private readonly IEventRepository _repository = repository;

    public async Task<PaginatedResult<Event>> GetAllAsync(
        string? title = null, DateTime? from = null, DateTime? to = null,
        int page = 1, int pageSize = 10, CancellationToken cancellationToken = default)
    {
        if (page < 1)
            throw new ValidationException(nameof(page), "Номер страницы должен быть больше или равен 1");
        if (pageSize < 1)
            throw new ValidationException(nameof(pageSize), "Размер страницы должен быть больше или равен 1");
        if (pageSize > 100)
            throw new ValidationException(nameof(pageSize), "Размер страницы не может превышать 100");

        var offset = ((long)page - 1) * pageSize;
        if (offset > int.MaxValue)
            throw new ValidationException(nameof(page), "Номер страницы слишком велик.");

        from = from.HasValue ? UtcDateTime.Normalize(from.Value) : null;
        to = to.HasValue ? UtcDateTime.Normalize(to.Value) : null;
        var (items, totalCount) = await _repository.GetPageAsync(
            title, from, to, (int)offset, pageSize, cancellationToken);
        return new PaginatedResult<Event>(items.ToList(), totalCount, page, pageSize);
    }

    public async Task<Event> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        await _repository.GetAsync(id, cancellationToken: cancellationToken)
        ?? throw new NotFoundException("Event", id);

    public async Task AddAsync(Event eventItem, CancellationToken cancellationToken = default)
    {
        NormalizeDates(eventItem);
        eventItem.Id = Guid.NewGuid();
        await _repository.AddAsync(eventItem, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(Event eventItem, CancellationToken cancellationToken = default)
    {
        NormalizeDates(eventItem);
        var title = eventItem.Title;
        var description = eventItem.Description;
        var startAt = eventItem.StartAt;
        var endAt = eventItem.EndAt;
        var totalSeats = eventItem.TotalSeats;
        await EventWriteSynchronization.Gate.WaitAsync(cancellationToken);
        try
        {
            var existing = await GetTrackedAsync(eventItem.Id, cancellationToken);
            var availableSeats = existing.RecalculateAvailableSeats(totalSeats);

            existing.Title = title;
            existing.Description = description;
            existing.StartAt = startAt;
            existing.EndAt = endAt;
            existing.TotalSeats = totalSeats;
            existing.AvailableSeats = availableSeats;

            await _repository.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            EventWriteSynchronization.Gate.Release();
        }
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await EventWriteSynchronization.Gate.WaitAsync(cancellationToken);
        try
        {
            var eventItem = await GetTrackedAsync(id, cancellationToken);
            eventItem.IsDeleted = true;
            await _repository.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            EventWriteSynchronization.Gate.Release();
        }
    }

    public async Task ReserveSeatsAsync(Guid id, int count = 1, CancellationToken cancellationToken = default)
    {
        await EventWriteSynchronization.Gate.WaitAsync(cancellationToken);
        try
        {
            var eventItem = await GetTrackedAsync(id, cancellationToken);
            if (!eventItem.TryReserveSeats(count))
                throw new NoAvailableSeatsException();
            await _repository.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            EventWriteSynchronization.Gate.Release();
        }
    }

    public async Task ReleaseSeatsAsync(Guid id, int count = 1, CancellationToken cancellationToken = default)
    {
        await EventWriteSynchronization.Gate.WaitAsync(cancellationToken);
        try
        {
            var eventItem = await GetTrackedAsync(id, cancellationToken);
            eventItem.ReleaseSeats(count);
            await _repository.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            EventWriteSynchronization.Gate.Release();
        }
    }

    private async Task<Event> GetTrackedAsync(Guid id, CancellationToken cancellationToken) =>
        await _repository.GetTrackedAsync(id, cancellationToken: cancellationToken)
        ?? throw new NotFoundException("Event", id);

    private static void NormalizeDates(Event eventItem)
    {
        eventItem.StartAt = UtcDateTime.Normalize(eventItem.StartAt);
        eventItem.EndAt = UtcDateTime.Normalize(eventItem.EndAt);
    }
}
