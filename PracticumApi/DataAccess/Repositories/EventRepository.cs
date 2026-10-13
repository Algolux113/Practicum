using Microsoft.EntityFrameworkCore;
using PracticumApi.DataAccess;
using PracticumApi.Interfaces;
using PracticumApi.Models;

namespace PracticumApi.DataAccess.Repositories;

public class EventRepository(AppDbContext context) : IEventRepository
{
    private readonly AppDbContext _context = context;

    public async Task<(IReadOnlyList<Event> Items, int TotalCount)> GetPageAsync(
        string? title, DateTime? from, DateTime? to, int skip, int take,
        CancellationToken cancellationToken = default)
    {
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
            .Skip(skip).Take(take).ToListAsync(cancellationToken);
        return (items, totalCount);
    }

    public Task<Event?> GetAsync(
        Guid id, bool includeDeleted = false, CancellationToken cancellationToken = default)
    {
        var query = _context.Events.AsNoTracking().Where(e => e.Id == id);
        if (!includeDeleted)
            query = query.Where(e => !e.IsDeleted);
        return query.SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<Event?> GetTrackedAsync(
        Guid id, bool includeDeleted = false, CancellationToken cancellationToken = default)
    {
        var current = await GetAsync(id, includeDeleted, cancellationToken);
        if (current is null)
            return null;

        var tracked = _context.Events.Local.FirstOrDefault(e => e.Id == id);
        if (tracked is null)
        {
            _context.Events.Attach(current);
            return current;
        }

        var entry = _context.Entry(tracked);
        entry.CurrentValues.SetValues(current);
        entry.OriginalValues.SetValues(current);
        entry.State = EntityState.Unchanged;
        return tracked;
    }

    public async Task AddAsync(Event eventItem, CancellationToken cancellationToken = default) =>
        await _context.Events.AddAsync(eventItem, cancellationToken);

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        await _context.SaveChangesAsync(cancellationToken);

    public void Detach(Event eventItem) => _context.Entry(eventItem).State = EntityState.Detached;
}
