using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace PracticumApi.Models;

public class Booking
{
    private Booking() { }

    public Booking(Guid eventId)
    {
        EventId = eventId;
    }

    [Required]
    public Guid Id { get; set; }

    [Required]
    public Guid EventId { get; set; }

    [JsonIgnore]
    public Event Event { get; set; } = null!;

    [Required]
    public BookingStatus Status { get; set; }

    [Required]
    public DateTime CreatedAt { get; set; }

    public DateTime? ProcessedAt { get; set; }

    public void Confirm()
    {
        Status = BookingStatus.Confirmed;
        ProcessedAt = DateTime.UtcNow;
    }

    public void Reject()
    {
        Status = BookingStatus.Rejected;
        ProcessedAt = DateTime.UtcNow;
    }
}
