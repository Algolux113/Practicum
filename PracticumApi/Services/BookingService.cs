using PracticumApi.Exceptions;
using PracticumApi.Interfaces;
using PracticumApi.Models;

namespace PracticumApi.Services;

/// <summary>
/// Сервис бронирований с сохранением брони и резерва места в одном контексте.
/// </summary>
public class BookingService(
    IBookingRepository bookingRepository,
    IEventRepository eventRepository) : IBookingService
{
    private readonly IBookingRepository _bookingRepository = bookingRepository;
    private readonly IEventRepository _eventRepository = eventRepository;

    public Task<List<Booking>> GetAllAsync(CancellationToken cancellationToken = default) =>
        _bookingRepository.GetAllAsync(cancellationToken);

    public async Task<Booking> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        await _bookingRepository.GetAsync(id, cancellationToken)
        ?? throw new NotFoundException("Booking", id);

    public async Task<Booking> CreateBookingAsync(Guid eventId, CancellationToken cancellationToken = default)
    {
        var booking = new Booking(eventId)
        {
            Id = Guid.NewGuid(),
            Status = BookingStatus.Pending,
            CreatedAt = DateTime.UtcNow,
        };

        await EventWriteSynchronization.Gate.WaitAsync(cancellationToken);
        Event? eventItem = null;
        try
        {
            eventItem = await _eventRepository.GetTrackedAsync(eventId, cancellationToken: cancellationToken)
                ?? throw new NotFoundException("Event", eventId);

            if (!eventItem.TryReserveSeats())
                throw new NoAvailableSeatsException();

            await _bookingRepository.AddAsync(booking, cancellationToken);
            // Оба репозитория используют один scoped AppDbContext, поэтому место и бронь
            // сохраняются одной транзакцией.
            await _bookingRepository.SaveChangesAsync(cancellationToken);
            return booking;
        }
        catch
        {
            _bookingRepository.Detach(booking);
            if (eventItem is not null)
                _eventRepository.Detach(eventItem);
            throw;
        }
        finally
        {
            EventWriteSynchronization.Gate.Release();
        }
    }

    public async Task UpdateAsync(Booking booking, CancellationToken cancellationToken = default)
    {
        var eventId = booking.EventId;
        var status = booking.Status;
        var createdAt = booking.CreatedAt;
        var processedAt = booking.ProcessedAt;
        var existing = await GetTrackedAsync(booking.Id, cancellationToken);
        existing.EventId = eventId;
        existing.Status = status;
        existing.CreatedAt = createdAt;
        existing.ProcessedAt = processedAt;
        await _bookingRepository.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var existing = await GetTrackedAsync(id, cancellationToken);
        _bookingRepository.Delete(existing);
        await _bookingRepository.SaveChangesAsync(cancellationToken);
    }

    public Task<BookingStatus> ProcessPendingAsync(Guid id, CancellationToken cancellationToken = default) =>
        TransitionAsync(id, reject: false, cancellationToken);

    public Task<BookingStatus> RejectAsync(Guid id, CancellationToken cancellationToken = default) =>
        TransitionAsync(id, reject: true, cancellationToken);

    private async Task<BookingStatus> TransitionAsync(Guid id, bool reject, CancellationToken cancellationToken)
    {
        await EventWriteSynchronization.Gate.WaitAsync(cancellationToken);
        Booking? booking = null;
        Event? eventItem = null;
        try
        {
            booking = await GetTrackedAsync(id, cancellationToken);

            // Повторная обработка не должна повторно возвращать место или подтверждать отказ.
            if (booking.Status == BookingStatus.Rejected ||
                (!reject && booking.Status != BookingStatus.Pending))
                return booking.Status;

            eventItem = await _eventRepository.GetTrackedAsync(
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

            // Статус и место сохраняются одной транзакцией PostgreSQL.
            await _bookingRepository.SaveChangesAsync(cancellationToken);
            return booking.Status;
        }
        catch
        {
            // Не оставляем несохранённый переход в отслеживаемом состоянии.
            if (booking is not null)
                _bookingRepository.Detach(booking);
            if (eventItem is not null)
                _eventRepository.Detach(eventItem);
            throw;
        }
        finally
        {
            EventWriteSynchronization.Gate.Release();
        }
    }

    private async Task<Booking> GetTrackedAsync(Guid id, CancellationToken cancellationToken) =>
        await _bookingRepository.GetTrackedAsync(id, cancellationToken)
        ?? throw new NotFoundException("Booking", id);
}
