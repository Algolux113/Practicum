using Domain.Models;
using Application.DTOs;

namespace Application.Interfaces;

public interface IEventService
{
    Task<PaginatedResult<Event>> GetAllAsync(
        string? title = null, DateTime? from = null, DateTime? to = null,
        int page = 1, int pageSize = 10, CancellationToken cancellationToken = default);
    Task<Event> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Event> CreateAsync(EventDTO dto, CancellationToken cancellationToken = default);
    Task UpdateAsync(Guid id, EventDTO dto, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
