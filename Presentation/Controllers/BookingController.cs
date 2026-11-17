using Microsoft.AspNetCore.Mvc;
using System.IdentityModel.Tokens.Jwt;
using Application.Interfaces;
using Application.DTOs;
using Domain.Models;
using Microsoft.AspNetCore.Authorization;

namespace Presentation.Controllers;

[ApiController]
[Authorize]
[Route("bookings")]
public class BookingController(IBookingService bookingService) : ControllerBase
{
    private readonly IBookingService _bookingService = bookingService;

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(BookingResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BookingResponse>> Get(Guid id)
    {
        if (!TryGetUserId(out var userId))
            return Unauthorized();

        var booking = await _bookingService.GetAsync(id, userId, CurrentRole, HttpContext.RequestAborted);

        return Ok(BookingResponse.From(booking));
    }

    [HttpPost("/events/{id:guid}/book")]
    [ProducesResponseType(typeof(BookingResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Book(Guid id)
    {
        if (!TryGetUserId(out var userId))
            return Unauthorized();
        var booking = await _bookingService.CreateBookingAsync(id, userId, HttpContext.RequestAborted);

        return Accepted($"/bookings/{booking.Id}", BookingResponse.From(booking));
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Cancel(Guid id)
    {
        if (!TryGetUserId(out var userId))
            return Unauthorized();

        await _bookingService.CancelAsync(id, userId, CurrentRole, HttpContext.RequestAborted);
        return NoContent();
    }

    private bool TryGetUserId(out Guid userId) =>
        Guid.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out userId) && userId != Guid.Empty;

    private UserRole CurrentRole => User.IsInRole(nameof(UserRole.Admin)) ? UserRole.Admin : UserRole.User;
}
