using Domain.Models;

namespace Application.Interfaces;

public interface IBookingService
{
    Task<Booking> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Booking> CreateBookingAsync(Guid eventId, CancellationToken cancellationToken = default);
    Task<BookingStatus> ProcessPendingAsync(Guid id, CancellationToken cancellationToken = default);
    Task<BookingStatus> RejectAsync(Guid id, CancellationToken cancellationToken = default);
}
