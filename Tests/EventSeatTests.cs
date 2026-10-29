using Domain.Exceptions;
using Domain.Models;

namespace Tests;

public class EventSeatTests
{
    [Fact]
    public void Entities_ShouldExposeNoPublicConstructorsOrSetters()
    {
        foreach (var type in new[] { typeof(Event), typeof(Booking) })
        {
            Assert.Empty(type.GetConstructors());
            Assert.All(type.GetProperties(), property => Assert.False(property.SetMethod?.IsPublic == true));
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_WithInvalidCapacity_ShouldReject(int totalSeats)
    {
        Assert.Throws<ValidationException>(() =>
            Event.Create("Событие", null, DateTime.UtcNow, DateTime.UtcNow.AddHours(1), totalSeats));
    }

    [Fact]
    public void Update_ShouldPreserveIdentityAndOccupiedSeatsAndRejectInvalidChangesAtomically()
    {
        var eventItem = Event.Create("До", "Описание", DateTime.UtcNow, DateTime.UtcNow.AddHours(1), 5);
        var id = eventItem.Id;
        eventItem.TryReserveSeats(3);
        var start = eventItem.StartAt.AddDays(1);

        eventItem.Update("После", "Новое описание", start, start.AddHours(2), 4);

        Assert.Equal(id, eventItem.Id);
        Assert.NotEqual(Guid.Empty, id);
        Assert.Equal(1, eventItem.AvailableSeats);
        Assert.Throws<ValidationException>(() =>
            eventItem.Update("Не сохранять", null, start.AddDays(1), start.AddDays(2), 2));
        Assert.Equal("После", eventItem.Title);
        Assert.Equal("Новое описание", eventItem.Description);
        Assert.Equal(start, eventItem.StartAt);
        Assert.Equal(start.AddHours(2), eventItem.EndAt);
        Assert.Equal(4, eventItem.TotalSeats);
        Assert.Equal(1, eventItem.AvailableSeats);
        Assert.Throws<NotSupportedException>(() => ((ICollection<Booking>)eventItem.Bookings).Add(Booking.Create(id)));
    }

    [Fact]
    public void BookingFactoryAndTransitions_ShouldPreserveIdentityAndRejectReactivation()
    {
        Assert.Throws<ValidationException>(() => Booking.Create(Guid.Empty));
        var eventId = Guid.NewGuid();
        var booking = Booking.Create(eventId);
        var id = booking.Id;
        Assert.NotEqual(Guid.Empty, id);
        Assert.Equal(eventId, booking.EventId);
        Assert.Equal(BookingStatus.Pending, booking.Status);
        Assert.Equal(DateTimeKind.Utc, booking.CreatedAt.Kind);
        Assert.Null(booking.ProcessedAt);
        booking.Confirm();
        var confirmedAt = booking.ProcessedAt;
        booking.Confirm();
        Assert.Equal(confirmedAt, booking.ProcessedAt);
        booking.Reject();
        var rejectedAt = booking.ProcessedAt;
        booking.Reject();
        Assert.Equal(rejectedAt, booking.ProcessedAt);
        Assert.Throws<ValidationException>(() => booking.Confirm());
        Assert.Equal(BookingStatus.Rejected, booking.Status);
        Assert.Equal(id, booking.Id);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void SeatOperations_WithNonPositiveCount_ShouldRejectWithoutChangingCapacity(int count)
    {
        // Arrange
        var eventItem = Event.Create("Событие", null, DateTime.UtcNow, DateTime.UtcNow.AddHours(1), 3);

        // Act / Assert
        Assert.Throws<ValidationException>(() => eventItem.TryReserveSeats(count));
        Assert.Throws<ValidationException>(() => eventItem.ReleaseSeats(count));
        Assert.Equal(3, eventItem.AvailableSeats);
    }

    [Fact]
    public void ReleaseSeats_WithLargeCount_ShouldClampWithoutOverflow()
    {
        var eventItem = Event.Create("Событие", null, DateTime.UtcNow, DateTime.UtcNow.AddHours(1), 3);
        eventItem.TryReserveSeats();

        eventItem.ReleaseSeats(int.MaxValue);

        Assert.Equal(3, eventItem.AvailableSeats);
    }
}
