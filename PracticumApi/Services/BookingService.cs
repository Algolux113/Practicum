using System.Threading;
using PracticumApi.Exceptions;
using PracticumApi.Interfaces;
using PracticumApi.Models;

namespace PracticumApi.Services;

/// <summary>
/// Сервис бронирований с хранилищем в памяти (аналог хранилища событий).
/// </summary>
public class BookingService(IEventService eventService) : IBookingService
{
    private readonly IEventService _eventService = eventService;
    private readonly List<Booking> _bookings = [];

    // Singleton, к которому обращаются и потоки запросов, и фоновый сервис обработки:
    // любой доступ к _bookings идёт под этим замком.
    private readonly Lock _sync = new();

    /// <summary>
    /// Возвращает снимок списка бронирований. Копия, а не внутренний список, чтобы
    /// вызывающий не мог его мутировать и чтобы перечисление у него не падало,
    /// когда параллельный поток добавляет новую бронь.
    /// </summary>
    public List<Booking> GetAll()
    {
        lock (_sync)
            return [.. _bookings];
    }

    public Booking Get(Guid id)
    {
        lock (_sync)
        {
            var booking = _bookings.FirstOrDefault(x => x.Id == id);
            if (booking is null)
                throw new NotFoundException("Booking", id);

            return booking;
        }
    }

    public Task<Booking> CreateBookingAsync(Guid eventId)
    {
        // Объект брони не трогает общее состояние, поэтому собирается до входа в lock —
        // критическая секция ниже держит замок минимально необходимое время.
        var booking = new Booking
        {
            Id = Guid.NewGuid(),
            EventId = eventId,
            Status = BookingStatus.Pending,
            CreatedAt = DateTime.UtcNow,
        };

        // Резерв места и добавление брони — атомарная пара под одним замком, иначе
        // между ними мог бы вклиниться другой вызов CreateBookingAsync.
        lock (_sync)
        {
            _eventService.ReserveSeats(eventId);
            _bookings.Add(booking);
        }

        return Task.FromResult(booking);
    }

    public void Update(Booking booking)
    {
        lock (_sync)
        {
            var index = _bookings.FindIndex(x => x.Id == booking.Id);
            if (index == -1)
                throw new NotFoundException("Booking", booking.Id);

            _bookings[index] = booking;
        }
    }

    public void Delete(Guid id)
    {
        lock (_sync)
        {
            var booking = _bookings.FirstOrDefault(x => x.Id == id);
            if (booking is null)
                throw new NotFoundException("Booking", id);

            _bookings.Remove(booking);
        }
    }
}
