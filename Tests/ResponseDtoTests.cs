using System.Text.Json;
using Application.DTOs;
using Domain.Models;

namespace Tests;

public class ResponseDtoTests
{
    [Fact]
    public void Responses_ShouldContainOnlyContractFieldsRegardlessOfNavigations()
    {
        // Arrange
        var eventItem = Event.Create("Событие", "Описание", DateTime.UtcNow, DateTime.UtcNow.AddHours(1), 3);
        eventItem.Delete();
        var booking = Booking.Create(eventItem.Id);
        booking.Confirm();
        eventItem.TryReserveSeats();
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        // Act
        using var eventJson = JsonDocument.Parse(JsonSerializer.Serialize(EventResponse.From(eventItem), options));
        using var bookingJson = JsonDocument.Parse(JsonSerializer.Serialize(BookingResponse.From(booking), options));

        // Assert
        Assert.Equal(new[] { "availableSeats", "description", "endAt", "id", "startAt", "title", "totalSeats" },
            eventJson.RootElement.EnumerateObject().Select(property => property.Name).Order());
        Assert.Equal(new[] { "createdAt", "eventId", "id", "processedAt", "status" },
            bookingJson.RootElement.EnumerateObject().Select(property => property.Name).Order());
        Assert.Equal(eventItem.Id, eventJson.RootElement.GetProperty("id").GetGuid());
        Assert.Equal(2, eventJson.RootElement.GetProperty("availableSeats").GetInt32());
        Assert.Equal(eventItem.StartAt, eventJson.RootElement.GetProperty("startAt").GetDateTime());
        Assert.Equal(booking.Id, bookingJson.RootElement.GetProperty("id").GetGuid());
        Assert.Equal(booking.EventId, bookingJson.RootElement.GetProperty("eventId").GetGuid());
        Assert.Equal(booking.ProcessedAt, bookingJson.RootElement.GetProperty("processedAt").GetDateTime());
    }
}
