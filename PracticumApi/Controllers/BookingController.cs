using Microsoft.AspNetCore.Mvc;
using PracticumApi.Interfaces;
using PracticumApi.Models;

namespace PracticumApi.Controllers;

[ApiController]
[Route("bookings")]
public class BookingController(IBookingService bookingService) : ControllerBase
{
    private readonly IBookingService _bookingService = bookingService;

    [HttpGet("{id:guid}")]
    public ActionResult<Booking> Get(Guid id)
    {
        // Если бронь не найдена, BookingService.Get выбрасывает NotFoundException → 404.
        var booking = _bookingService.Get(id);

        return Ok(booking);
    }

    [HttpPost("/events/{id:guid}/book")]
    public async Task<IActionResult> Book(Guid id)
    {
        var booking = await _bookingService.CreateBookingAsync(id);

        return Accepted($"/bookings/{booking.Id}", booking);
    }
}