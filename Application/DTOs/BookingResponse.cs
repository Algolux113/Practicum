using Domain.Models;

namespace Application.DTOs;

public sealed record BookingResponse(
    Guid Id,
    Guid EventId,
    BookingStatus Status,
    DateTime CreatedAt,
    DateTime? ProcessedAt)
{
    public static BookingResponse From(Booking booking) => new(
        booking.Id, booking.EventId, booking.Status, booking.CreatedAt, booking.ProcessedAt);
}
