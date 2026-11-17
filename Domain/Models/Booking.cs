using Domain.Exceptions;

namespace Domain.Models;

public class Booking
{
    private Booking() { }

    public Guid Id { get; private set; }
    public Guid EventId { get; private set; }
    public Guid UserId { get; private set; }
    public Event Event { get; private set; } = null!;
    public BookingStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? ProcessedAt { get; private set; }

    public static Booking Create(Guid eventId, Guid userId)
    {
        if (eventId == Guid.Empty)
            throw new ValidationException(nameof(eventId), "Идентификатор события обязателен.");
        if (userId == Guid.Empty)
            throw new ValidationException(nameof(userId), "Идентификатор пользователя обязателен.");

        return new Booking
        {
            Id = Guid.NewGuid(), EventId = eventId, UserId = userId,
            Status = BookingStatus.Pending, CreatedAt = DateTime.UtcNow
        };
    }

    public void Confirm()
    {
        if (Status == BookingStatus.Confirmed)
            return;
        if (Status == BookingStatus.Rejected)
            throw new ValidationException(nameof(Status), "Отклонённую бронь нельзя подтвердить.");
        if (Status == BookingStatus.Cancelled)
            throw new ValidationException(nameof(Status), "Отменённую бронь нельзя подтвердить.");
        Status = BookingStatus.Confirmed;
        ProcessedAt = DateTime.UtcNow;
    }

    public void Reject()
    {
        if (Status is BookingStatus.Rejected or BookingStatus.Cancelled)
            return;
        Status = BookingStatus.Rejected;
        ProcessedAt = DateTime.UtcNow;
    }

    public void Cancel()
    {
        if (Status == BookingStatus.Cancelled)
            return;
        if (Status == BookingStatus.Rejected)
            throw new ValidationException(nameof(Status), "Отклонённую бронь нельзя отменить.");
        Status = BookingStatus.Cancelled;
        ProcessedAt = DateTime.UtcNow;
    }
}