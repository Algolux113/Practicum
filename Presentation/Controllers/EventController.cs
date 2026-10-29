using Microsoft.AspNetCore.Mvc;
using Application.DTOs;
using Application.Interfaces;

namespace Presentation.Controllers;

[ApiController]
[Route("events")]
public class EventController(IEventService eventService) : ControllerBase
{
    private readonly IEventService _eventService = eventService;

    [HttpGet]
    [ProducesResponseType(typeof(PaginatedResult<EventResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PaginatedResult<EventResponse>>> GetAll(
        [FromQuery] string? title = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10
    )
    {
        var result = await _eventService.GetAllAsync(title, from, to, page, pageSize, HttpContext.RequestAborted);
        return new PaginatedResult<EventResponse>(result.Items.Select(EventResponse.From).ToList(),
            result.TotalCount, result.Page, result.PageSize);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(EventResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<EventResponse>> Get(Guid id)
    {
        var eventItem = await _eventService.GetAsync(id, HttpContext.RequestAborted);
        return EventResponse.From(eventItem);
    }

    [HttpPost]
    [ProducesResponseType(typeof(EventResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(EventDTO eventDTO)
    {
        var eventItem = await _eventService.CreateAsync(eventDTO, HttpContext.RequestAborted);
        return CreatedAtAction(nameof(Get), new { id = eventItem.Id }, EventResponse.From(eventItem));
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Update(Guid id, EventDTO eventDTO)
    {
        await _eventService.UpdateAsync(id, eventDTO, HttpContext.RequestAborted);

        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id)
    {
        await _eventService.DeleteAsync(id, HttpContext.RequestAborted);
        return NoContent();
    }
}
