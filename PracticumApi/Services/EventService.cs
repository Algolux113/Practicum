using Microsoft.EntityFrameworkCore;
using PracticumApi.DataAccess;
using PracticumApi.Interfaces;
using PracticumApi.Models;
using PracticumApi.Exceptions;

namespace PracticumApi.Services;

public class EventService(AppDbContext context) : IEventService
{
    private readonly AppDbContext _context = context;

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
        var query = _context.Events.AsNoTracking().Where(e => !e.IsDeleted);
        if (!string.IsNullOrWhiteSpace(title))
        {
            var search = title.ToLower();
            query = query.Where(e => e.Title != null && e.Title.ToLower().Contains(search));
        }
        if (from.HasValue)
            query = query.Where(e => e.StartAt >= from.Value);
        if (to.HasValue)
            query = query.Where(e => e.EndAt <= to.Value);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query.OrderBy(e => e.StartAt).ThenBy(e => e.Id)
            .Skip((int)offset).Take(pageSize).ToListAsync(cancellationToken);
        return new PaginatedResult<Event>(items, totalCount, page, pageSize);
    }

    public async Task<Event> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        await _context.Events.AsNoTracking().SingleOrDefaultAsync(e => e.Id == id && !e.IsDeleted, cancellationToken)
        ?? throw new NotFoundException("Event", id);

    public async Task AddAsync(Event eventItem, CancellationToken cancellationToken = default)
    {
        NormalizeDates(eventItem);
        eventItem.Id = Guid.NewGuid();
        await _context.Events.AddAsync(eventItem, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(Event eventItem, CancellationToken cancellationToken = default)
    {
        NormalizeDates(eventItem);
        await EventWriteSynchronization.Gate.WaitAsync(cancellationToken);
        try
        {
            var values = _context.Entry(eventItem).CurrentValues.Clone();
            var totalSeats = eventItem.TotalSeats;
            var existing = await GetTrackedAsync(eventItem.Id, cancellationToken);
            // Пересчитываем места по свежим данным внутри той же секции, что и бронирование.
            var availableSeats = existing.RecalculateAvailableSeats(totalSeats);
            _context.Entry(existing).CurrentValues.SetValues(values);
            existing.AvailableSeats = availableSeats;
            existing.IsDeleted = false;
            await _context.SaveChangesAsync(cancellationToken);
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
            await _context.SaveChangesAsync(cancellationToken);
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
            await _context.SaveChangesAsync(cancellationToken);
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
            await _context.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            EventWriteSynchronization.Gate.Release();
        }
    }

    private async Task<Event> GetTrackedAsync(Guid id, CancellationToken cancellationToken)
    {
        var current = await GetAsync(id, cancellationToken);
        var tracked = _context.Events.Local.FirstOrDefault(e => e.Id == id);
        if (tracked is null)
        {
            _context.Events.Attach(current);
            return current;
        }
        _context.Entry(tracked).CurrentValues.SetValues(current);
        return tracked;
    }

    private static void NormalizeDates(Event eventItem)
    {
        eventItem.StartAt = UtcDateTime.Normalize(eventItem.StartAt);
        eventItem.EndAt = UtcDateTime.Normalize(eventItem.EndAt);
    }
}
