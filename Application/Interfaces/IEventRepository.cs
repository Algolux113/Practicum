using Domain.Models;

namespace Application.Interfaces;

public interface IEventRepository
{
    Task<(IReadOnlyList<Event> Items, int TotalCount)> GetPageAsync(
        string? title, DateTime? from, DateTime? to, int skip, int take,
        CancellationToken cancellationToken = default);
    Task<Event?> GetAsync(Guid id, bool includeDeleted = false, CancellationToken cancellationToken = default);
    Task<Event?> GetForUpdateAsync(Guid id, bool includeDeleted = false, CancellationToken cancellationToken = default);
    Task AddAsync(Event eventItem, CancellationToken cancellationToken = default);
}
