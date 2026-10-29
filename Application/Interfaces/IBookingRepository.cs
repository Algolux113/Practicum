using Domain.Models;

namespace Application.Interfaces;

public interface IBookingRepository
{
    Task<List<Booking>> GetPendingAsync(CancellationToken cancellationToken = default);
    Task<Booking?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Booking?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddAsync(Booking booking, CancellationToken cancellationToken = default);
}
