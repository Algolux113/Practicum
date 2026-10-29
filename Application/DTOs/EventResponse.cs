using Domain.Models;

namespace Application.DTOs;

public sealed record EventResponse(
    Guid Id,
    string? Title,
    string? Description,
    DateTime StartAt,
    DateTime EndAt,
    int TotalSeats,
    int AvailableSeats)
{
    public static EventResponse From(Event eventItem) => new(
        eventItem.Id, eventItem.Title, eventItem.Description,
        eventItem.StartAt, eventItem.EndAt, eventItem.TotalSeats, eventItem.AvailableSeats);
}
