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
    public ActionResult<PaginatedResult<Event>> GetAll(
        [FromQuery] string? title = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10
    ) => _eventService.GetAll(title, from, to, page, pageSize);

    [HttpGet("{id:int}")]
    public ActionResult<Event> Get(int id)
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
            StartAt = eventDTO.StartAt,
            EndAt = eventDTO.EndAt,
        };

        _eventService.Add(eventItem);
        return CreatedAtAction(nameof(Get), new { id = eventItem.Id }, eventItem);
    }

    [HttpPut("{id:int}")]
    public IActionResult Update(int id, EventDTO eventDTO)
    {
        var existingEvent = _eventService.Get(id);
        
        existingEvent.Title = eventDTO.Title;
        existingEvent.Description = eventDTO.Description;
        existingEvent.StartAt = eventDTO.StartAt;
        existingEvent.EndAt = eventDTO.EndAt;

        _eventService.Update(existingEvent);

        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public IActionResult Delete(int id)
    {
        _eventService.Delete(id);
        return NoContent();
    }
}
