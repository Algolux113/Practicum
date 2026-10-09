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
    public async Task<ActionResult<PaginatedResult<Event>>> GetAll(
        [FromQuery] string? title = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10
    ) => await _eventService.GetAllAsync(title, from, to, page, pageSize, HttpContext.RequestAborted);

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(Event), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<Event>> Get(Guid id)
    {
        var eventItem = await _eventService.GetAsync(id, HttpContext.RequestAborted);
        return eventItem;
    }

    [HttpPost]
    [ProducesResponseType(typeof(Event), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(EventDTO eventDTO)
    {
        // StartAt/EndAt/TotalSeats гарантированно заданы: [Required] + IValidatableObject уже отработали.
        var eventItem = Event.Create(
            eventDTO.Title,
            eventDTO.Description,
            eventDTO.StartAt!.Value,
            eventDTO.EndAt!.Value,
            eventDTO.TotalSeats!.Value);

        await _eventService.AddAsync(eventItem, HttpContext.RequestAborted);
        return CreatedAtAction(nameof(Get), new { id = eventItem.Id }, eventItem);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Update(Guid id, EventDTO eventDTO)
    {
        // Сервис проверит существование и пересчитает места по актуальному состоянию БД.
        var updatedEvent = new Event(eventDTO.Title)
        {
            Id = id,
            Description = eventDTO.Description,
            StartAt = eventDTO.StartAt!.Value,
            EndAt = eventDTO.EndAt!.Value,
            TotalSeats = eventDTO.TotalSeats!.Value,
        };

        await _eventService.UpdateAsync(updatedEvent, HttpContext.RequestAborted);

        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id)
    {
        await _eventService.DeleteAsync(id, HttpContext.RequestAborted);
        return NoContent();
    }
}
