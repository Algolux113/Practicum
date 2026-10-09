using Microsoft.EntityFrameworkCore;
using PracticumApi.DataAccess;
using PracticumApi.Exceptions;
using PracticumApi.Interfaces;
using PracticumApi.Models;

namespace PracticumApi.Services;

/// <summary>
/// Сервис бронирований с сохранением брони и резерва места в одном контексте.
/// </summary>
public class BookingService(AppDbContext context) : IBookingService
{
    private readonly AppDbContext _context = context;

    public async Task<List<Booking>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await _context.Bookings.AsNoTracking().ToListAsync(cancellationToken);

    public async Task<Booking> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        await _context.Bookings.AsNoTracking().SingleOrDefaultAsync(b => b.Id == id, cancellationToken)
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
        try
        {
            // Читаем актуальный остаток после входа в секцию, даже если событие уже отслеживается.
            var current = await _context.Events.AsNoTracking()
                .SingleOrDefaultAsync(e => e.Id == eventId && !e.IsDeleted, cancellationToken)
                ?? throw new NotFoundException("Event", eventId);
            var eventItem = _context.Events.Local.FirstOrDefault(e => e.Id == eventId);
            if (eventItem is null)
            {
                eventItem = current;
                _context.Events.Attach(eventItem);
            }
            else
            {
                _context.Entry(eventItem).CurrentValues.SetValues(current);
            }

            if (!eventItem.TryReserveSeats())
                throw new NoAvailableSeatsException();

            try
            {
                await _context.Bookings.AddAsync(booking, cancellationToken);
                // EF Core сохраняет бронь и изменение события одной транзакцией.
                await _context.SaveChangesAsync(cancellationToken);
            }
            catch
            {
                _context.Entry(booking).State = EntityState.Detached;
                _context.Entry(eventItem).State = EntityState.Detached;
                throw;
            }
            return booking;
        }
        finally
        {
            EventWriteSynchronization.Gate.Release();
        }
    }

    public async Task UpdateAsync(Booking booking, CancellationToken cancellationToken = default)
    {
        var existing = await GetTrackedAsync(booking.Id, cancellationToken);
        _context.Entry(existing).CurrentValues.SetValues(booking);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var existing = await GetTrackedAsync(id, cancellationToken);
        _context.Bookings.Remove(existing);
        await _context.SaveChangesAsync(cancellationToken);
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
            var current = await GetAsync(id, cancellationToken);
            booking = _context.Bookings.Local.FirstOrDefault(b => b.Id == id);
            if (booking is null)
            {
                booking = current;
                _context.Bookings.Attach(booking);
            }
            else
            {
                _context.Entry(booking).CurrentValues.SetValues(current);
            }

            // Повторная обработка не должна повторно возвращать место или подтверждать отказ.
            if (booking.Status == BookingStatus.Rejected ||
                (!reject && booking.Status != BookingStatus.Pending))
                return booking.Status;

            var currentEvent = await _context.Events.AsNoTracking()
                .SingleOrDefaultAsync(e => e.Id == booking.EventId, cancellationToken);
            if (currentEvent is not null)
            {
                eventItem = _context.Events.Local.FirstOrDefault(e => e.Id == booking.EventId);
                if (eventItem is null)
                {
                    eventItem = currentEvent;
                    _context.Events.Attach(eventItem);
                }
                else
                {
                    _context.Entry(eventItem).CurrentValues.SetValues(currentEvent);
                }
            }

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
            await _context.SaveChangesAsync(cancellationToken);
            return booking.Status;
        }
        catch
        {
            // Не оставляем несохранённый переход в отслеживаемом состоянии.
            if (booking is not null)
                _context.Entry(booking).State = EntityState.Detached;
            if (eventItem is not null)
                _context.Entry(eventItem).State = EntityState.Detached;
            throw;
        }
        finally
        {
            EventWriteSynchronization.Gate.Release();
        }
    }

    private async Task<Booking> GetTrackedAsync(Guid id, CancellationToken cancellationToken)
    {
        var current = await GetAsync(id, cancellationToken);
        var tracked = _context.Bookings.Local.FirstOrDefault(b => b.Id == id);
        if (tracked is null)
        {
            _context.Bookings.Attach(current);
            return current;
        }
        return tracked;
    }
}
