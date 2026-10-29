using Domain.Exceptions;

namespace Domain.Models;

public class Booking
{
    private Booking() { }

    public Guid Id { get; private set; }
    public Guid EventId { get; private set; }
    public Event Event { get; private set; } = null!;
    public BookingStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? ProcessedAt { get; private set; }

    public static Booking Create(Guid eventId)
    {
        if (eventId == Guid.Empty)
            throw new ValidationException(nameof(eventId), "Идентификатор события обязателен.");
        return new Booking
        {
            Id = Guid.NewGuid(), EventId = eventId,
            Status = BookingStatus.Pending, CreatedAt = DateTime.UtcNow
        };
    }

    public void Confirm()
    {
        if (Status == BookingStatus.Confirmed)
            return;
        if (Status == BookingStatus.Rejected)
            throw new ValidationException(nameof(Status), "Отклонённую бронь нельзя подтвердить.");
        Status = BookingStatus.Confirmed;
        ProcessedAt = DateTime.UtcNow;
    }

    public void Reject()
    {
        if (Status == BookingStatus.Rejected)
            return;
        Status = BookingStatus.Rejected;
        ProcessedAt = DateTime.UtcNow;
    }
}