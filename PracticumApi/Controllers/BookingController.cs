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
    [ProducesResponseType(typeof(Booking), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public ActionResult<Booking> Get(Guid id)
    {
        // Если бронь не найдена, BookingService.Get выбрасывает NotFoundException → 404.
        var booking = _bookingService.Get(id);

        return Ok(booking);
    }

    [HttpPost("/events/{id:guid}/book")]
    [ProducesResponseType(typeof(Booking), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Book(Guid id)
    {
        var booking = await _bookingService.CreateBookingAsync(id);

        return Accepted($"/bookings/{booking.Id}", booking);
    }
}