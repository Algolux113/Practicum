using PracticumApi.Exceptions;
using PracticumApi.Interfaces;
using PracticumApi.Models;

namespace PracticumApi.Services;

/// <summary>
/// Сервис бронирований с хранилищем в памяти (аналог хранилища событий).
/// </summary>
public class BookingService() : IBookingService
{
    private readonly List<Booking> _bookings = [];

    public List<Booking> GetAll() => _bookings;

    public Booking Get(Guid id)
    {
        var booking = _bookings.FirstOrDefault(x => x.Id == id);
        if (booking is null)
            throw new NotFoundException("Booking", id);

        return booking;
    }

    public Booking Create(Booking booking)
    {
        booking.Id = Guid.NewGuid();
        booking.Status = BookingStatus.Pending;
        booking.CreatedAt = DateTime.UtcNow;

        _bookings.Add(booking);

        return booking;
    }

    public void Update(Booking booking)
    {
        var index = _bookings.FindIndex(x => x.Id == booking.Id);
        if (index == -1)
            throw new NotFoundException("Booking", booking.Id);

        _bookings[index] = booking;
    }

    public void Delete(Guid id)
    {
        var booking = _bookings.FirstOrDefault(x => x.Id == id);
        if (booking is null)
            throw new NotFoundException("Booking", id);

        _bookings.Remove(booking);
    }
}