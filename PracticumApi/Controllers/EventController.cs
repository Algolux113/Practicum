using Microsoft.AspNetCore.Mvc;
using PracticumApi.Models;
using PracticumApi.Interfaces;

namespace PracticumApi.Controllers;

[ApiController]
[Route("events")]
public class EventController(IEventService eventService) : ControllerBase
{
    private readonly IEventService _eventService = eventService;

    [HttpGet]
    [ProducesResponseType(typeof(PaginatedResult<Event>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public ActionResult<PaginatedResult<Event>> GetAll(
        [FromQuery] string? title = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10
    ) => _eventService.GetAll(title, from, to, page, pageSize);

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(Event), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public ActionResult<Event> Get(Guid id)
    {
        var eventItem = _eventService.Get(id);
        return eventItem;
    }

    [HttpPost]
    [ProducesResponseType(typeof(Event), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public IActionResult Create(EventDTO eventDTO)
    {
        // StartAt/EndAt/TotalSeats гарантированно заданы: [Required] + IValidatableObject уже отработали.
        var eventItem = Event.Create(
            eventDTO.Title,
            eventDTO.Description,
            eventDTO.StartAt!.Value,
            eventDTO.EndAt!.Value,
            eventDTO.TotalSeats!.Value);

        _eventService.Add(eventItem);
        return CreatedAtAction(nameof(Get), new { id = eventItem.Id }, eventItem);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public IActionResult Update(Guid id, EventDTO eventDTO)
    {
        // Проверяем существование события; отсутствие — это 404.
        var existingEvent = _eventService.Get(id);

        // Собираем новый объект и отдаём его сервису целиком, не мутируя хранимую сущность
        // напрямую: так контракт Update (найти и заменить) остаётся честным.
        // AvailableSeats пересчитывается относительно нового TotalSeats, а не переносится
        // как есть — иначе смена вместимости могла бы нарушить инвариант
        // AvailableSeats <= TotalSeats (или "потерять" вновь добавленные места).
        var updatedEvent = new Event
        {
            Id = id,
            Title = eventDTO.Title,
            Description = eventDTO.Description,
            StartAt = eventDTO.StartAt!.Value,
            EndAt = eventDTO.EndAt!.Value,
            TotalSeats = eventDTO.TotalSeats!.Value,
            AvailableSeats = existingEvent.RecalculateAvailableSeats(eventDTO.TotalSeats!.Value),
        };

        _eventService.Update(updatedEvent);

        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public IActionResult Delete(Guid id)
    {
        _eventService.Delete(id);
        return NoContent();
    }
}
