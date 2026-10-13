using PracticumApi.Models;

namespace PracticumApi.Interfaces;

public interface IBookingRepository
{
    Task<List<Booking>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<List<Booking>> GetPendingAsync(CancellationToken cancellationToken = default);
    Task<Booking?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Booking?> GetTrackedAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddAsync(Booking booking, CancellationToken cancellationToken = default);
    void Delete(Booking booking);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
    void Detach(Booking booking);
}
