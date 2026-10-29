using Application.DTOs;
using Domain.Models;

namespace Tests;

internal static class EventTestMapping
{
    internal static EventDTO ToDto(this Event eventItem) => new()
    {
        Title = eventItem.Title, Description = eventItem.Description,
        StartAt = eventItem.StartAt, EndAt = eventItem.EndAt, TotalSeats = eventItem.TotalSeats
    };
}
