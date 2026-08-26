using PracticumApi.Interfaces;
using PracticumApi.Models;
using PracticumApi.Exceptions;

namespace PracticumApi.Services;

public class EventService() : IEventService
{
    private readonly List<Event> Events = [];

    public PaginatedResult<Event> GetAll(
        string? title = null,
        DateTime? from = null,
        DateTime? to = null,
        int page = 1,
        int pageSize = 10)
    {
        // Валидация параметров пагинации
        if (page < 1)
            throw new ValidationException(nameof(page), "Номер страницы должен быть больше или равен 1");
        
        if (pageSize < 1)
            throw new ValidationException(nameof(pageSize), "Размер страницы должен быть больше или равен 1");
        
        if (pageSize > 100)
            throw new ValidationException(nameof(pageSize), "Размер страницы не может превышать 100");

        var query = Events.AsEnumerable();

        if(!string.IsNullOrWhiteSpace(title))
            query = query.Where(x => !string.IsNullOrWhiteSpace(x.Title) && x.Title.Contains(title, StringComparison.OrdinalIgnoreCase));

        if(from.HasValue)
            query = query.Where(x => x.StartAt >= from.Value);

        if(to.HasValue)
            query = query.Where(x => x.EndAt <= to.Value);

        var items = query.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return new PaginatedResult<Event>(items, query.Count(), page, pageSize);
    }

    public Event Get(Guid id)
    {
        var eventItem = Events.FirstOrDefault(x => x.Id == id);
        if (eventItem is null)
            throw new NotFoundException("Event", id);
        
        return eventItem;
    }

    public void Add(Event eventItem)
    {
        eventItem.Id = Guid.NewGuid();
        Events.Add(eventItem);
    }

    public void Update(Event eventItem)
    {
        var index = Events.FindIndex(x => x.Id == eventItem.Id);
        if(index == -1)
            throw new NotFoundException("Event", eventItem.Id);

        Events[index] = eventItem;
    }

    public void Delete(Guid id)
    {
        var eventItem = Events.FirstOrDefault(x => x.Id == id);
        if(eventItem is null)
            throw new NotFoundException("Event", id);

        Events.Remove(eventItem);
    }
}
