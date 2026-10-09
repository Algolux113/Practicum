using PracticumApi.Models;

namespace PracticumApi.Interfaces;

public interface IEventService
{
    Task<PaginatedResult<Event>> GetAllAsync(
        string? title = null, DateTime? from = null, DateTime? to = null,
        int page = 1, int pageSize = 10, CancellationToken cancellationToken = default);
    Task<Event> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddAsync(Event eventItem, CancellationToken cancellationToken = default);
    Task UpdateAsync(Event eventItem, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task ReserveSeatsAsync(Guid id, int count = 1, CancellationToken cancellationToken = default);
    Task ReleaseSeatsAsync(Guid id, int count = 1, CancellationToken cancellationToken = default);
}
