using Domain.Models;

namespace Application.Interfaces;

public interface IBookingService
{
    Task<Booking> GetAsync(Guid id, Guid userId, UserRole role, CancellationToken cancellationToken = default);
    Task<Booking> CreateBookingAsync(Guid eventId, Guid userId, CancellationToken cancellationToken = default);
    Task<BookingStatus> CancelAsync(Guid id, Guid userId, UserRole role, CancellationToken cancellationToken = default);
    Task<BookingStatus> ProcessPendingAsync(Guid id, CancellationToken cancellationToken = default);
    Task<BookingStatus> RejectAsync(Guid id, CancellationToken cancellationToken = default);
    Task<BookingStatus> RejectPendingAsync(Guid id, CancellationToken cancellationToken = default);
}
