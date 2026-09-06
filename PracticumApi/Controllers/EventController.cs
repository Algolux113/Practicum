using Microsoft.AspNetCore.Mvc;
using PracticumApi.Models;
using PracticumApi.Interfaces;

namespace PracticumApi.Controllers;

[ApiController]
[Route("events")]
public class EventController(
    IEventService eventService,
    IBookingService bookingService) : ControllerBase
{
    private readonly IEventService _eventService = eventService;
    private readonly IBookingService _bookingService = bookingService;

    [HttpGet]
    public ActionResult<PaginatedResult<Event>> GetAll(
        [FromQuery] string? title = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10
    ) => _eventService.GetAll(title, from, to, page, pageSize);

    [HttpGet("{id:guid}")]
    public ActionResult<Event> Get(Guid id)
    {
        var eventItem = _eventService.Get(id);
        return eventItem;
    }

    [HttpPost]
    public IActionResult Create(EventDTO eventDTO)
    {
        var eventItem = new Event
        {
            Title = eventDTO.Title,
            Description = eventDTO.Description,
            // StartAt/EndAt гарантированно заданы: [Required] + IValidatableObject уже отработали.
            StartAt = eventDTO.StartAt!.Value,
            EndAt = eventDTO.EndAt!.Value,
        };

        _eventService.Add(eventItem);
        return CreatedAtAction(nameof(Get), new { id = eventItem.Id }, eventItem);
    }

    [HttpPut("{id:guid}")]
    public IActionResult Update(Guid id, EventDTO eventDTO)
    {
        // Проверяем существование события; отсутствие — это 404.
        _eventService.Get(id);

        // Собираем новый объект и отдаём его сервису целиком, не мутируя хранимую сущность
        // напрямую: так контракт Update (найти и заменить) остаётся честным.
        var updatedEvent = new Event
        {
            Id = id,
            Title = eventDTO.Title,
            Description = eventDTO.Description,
            StartAt = eventDTO.StartAt!.Value,
            EndAt = eventDTO.EndAt!.Value,
        };

        _eventService.Update(updatedEvent);

        return NoContent();
    }

    [HttpPost("{id:guid}/book")]
    public async Task<IActionResult> Book(Guid id)
    {
        var booking = await _bookingService.CreateBookingAsync(id);

        return Accepted($"/bookings/{booking.Id}", booking);
    }

    [HttpDelete("{id:guid}")]
    public IActionResult Delete(Guid id)
    {
        _eventService.Delete(id);
        return NoContent();
    }
}
