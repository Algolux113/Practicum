using Domain.Exceptions;
using Application.Interfaces;
using Domain.Models;

namespace Application.Services;

public class BookingService(
    IBookingRepository bookingRepository,
    IEventRepository eventRepository,
    IUnitOfWork unitOfWork) : IBookingService
{
    public async Task<Booking> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        await bookingRepository.GetAsync(id, cancellationToken)
        ?? throw new NotFoundException("Booking", id);

    public Task<Booking> CreateBookingAsync(Guid eventId, CancellationToken cancellationToken = default) =>
        unitOfWork.ExecuteAsync(async () =>
        {
            var eventItem = await eventRepository.GetForUpdateAsync(eventId, cancellationToken: cancellationToken)
                ?? throw new NotFoundException("Event", eventId);
            if (!eventItem.TryReserveSeats())
                throw new NoAvailableSeatsException();

            var booking = Booking.Create(eventId);
            await bookingRepository.AddAsync(booking, cancellationToken);
            return booking;
        }, cancellationToken);

    public Task<BookingStatus> ProcessPendingAsync(Guid id, CancellationToken cancellationToken = default) =>
        TransitionAsync(id, reject: false, cancellationToken);

    public Task<BookingStatus> RejectAsync(Guid id, CancellationToken cancellationToken = default) =>
        TransitionAsync(id, reject: true, cancellationToken);

    private Task<BookingStatus> TransitionAsync(Guid id, bool reject, CancellationToken cancellationToken) =>
        unitOfWork.ExecuteAsync(async () =>
        {
            var booking = await GetForUpdateAsync(id, cancellationToken);
            // Повторная обработка не должна повторно возвращать место или подтверждать отказ.
            if (booking.Status == BookingStatus.Rejected ||
                (!reject && booking.Status != BookingStatus.Pending))
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
}
