using PracticumApi.Interfaces;
using PracticumApi.Models;

namespace PracticumApi.Services;

public class EventService() : IEventService
{
    private readonly List<Event> Events = [];

    public List<Event> GetAll() => Events;

    public Event? Get(int id) => Events.FirstOrDefault(x => x.Id == id);

    public void Add(Event eventItem)
    {
        eventItem.Id = Events.Count != 0 ? Events.Max(x => x.Id) + 1 : 1;
        Events.Add(eventItem);
    }

    public void Update(Event eventItem)
    {
        var index = Events.FindIndex(x => x.Id == eventItem.Id);
        if(index == -1)
            return;

        Events[index] = eventItem;
    }

    public void Delete(int id)
    {
        var eventItem = Get(id);
        if(eventItem is null)
            return;

        Events.Remove(eventItem);
    }
}
