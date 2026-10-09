using PracticumApi.Models;

namespace PracticumApi.Interfaces;

public interface IBookingService
{
    Task<List<Booking>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<Booking> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Booking> CreateBookingAsync(Guid eventId, CancellationToken cancellationToken = default);
    Task UpdateAsync(Booking booking, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<BookingStatus> ProcessPendingAsync(Guid id, CancellationToken cancellationToken = default);
    Task<BookingStatus> RejectAsync(Guid id, CancellationToken cancellationToken = default);
}
