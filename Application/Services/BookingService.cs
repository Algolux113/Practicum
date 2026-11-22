using Domain.Exceptions;
using Application.Interfaces;
using Domain.Models;

namespace Application.Services;

public class BookingService(
    IBookingRepository bookingRepository,
    IEventRepository eventRepository,
    IUserRepository userRepository,
    IUnitOfWork unitOfWork) : IBookingService
{
    private const int ActiveBookingLimit = 10;

    public async Task<Booking> GetAsync(Guid id, Guid userId, UserRole role, CancellationToken cancellationToken = default)
    {
        var booking = await bookingRepository.GetAsync(id, cancellationToken)
            ?? throw new NotFoundException("Booking", id);
        EnsureAccess(booking, userId, role);
        return booking;
    }

    public Task<Booking> CreateBookingAsync(Guid eventId, Guid userId, CancellationToken cancellationToken = default) =>
        unitOfWork.ExecuteAsync(async () =>
        {
            var eventItem = await eventRepository.GetForUpdateAsync(eventId, cancellationToken: cancellationToken)
                ?? throw new NotFoundException("Event", eventId);
            if (eventItem.StartAt <= DateTime.UtcNow)
                throw new PastEventBookingException();

            var booking = Booking.Create(eventId, userId);
            if (!await userRepository.LockUserForBookingAsync(userId, cancellationToken))
                throw new NotFoundException("User", userId);
            if (await bookingRepository.CountActiveAsync(userId, cancellationToken) >= ActiveBookingLimit)
                throw new ActiveBookingLimitExceededException(ActiveBookingLimit);
            if (!eventItem.TryReserveSeats())
                throw new NoAvailableSeatsException();

            await bookingRepository.AddAsync(booking, cancellationToken);
            return booking;
        }, cancellationToken);

    public Task<BookingStatus> CancelAsync(
        Guid id, Guid userId, UserRole role, CancellationToken cancellationToken = default) =>
        unitOfWork.ExecuteAsync(async () =>
        {
            var booking = await GetForUpdateAsync(id, cancellationToken);
            EnsureAccess(booking, userId, role);
            if (booking.Status == BookingStatus.Cancelled)
                return booking.Status;

            booking.Cancel();
            var eventItem = await eventRepository.GetForUpdateAsync(
                booking.EventId, includeDeleted: true, cancellationToken: cancellationToken);
            eventItem?.ReleaseSeats();
            return booking.Status;
        }, cancellationToken);

    public Task<BookingStatus> ProcessPendingAsync(Guid id, CancellationToken cancellationToken = default) =>
        TransitionAsync(id, reject: false, cancellationToken);

    public Task<BookingStatus> RejectAsync(Guid id, CancellationToken cancellationToken = default) =>
        TransitionAsync(id, reject: true, cancellationToken, allowConfirmedRejection: true);

    public Task<BookingStatus> RejectPendingAsync(Guid id, CancellationToken cancellationToken = default) =>
        TransitionAsync(id, reject: true, cancellationToken);

    private Task<BookingStatus> TransitionAsync(
        Guid id, bool reject, CancellationToken cancellationToken, bool allowConfirmedRejection = false) =>
        unitOfWork.ExecuteAsync(async () =>
        {
            var booking = await GetForUpdateAsync(id, cancellationToken);
            // Повторная обработка не должна повторно возвращать место или подтверждать отказ.
            if (booking.Status is BookingStatus.Rejected or BookingStatus.Cancelled ||
                (!allowConfirmedRejection && booking.Status != BookingStatus.Pending))
                return booking.Status;

            var eventItem = await eventRepository.GetForUpdateAsync(
                booking.EventId, includeDeleted: true, cancellationToken: cancellationToken);
            if (reject || eventItem is null || eventItem.IsDeleted)
            {
                booking.Reject();
                eventItem?.ReleaseSeats();
            }
            else
            {
                booking.Confirm();
            }
            return booking.Status;
        }, cancellationToken);

    private async Task<Booking> GetForUpdateAsync(Guid id, CancellationToken cancellationToken) =>
        await bookingRepository.GetForUpdateAsync(id, cancellationToken)
        ?? throw new NotFoundException("Booking", id);

    private static void EnsureAccess(Booking booking, Guid userId, UserRole role)
    {
        if (userId == Guid.Empty || (role != UserRole.Admin && booking.UserId != userId))
            throw new ForbiddenOperationException();
    }
}
